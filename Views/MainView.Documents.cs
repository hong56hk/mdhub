using Avalonia.Media;
using Avalonia.Platform.Storage;
using Mdhub.Models;
using Mdhub.Services;

using Mdhub.Localization;

namespace Mdhub.Views;

// Document workflows: file dialogs, duplicate tabs, saving, and unsaved-change decisions.
public partial class MainView
{
    private static FilePickerFileType TextFiles => new(Loc.T("TextAndMarkdown")) { Patterns = ["*.txt", "*.md", "*.markdown"] };
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
        document.Editor.PropertyChanged += (_, e) =>
        {
            if (_active == document && (e.Property == Avalonia.Controls.TextBox.SelectionStartProperty || e.Property == Avalonia.Controls.TextBox.SelectionEndProperty || e.Property == Avalonia.Controls.TextBox.CaretIndexProperty))
                UpdateCaretStatus();
        };
        _documents.Add(document);
        FileTabs.SelectedDocument = document;
        return document;
    }

    private async Task OpenAsync()
    {
        var files = await Host.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.T("OpenFilesTitle"), AllowMultiple = true, FileTypeFilter = [FilePickerFileTypes.All, TextFiles]
        });
        foreach (var file in files)
        {
            using (file) await OpenPathAsync(file.TryGetLocalPath() ?? throw new IOException(Loc.T("ChooseLocalFile")));
        }
    }

    // Session restore reopens tabs without reordering Open Recent.
    private bool _restoringTabs;
    private async Task OpenPathAsync(string path)
    {
        var recent = !_restoringTabs;
        path = Path.GetFullPath(path);
        var existing = _documents.FirstOrDefault(d => SamePath(d.Path, path));
        if (existing is not null) { FileTabs.SelectedDocument = existing; if (recent) RecentService.AddFile(path); return; }
        var kind = await Task.Run(() => FileSupport.Detect(path));
        if (kind == SupportedFileKind.Unsupported) { UpdateStatus(Loc.F("UnsupportedFile", path)); return; }
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
            if (recent) RecentService.AddFile(path);
            return;
        }
        (string Text, System.Text.Encoding Encoding) loaded;
        try { loaded = await TextFileStore.ReadAsync(path); }
        catch (System.Text.DecoderFallbackException) { UpdateStatus(Loc.F("UnsupportedEncoding", path)); return; }
        if (!FileSupport.IsTextByExtension(path) && !FileSupport.IsText(loaded.Text)) { UpdateStatus(Loc.F("UnsupportedBinary", path)); return; }
        AddDocument(path, loaded.Text, encoding: loaded.Encoding);
        if (recent) RecentService.AddFile(path);
    }

    private static bool SamePath(string? a, string b) => string.Equals(a, b,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Test seam: headless UI tests replace the system save picker.
    internal Func<NoteDocument, Task<string?>>? SaveFilePickerOverride { get; set; }

    private async Task<bool> SaveAsync(NoteDocument document, bool saveAs)
    {
        if (document.IsImage) { UpdateStatus(Loc.T("ImagesViewOnly")); return true; }
        var path = document.Path;
        if (saveAs || path is null)
        {
            if (SaveFilePickerOverride is not null) path = await SaveFilePickerOverride(document);
            else
            {
                using var file = await Host.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = Loc.T("SaveNoteTitle"), SuggestedFileName = path is null ? document.UntitledName : Path.GetFileName(path),
                    DefaultExtension = document.IsMarkdown ? "md" : "txt",
                    FileTypeChoices = [TextFiles, FilePickerFileTypes.All], ShowOverwritePrompt = true
                });
                if (file is null) return false;
                path = file.TryGetLocalPath() ?? throw new IOException(Loc.T("ChooseLocalFile"));
            }
            if (path is null) return false;
            if (_documents.Any(d => d != document && SamePath(d.Path, path)))
                throw new IOException(Loc.T("AlreadyOpenSaveAs"));
        }
        await TextFileStore.WriteAsync(path, document.Text, document.Encoding);
        var isNewPath = document.Path is null || !SamePath(document.Path, Path.GetFullPath(path));
        document.Path = path;
        document.SavedText = document.Text;
        document.Refresh();
        if (_active == document) MdEditor.ShowDocument(document.IsMarkdown ? document : null);
        UpdateContentLayout();
        UpdateStatus(Loc.F("Saved", path));
        MdEditor.ScheduleRefresh();
        if (isNewPath && FolderExplorer.Contains(path)) await FolderExplorer.RevealCreatedAsync(path);
        return true;
    }

    private async Task<bool> ConfirmCloseAsync(NoteDocument document)
    {
        if (!document.IsDirty) return true;
        FileTabs.SelectedDocument = document;
        var answer = await DialogService.ShowAsync(Host, Loc.T("UnsavedTitle"), Loc.F("SaveChangesTo", document.Title.TrimStart('●', ' ')), "Save", "Discard", "Cancel");
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
