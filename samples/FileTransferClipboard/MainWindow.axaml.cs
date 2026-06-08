using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace FileTransferClipboard;

/// <summary>
/// Demonstrates "eager copy" file transfer to clipboard for macOS (NSPasteboard) and Linux (X11).
///
/// On Windows, IDataObject allows lazy/deferred rendering — the shell queries for file data only
/// when the user pastes (e.g., via IStream or CFSTR_SHELLIDLIST). This enables progress UI at
/// paste time, and the destination path is known through shell interfaces.
///
/// On macOS, NSPasteboard only supports NSFilePromiseProvider for drag-and-drop (not paste).
/// For clipboard paste to Finder, you must place actual file:// URLs pointing to existing files.
/// There is NO callback mechanism to know where Finder will paste the file.
///
/// On Linux/X11, the clipboard uses the text/uri-list MIME type for file operations.
/// Files must exist on disk as file:// URIs. There is NO mechanism to know the paste destination.
///
/// The workaround (demonstrated here):
/// 1. Download/transfer files eagerly to a temp directory with progress UI.
/// 2. Place the resulting file paths on the system clipboard.
/// 3. The user pastes in their file manager, which copies from the temp location.
///
/// This gives you full control over the download/transfer progress UI, even though you cannot
/// intercept the final file-manager paste operation to show progress there.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly HttpClient s_httpClient = new();
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        UpdatePlatformInfo();
    }

    private void UpdatePlatformInfo()
    {
        string platform;
        string mechanism;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            platform = "macOS";
            mechanism = "NSPasteboard with public.file-url UTI. "
                + "Note: NSFilePromiseProvider only works for drag-and-drop, not clipboard paste to Finder. "
                + "Files must exist on disk before placing URLs on the pasteboard.";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            platform = "Linux (X11)";
            mechanism = "X11 CLIPBOARD selection with text/uri-list MIME type (RFC 2483). "
                + "File managers expect file:// URIs pointing to existing files. "
                + "INCR protocol is used for large data transfers between X11 clients.";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            platform = "Windows";
            mechanism = "Win32 OLE clipboard with CF_HDROP / CFSTR_SHELLIDLIST. "
                + "Windows supports lazy IDataObject with IStream for deferred rendering. "
                + "This sample uses the eager approach which also works on Windows.";
        }
        else
        {
            platform = "Unknown";
            mechanism = "Unknown platform clipboard mechanism.";
        }

        PlatformInfoText.Text = $"Platform: {platform} | Clipboard mechanism: {mechanism}";
    }

    private async void OnCopyToClipboard(object? sender, RoutedEventArgs e)
    {
        // Cancel any previous operation
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var url = UrlInput.Text?.Trim();
        if (string.IsNullOrEmpty(url))
        {
            StatusText.Text = "Please enter a URL.";
            return;
        }

        CopyToClipboardButton.IsEnabled = false;
        TransferProgress.Value = 0;
        ProgressText.Text = "";
        FilePathText.Text = "";
        ClipboardInfoText.Text = "";

        try
        {
            StatusText.Text = "Starting download...";

            // Step 1: Download file to temp directory with progress
            var tempDir = Path.Combine(Path.GetTempPath(), "avalonia-file-transfer-sample");
            Directory.CreateDirectory(tempDir);

            var fileName = GetFileNameFromUrl(url);
            var tempFilePath = Path.Combine(tempDir, fileName);

            await DownloadFileWithProgressAsync(url, tempFilePath, ct);

            if (ct.IsCancellationRequested)
                return;

            // Step 2: Place the file on the clipboard using Avalonia's cross-platform API
            // This works identically on macOS (public.file-url) and X11 (text/uri-list)
            StatusText.Text = "Placing file on clipboard...";

            if (TopLevel.GetTopLevel(this) is { Clipboard: { } clipboard, StorageProvider: { } storageProvider })
            {
                var storageFile = await storageProvider.TryGetFileFromPathAsync(tempFilePath);
                if (storageFile != null)
                {
                    await clipboard.SetFileAsync(storageFile);

                    StatusText.Text = "✓ File downloaded and placed on clipboard!";
                    FilePathText.Text = $"Temp file: {tempFilePath}";
                    ClipboardInfoText.Text = GetClipboardExplanation();
                }
                else
                {
                    StatusText.Text = "Error: Could not create storage file reference.";
                }
            }
            else
            {
                StatusText.Text = "Error: Clipboard not available.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Download cancelled.";
            TransferProgress.Value = 0;
            ProgressText.Text = "";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            CopyToClipboardButton.IsEnabled = true;
        }
    }

    private async Task DownloadFileWithProgressAsync(string url, string destinationPath, CancellationToken ct)
    {
        using var response = await s_httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        var totalMb = totalBytes.HasValue ? totalBytes.Value / (1024.0 * 1024.0) : 0;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;
        var lastProgressUpdate = DateTime.MinValue;

        while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
            totalRead += bytesRead;

            // Throttle UI updates to avoid overwhelming the dispatcher
            var now = DateTime.UtcNow;
            if ((now - lastProgressUpdate).TotalMilliseconds < 50)
                continue;
            lastProgressUpdate = now;

            var readMb = totalRead / (1024.0 * 1024.0);

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                var percent = (double)totalRead / totalBytes.Value * 100;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TransferProgress.IsIndeterminate = false;
                    TransferProgress.Value = percent;
                    ProgressText.Text = $"{readMb:F2} MB / {totalMb:F2} MB ({percent:F1}%)";
                    StatusText.Text = "Downloading...";
                });
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    TransferProgress.IsIndeterminate = true;
                    ProgressText.Text = $"{readMb:F2} MB downloaded";
                    StatusText.Text = "Downloading (size unknown)...";
                });
            }
        }

        // Final update
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            TransferProgress.IsIndeterminate = false;
            TransferProgress.Value = 100;
            var finalMb = totalRead / (1024.0 * 1024.0);
            ProgressText.Text = $"Download complete: {finalMb:F2} MB";
        });
    }

    private static string GetFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrEmpty(name))
                return name;
        }
        catch
        {
            // Ignored
        }

        return $"download_{DateTime.Now:yyyyMMdd_HHmmss}";
    }

    private static string GetClipboardExplanation()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macOS: The file URL was placed on NSPasteboard as 'public.file-url'. "
                + "You can now Cmd+V in Finder to paste the file. "
                + "Finder will copy the file from the temp location to the current directory. "
                + "Note: There is no API to intercept or know the destination path.";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "Linux/X11: The file URI was placed on the CLIPBOARD selection as 'text/uri-list'. "
                + "You can now Ctrl+V in your file manager (Nautilus, Dolphin, Thunar, etc.) to paste. "
                + "The file manager will copy from the temp location. "
                + "Note: There is no protocol to know the destination path.";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "Windows: The file was placed on clipboard as CF_HDROP. "
                + "You can now Ctrl+V in Explorer to paste. "
                + "On Windows, IDataObject with IStream could be used for lazy rendering instead.";
        }

        return "File placed on clipboard.";
    }
}
