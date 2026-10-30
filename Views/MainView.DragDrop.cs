using Mdhub.Services;

using Mdhub.Localization;

namespace Mdhub.Views;

public partial class MainView
{
    private async Task MoveExplorerFileAsync(ExplorerMoveRequest request)
    {
        var root = FolderExplorer.OpenedFolder ?? throw new InvalidOperationException(Loc.T("OpenFolderFirst"));
        var target = ExplorerMove.Destination(root, request.Source, request.Directory)
            ?? throw new IOException(Loc.T("CannotMoveHere"));
        if (_documents.Any(document => document.Path is not null && SamePath(document.Path, target)))
            throw new IOException(Loc.T("DestinationOpen"));
        await Task.Run(() => ExplorerFileOperations.Move(request.Source, target));
        UpdateMovedDocuments(request.Source, target);
        if (ExplorerClipboard is { } clipboard && SamePath(clipboard.Path, request.Source))
            ExplorerClipboard = (target, clipboard.IsCut);
        await FolderExplorer.RevealCreatedAsync(target);
        UpdateStatus(Loc.F("Moved", target));
    }
}
