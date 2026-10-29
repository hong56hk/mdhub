using Mdhub.Services;

namespace Mdhub.Views;

public partial class MainView
{
    private async Task MoveExplorerFileAsync(ExplorerMoveRequest request)
    {
        var root = FolderExplorer.OpenedFolder ?? throw new InvalidOperationException("Open a folder first.");
        var target = ExplorerMove.Destination(root, request.Source, request.Directory)
            ?? throw new IOException("Cannot move this file here. The destination may already contain a file with that name.");
        if (_documents.Any(document => document.Path is not null && SamePath(document.Path, target)))
            throw new IOException("The destination is already open in a tab. Close that tab before moving this file.");
        await Task.Run(() => ExplorerFileOperations.Move(request.Source, target));
        UpdateMovedDocuments(request.Source, target);
        if (ExplorerClipboard is { } clipboard && SamePath(clipboard.Path, request.Source))
            ExplorerClipboard = (target, clipboard.IsCut);
        await FolderExplorer.RevealCreatedAsync(target);
        UpdateStatus($"Moved · {target}");
    }
}
