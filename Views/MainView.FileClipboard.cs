using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Mdhub.Views;

// Bug 85: explorer Copy/Cut (context menu or Ctrl+C/X) also put the files themselves on the system clipboard,
// so the clipboard holds the file, not stale text, and other apps (File Explorer) can paste it.
public partial class MainView
{
    // Test seam: the paths last published to the system clipboard.
    internal IReadOnlyList<string>? PublishedClipboardFiles { get; private set; }

    private async Task PublishClipboardFilesAsync(IReadOnlyList<string> paths)
    {
        PublishedClipboardFiles = paths;
        if (TopLevel.GetTopLevel(this) is not { Clipboard: { } clipboard, StorageProvider: { } storage }) return;
        try
        {
            var data = new DataTransfer();
            foreach (var path in paths)
            {
                IStorageItem? item = Directory.Exists(path) ? await storage.TryGetFolderFromPathAsync(path) : await storage.TryGetFileFromPathAsync(path);
                if (item is null) continue;
                var entry = new DataTransferItem();
                entry.Set(DataFormat.File, item);
                data.Add(entry);
            }
            await clipboard.SetDataAsync(data);
        }
        catch (Exception)
        {
            try { await clipboard.ClearAsync(); } catch (Exception) { } // never leave the old text there
        }
    }
}
