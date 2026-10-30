using Mdhub.Models;
using Mdhub.Services;

using Mdhub.Localization;

namespace Mdhub.Views;

public partial class MainView
{
    private async Task CreateExplorerItemAsync(bool isFolder)
    {
        var directory = FolderExplorer.GetCreationDirectory();
        var name = await DialogService.PromptNameAsync(Host, Loc.T(isFolder ? "NewFolder" : "NewFile"), directory,
            isFolder ? "New folder" : "Untitled.md", FolderExplorer.DisplayDirectory(directory));
        if (name is null) return;
        var path = await FolderService.CreateAsync(directory, name, isFolder);
        await FolderExplorer.RevealCreatedAsync(path);
        if (!isFolder) await OpenPathAsync(path);
        UpdateStatus(Loc.F(isFolder ? "CreatedFolder" : "CreatedFile", path));
    }
    // Test seam: headless UI tests replace the shell "reveal in file explorer" call.
    internal Action<string>? RevealOverride { get; set; }
    // Test seam: headless UI tests replace sending items to the Recycle Bin.
    internal Action<string>? RecycleOverride { get; set; }
    // Cut/copy state kept for a later Paste command.
    internal (string Path, bool IsCut)? ExplorerClipboard { get; private set; }

    private async Task HandleExplorerRequestAsync(ExplorerRequest request)
    {
        var path = Path.GetFullPath(request.Path);
        var root = FolderExplorer.OpenedFolder ?? throw new InvalidOperationException(Loc.T("OpenFolderFirst"));
        var parent = Path.GetDirectoryName(path)!;
        switch (request.Action)
        {
            case ExplorerAction.Reveal:
                (RevealOverride ?? ExplorerFileOperations.Reveal)(path);
                await FolderExplorer.RevealCreatedAsync(path);
                break;
            case ExplorerAction.Cut:
            case ExplorerAction.Copy:
                ExplorerClipboard = (path, request.Action == ExplorerAction.Cut);
                await FolderExplorer.RevealCreatedAsync(path);
                UpdateStatus(Loc.F(request.Action == ExplorerAction.Cut ? "CutStatus" : "CopiedStatus", path));
                break;
            case ExplorerAction.Paste:
            {
                if (ExplorerClipboard is not { } clip) return;
                var target = Path.Combine(path, Path.GetFileName(clip.Path));
                if (File.Exists(target) || Directory.Exists(target)) target = ExplorerFileOperations.UniqueCopyPath(target);
                if (clip.IsCut)
                {
                    await Task.Run(() => ExplorerFileOperations.Move(clip.Path, target));
                    ExplorerClipboard = null;
                    UpdateMovedDocuments(clip.Path, target);
                }
                else await Task.Run(() => ExplorerFileOperations.Copy(clip.Path, target));
                await FolderExplorer.RevealCreatedAsync(target);
                UpdateStatus(Loc.F(clip.IsCut ? "Moved" : "PastedStatus", target));
                break;
            }
            case ExplorerAction.Duplicate:
            {
                var target = ExplorerFileOperations.UniqueCopyPath(path);
                await Task.Run(() => ExplorerFileOperations.Copy(path, target));
                await FolderExplorer.RevealCreatedAsync(target);
                UpdateStatus(Loc.F("Duplicated", target));
                break;
            }
            case ExplorerAction.Rename:
                FolderExplorer.BeginRename(path);
                break;
            case ExplorerAction.Delete:
            {
                // Item 53: the button says what happens (the item goes to the Recycle Bin, it is not erased).
                var answer = await DialogService.ShowAsync(Host, Loc.T("Delete"), Loc.F("DeleteConfirm", Path.GetFileName(path)), "MoveToRecycleBin", "Cancel");
                if (answer != "MoveToRecycleBin") return;
                var affected = _documents.Where(d => d.Path is not null && IsSameOrInside(path, d.Path)).ToList();
                foreach (var document in affected)
                    if (!await ConfirmCloseAsync(document)) return;
                var recycle = RecycleOverride;
                await Task.Run(() => { if (recycle is null) ExplorerFileOperations.Recycle(root, path); else recycle(path); });
                foreach (var document in affected) RemoveDocument(document);
                await FolderExplorer.RevealCreatedAsync(parent);
                UpdateStatus(Loc.F("MovedToBin", path));
                break;
            }
        }
    }

    private static bool IsSameOrInside(string root, string path) =>
        ExplorerFileOperations.Contains(root, path);

    private void UpdateMovedDocuments(string oldPath, string newPath)
    {
        foreach (var document in _documents.Where(d => d.Path is not null && IsSameOrInside(oldPath, d.Path)))
        {
            var relative = Path.GetRelativePath(oldPath, document.Path!);
            document.Path = relative == "." ? newPath : Path.Combine(newPath, relative);
            document.Refresh();
        }
        UpdateStatus();
        if (_active is { } active) MdEditor.ShowDocument(active.IsMarkdown ? active : null);
        UpdateContentLayout();
    }

    private void RemoveDocument(NoteDocument document)
    {
        var index = _documents.IndexOf(document);
        _documents.Remove(document);
        document.Image?.Dispose();
        if (_documents.Count == 0) AddDocument(null, "");
        else if (_active == document) FileTabs.SelectedDocument = _documents[Math.Min(index, _documents.Count - 1)];
    }
}
