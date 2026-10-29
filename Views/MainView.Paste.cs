using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Mdhub.Models;
using Mdhub.Text;

namespace Mdhub.Views;

// An image on the clipboard: a copied image file, or bitmap data already encoded as PNG.
internal sealed record ClipboardImage(string? FilePath, byte[]? PngBytes);

// Pasting an image into a Markdown note saves it under .assets and inserts a relative link.
public partial class MainView
{
    // Test seams: replace clipboard reading and the timestamp used for asset names.
    internal Func<Task<ClipboardImage?>>? ClipboardImageOverride { get; set; }
    internal Func<DateTime> PasteClock { get; set; } = () => DateTime.Now;

    private async Task PasteAsync()
    {
        if (_active is not { } document) return;
        var image = document.IsMarkdown ? await ReadClipboardImageAsync() : null;
        if (image is null) { TextEditor.Edit("paste"); return; }
        await RunAsync(() => PasteImageAsync(document, image));
    }

    private async Task PasteImageAsync(NoteDocument document, ClipboardImage image)
    {
        var editor = document.Editor;
        var (start, end) = (Math.Min(editor.SelectionStart, editor.SelectionEnd), Math.Max(editor.SelectionStart, editor.SelectionEnd));
        if (document.Path is null && !await SaveAsync(document, false)) return;
        if (document.Path is not { } notePath || Path.GetDirectoryName(notePath) is not { } directory) return;

        var assets = Path.Combine(directory, ImageAssets.FolderName);
        Directory.CreateDirectory(assets);
        var extension = image.FilePath is { } source ? Path.GetExtension(source) : ".png";
        var name = ImageAssets.FileName(notePath, extension, PasteClock(), candidate => File.Exists(Path.Combine(assets, candidate)));
        var target = Path.Combine(assets, name);
        if (image.FilePath is { } file) File.Copy(file, target);
        else File.WriteAllBytes(target, image.PngBytes ?? []);

        // Restore the caret captured before any save dialog, then insert as one undoable edit.
        var length = editor.Text?.Length ?? 0;
        editor.SelectionStart = Math.Min(start, length);
        editor.SelectionEnd = Math.Min(end, length);
        var readOnly = editor.IsReadOnly; // the busy state makes editors read-only
        editor.IsReadOnly = false;
        try { editor.SelectedText = ImageAssets.MarkdownLink(name); }
        finally { editor.IsReadOnly = readOnly; }
        editor.Focus();
    }

    private async Task<ClipboardImage?> ReadClipboardImageAsync()
    {
        if (ClipboardImageOverride is { } read) return await read();
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return null;
        try
        {
            var files = await clipboard.TryGetFilesAsync();
            if (files?.Select(item => item.TryGetLocalPath()).FirstOrDefault(path => path is not null && File.Exists(path) && ImageAssets.IsImageFile(path)) is { } filePath)
                return new ClipboardImage(filePath, null);
            if (files is { Length: > 0 }) return null;
            using var bitmap = await clipboard.TryGetBitmapAsync();
            if (bitmap is null) return null;
            using var png = new MemoryStream();
            bitmap.Save(png);
            return new ClipboardImage(null, png.ToArray());
        }
        catch (Exception) { return null; }
    }
}