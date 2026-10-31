using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

// Item 52 (VS Code): binary files get a placeholder tab; open them with the default app or anyway as text.
public partial class MainView
{
    // Test seam: (title key, message, confirm key) -> confirmed. Replaces the modal dialog.
    internal Func<string, string, string, Task<bool>>? ConfirmOverride { get; set; }

    private async Task<bool> ConfirmAsync(string titleKey, string message, string confirmKey)
    {
        if (ConfirmOverride is not null) return await ConfirmOverride(titleKey, message, confirmKey);
        return await DialogService.ShowAsync(Host, Loc.T(titleKey), message, confirmKey, "Cancel") == confirmKey;
    }

    private async Task<bool> ConfirmLargeAsync(string path)
    {
        long length;
        try { length = new FileInfo(path).Length; } catch (IOException) { return true; }
        return length <= FileSupport.LargeTextBytes || await ConfirmAsync("LargeFileTitle", Loc.T("LargeFileConfirm"), "OpenAnyway");
    }

    private void AddBinaryDocument(string path, bool recent)
    {
        var document = new NoteDocument { Path = path, IsBinary = true };
        _documents.Add(document);
        FileTabs.SelectedDocument = document;
        if (recent) RecentService.AddFile(path);
    }

    private void WireBinaryPlaceholder()
    {
        BinaryPlaceholder.OpenDefaultRequested += async () => { if (_active is { IsBinary: true, Path: { } path }) await OpenWithDefaultAppAsync(path); };
        BinaryPlaceholder.OpenAsTextRequested += async () => { if (_active is { IsBinary: true } document && !_busy) await RunAsync(() => OpenAsTextAsync(document)); };
        FolderExplorer.DefaultAppRequested += async path => await OpenWithDefaultAppAsync(path);
    }

    internal async Task OpenWithDefaultAppAsync(string path)
    {
        if (_busy) return;
        if (DefaultApp.IsExecutable(path) && !await ConfirmAsync("RunProgramTitle", Loc.F("RunProgramConfirm", Path.GetFileName(path)), "RunProgram")) return;
        try { DefaultApp.Launch(path); UpdateStatus(Loc.F("OpenedWithDefaultApp", Path.GetFileName(path))); }
        catch (Exception ex) { UpdateStatus(Loc.F("UnableToOpenDefaultApp", ex.Message)); }
    }

    // Replaces the placeholder tab in place with a text tab; lossy decodes stay read-only.
    private async Task OpenAsTextAsync(NoteDocument placeholder)
    {
        if (placeholder.Path is not { } path || !await ConfirmLargeAsync(path)) return;
        var (text, encoding, lossy) = await TextFileStore.ReadLenientAsync(path);
        var index = _documents.IndexOf(placeholder);
        var document = new NoteDocument { Path = path, SavedText = text, Encoding = encoding, IsReadOnlyText = lossy };
        document.SetLoadedLineEnding(Mdhub.Text.LineEndings.Detect(text, LineEndingSettings.ForNewFile)); // item 68
        InitializeTextDocument(document, text);
        _documents.Insert(index, document);
        _documents.Remove(placeholder);
        FileTabs.SelectedDocument = document;
        if (document.IsReadOnlyText) UpdateStatus(Loc.T("OpenedReadOnly"));
    }
}