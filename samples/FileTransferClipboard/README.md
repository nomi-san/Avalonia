# FileTransferClipboard Sample

Demonstrates **eager file copy to clipboard** with progress UI — a cross-platform approach that works on macOS (NSPasteboard), Linux/X11, and Windows.

## Problem Statement

On **Windows**, the `IDataObject` COM interface supports **lazy/deferred rendering**: when you place data on the clipboard, the actual file bytes are only transferred when the user pastes (e.g., Explorer calls `IStream::Read`). This enables:
- Progress UI at paste time
- Knowing the destination path via shell interfaces (e.g., `IFileOperation`, `CFSTR_TARGETCLSID`)

On **macOS** and **Linux/X11**, there is **no equivalent mechanism for clipboard paste operations**:

### macOS Limitations
- `NSPasteboard` requires placing actual `public.file-url` entries pointing to **existing files on disk**
- `NSFilePromiseProvider` (which provides lazy file delivery) **only works for drag-and-drop**, not for clipboard paste to Finder
- There is **no callback** to discover where Finder will paste the file
- There is **no way** to intercept the Finder paste operation to show progress

### Linux/X11 Limitations
- The X11 CLIPBOARD selection uses `text/uri-list` MIME type for file operations
- File managers expect `file://` URIs pointing to **existing files on disk**
- The INCR protocol handles large data transfer between X11 clients, but this is for the clipboard data itself, not for the file content
- There is **no protocol** to discover the paste destination
- There is **no way** to intercept the file manager paste operation

## Solution: Eager Copy with Progress

Since both platforms require files to exist on disk before they can be placed on the clipboard, the approach is:

1. **Download/transfer** files from a remote source to a local temp directory
2. **Show progress UI** during the transfer (this is where you get progress — during download, not during paste)
3. **Place file paths** on the system clipboard using Avalonia's cross-platform clipboard API
4. **User pastes** in Finder/file manager — the OS handles copying from temp to destination

```
┌─────────────────────────────────────────────────────────┐
│ Your App (this sample)                                  │
│                                                         │
│  1. User clicks "Copy"                                  │
│  2. Download from URL ──────► Progress bar (0-100%)     │
│  3. Save to temp file                                   │
│  4. clipboard.SetFileAsync(tempFile)                    │
│     ├─ macOS: NSPasteboard ← public.file-url           │
│     ├─ Linux: CLIPBOARD   ← text/uri-list              │
│     └─ Win32: OLE clipboard ← CF_HDROP                 │
│                                                         │
└─────────────────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────┐
│ File Manager (Finder / Nautilus / Explorer)              │
│                                                         │
│  User presses Cmd+V / Ctrl+V                            │
│  File manager reads clipboard → copies file from temp   │
│  (No progress callback to your app — OS handles this)   │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

## Platform-Specific Details

### macOS (Avalonia.Native backend)
- Avalonia places `public.file-url` UTI on the `NSPasteboard`
- The native code in `clipboard.mm` handles `WriteableClipboardItem` with `NSPasteboardWriting`
- File reference URIs (`/.file/id=...`) are resolved to path URIs automatically

### Linux/X11 (Avalonia.X11 backend)
- Avalonia sets the `CLIPBOARD` selection owner via `XSetSelectionOwner`
- When another app requests the data, it responds with `text/uri-list` encoded as UTF-8
- The `ClipboardUriListHelper` handles `file://` URI encoding with CR+LF line endings (RFC 2483)
- For large payloads, the INCR (incremental) protocol is used automatically

### Windows (Avalonia.Win32 backend)
- Avalonia uses OLE clipboard with `CF_HDROP` for file paths
- This sample works on Windows too, but Windows also supports the more advanced lazy approach via `IDataObject`/`IStream`

## Running

```bash
cd samples/FileTransferClipboard
dotnet run
```

## Key Takeaway

If you need to "copy" remote/internet files to the clipboard on macOS or Linux, you **must** download them first (eager approach). The progress UI is shown during the download phase. Once files are on the clipboard, the paste operation is handled entirely by the OS file manager with no application callback.
