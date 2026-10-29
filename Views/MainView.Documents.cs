using Avalonia.Media;
using Avalonia.Platform.Storage;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// Document workflows: file dialogs, duplicate tabs, saving, and unsaved-change decisions.
public partial class MainView
{
    private static readonly FilePickerFileType TextFiles = new("Text and Markdown") { Patterns = ["*.txt", "*.md", "*.markdown"] };
    private NoteDocument AddDocument(string? path, string text, string? name = null, System.Text.Encoding? encoding = null)
    {
        var document = new NoteDocument
        {
            Path = path, SavedText = text, UntitledName = name ?? $"Untitled {++_untitled}.md",
            Encoding = encoding ?? TextFileStore.DefaultEncoding
        };
        document.Editor.Text = text;
        document.Editor.TextWrapping = _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        document.Editor.TextChanged += (_, _) =>
        {
            document.Refresh();
            if (document.IsDirty) _sessionNote = null;
            if (_active == document) { UpdateStatus(); MdEditor.ScheduleRefresh(); }
            if (GlobalSearch.IsVisible && !_busy) GlobalSearch.Schedule();
        };
        _documents.Add(document);
        FileTabs.SelectedDocument = document;
        return document;
    }

    private async Task OpenAsync()
    {
        var files = await Host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open files", AllowMultiple = true, FileTypeFilter = [FilePickerFileTypes.All, TextFiles]
        });
        foreach (var file in files)
        {
            using (file) await OpenPathAsync(file.TryGetLocalPath() ?? throw new IOException("Choose a local file."));
        }
    }

    private async Task OpenPathAsync(string path)
    {
        path = Path.GetFullPath(path);
        var existing = _documents.FirstOrDefault(d => SamePath(d.Path, path));
        if (existing is not null) { FileTabs.SelectedDocument = existing; return; }
        var kind = await Task.Run(() => FileSupport.Detect(path));
        if (kind == SupportedFileKind.Unsupported) { UpdateStatus("Unsupported file type or text encoding · " + path); return; }
        if (kind == SupportedFileKind.Image)
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = File.OpenRead(path);
                return new Avalonia.Media.Imaging.Bitmap(stream);
            });
            var imageDocument = new NoteDocument { Path = path, Image = bitmap };
            _documents.Add(imageDocument);
            FileTabs.SelectedDocument = imageDocument;
            return;
        }
        (string Text, System.Text.Encoding Encoding) loaded;
        try { loaded = await TextFileStore.ReadAsync(path); }
        catch (System.Text.DecoderFallbackException) { UpdateStatus("Unsupported text encoding · " + path); return; }
        if (!FileSupport.IsText(loaded.Text)) { UpdateStatus("Unsupported binary file · " + path); return; }
        AddDocument(path, loaded.Text, encoding: loaded.Encoding);
    }

    private static bool SamePath(string? a, string b) => string.Equals(a, b,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Test seam: headless UI tests replace the system save picker.
    internal Func<NoteDocument, Task<string?>>? SaveFilePickerOverride { get; set; }

    private async Task<bool> SaveAsync(NoteDocument document, bool saveAs)
    {
        if (document.IsImage) { UpdateStatus("Images are view-only."); return true; }
        var path = document.Path;
        if (saveAs || path is null)
        {
            if (SaveFilePickerOverride is not null) path = await SaveFilePickerOverride(document);
            else
            {
                using var file = await Host.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save note", SuggestedFileName = path is null ? document.UntitledName : Path.GetFileName(path),
                    DefaultExtension = document.IsMarkdown ? "md" : "txt",
                    FileTypeChoices = [TextFiles, FilePickerFileTypes.All], ShowOverwritePrompt = true
                });
                if (file is null) return false;
                path = file.TryGetLocalPath() ?? throw new IOException("Choose a local file.");
            }
            if (path is null) return false;
            if (_documents.Any(d => d != document && SamePath(d.Path, path)))
                throw new IOException("That file is already open in another tab. Save to a different name.");
        }
        await TextFileStore.WriteAsync(path, document.Text, document.Encoding);
        var isNewPath = document.Path is null || !SamePath(document.Path, Path.GetFullPath(path));
        document.Path = path;
        document.SavedText = document.Text;
        document.Refresh();
        if (_active == document) MdEditor.ShowDocument(document.IsMarkdown ? document : null);
        UpdateContentLayout();
        UpdateStatus("Saved · " + path);
        MdEditor.ScheduleRefresh();
        if (isNewPath && FolderExplorer.Contains(path)) await FolderExplorer.RevealCreatedAsync(path);
        return true;
    }

    private async Task<bool> ConfirmCloseAsync(NoteDocument document)
    {
        if (!document.IsDirty) return true;
        FileTabs.SelectedDocument = document;
        var answer = await DialogService.ShowAsync(Host, "Unsaved changes", $"Save changes to {document.Title.TrimStart('●', ' ')}?", "Save", "Discard", "Cancel");
        return answer == "Discard" || (answer == "Save" && await SaveAsync(document, false));
    }

    private async Task CloseDocumentAsync(NoteDocument document)
    {
        if (!await ConfirmCloseAsync(document)) return;
        var index = _documents.IndexOf(document);
        _documents.Remove(document);
        document.Image?.Dispose();
        if (_documents.Count == 0) AddDocument(null, "");
        else if (_active == document) FileTabs.SelectedDocument = _documents[Math.Min(index, _documents.Count - 1)];
    }
}
