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
            if (_active == document) { UpdateStatus(); MdEditor.ScheduleRefresh(); }
        };
        _documents.Add(document);
        FileTabs.SelectedDocument = document;
        return document;
    }

    private async Task OpenAsync()
    {
        var files = await Host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open text or Markdown files", AllowMultiple = true, FileTypeFilter = [TextFiles, FilePickerFileTypes.All]
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
        var loaded = await TextFileStore.ReadAsync(path);
        if (loaded.Text.Contains('\0')) throw new IOException("This appears to be a binary file. Choose a text or Markdown file.");
        AddDocument(path, loaded.Text, encoding: loaded.Encoding);
    }

    private static bool SamePath(string? a, string b) => string.Equals(a, b,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private async Task<bool> SaveAsync(NoteDocument document, bool saveAs)
    {
        var path = document.Path;
        if (saveAs || path is null)
        {
            using var file = await Host.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save note", SuggestedFileName = path is null ? document.UntitledName : Path.GetFileName(path),
                DefaultExtension = document.IsMarkdown ? "md" : "txt",
                FileTypeChoices = [TextFiles, FilePickerFileTypes.All], ShowOverwritePrompt = true
            });
            if (file is null) return false;
            path = file.TryGetLocalPath() ?? throw new IOException("Choose a local file.");
            if (_documents.Any(d => d != document && SamePath(d.Path, path)))
                throw new IOException("That file is already open in another tab. Save to a different name.");
        }
        await TextFileStore.WriteAsync(path, document.Text, document.Encoding);
        document.Path = path;
        document.SavedText = document.Text;
        document.Refresh();
        UpdateStatus("Saved · " + path);
        MdEditor.ScheduleRefresh();
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
        if (_documents.Count == 0) AddDocument(null, "");
        else if (_active == document) FileTabs.SelectedDocument = _documents[Math.Min(index, _documents.Count - 1)];
    }
}
