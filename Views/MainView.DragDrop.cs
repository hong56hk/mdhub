using Mdhub.Services;

using Mdhub.Localization;

namespace Mdhub.Views;

public partial class MainView
{
    private async Task ImportExplorerFilesAsync(ExplorerImportRequest request)
    {
        var root = FolderExplorer.OpenedFolder ?? throw new InvalidOperationException(Loc.T("OpenFolderFirst"));
        var openPaths = AllDocuments.Select(d => d.Path).OfType<string>().ToHashSet(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string? last = null;
        var copied = 0;
        try
        {
            foreach (var source in request.Sources)
            {
                last = await Task.Run(() =>
                {
                    var target = ExplorerImport.Destination(root, source, request.Directory)
                        ?? throw new IOException(Loc.T("CannotImportHere"));
                    bool Occupied(string path) => File.Exists(path) || Directory.Exists(path) || openPaths.Contains(path);
                    if (Occupied(target)) target = ExplorerFileOperations.UniqueCopyPath(target, false, Occupied);
                    ExplorerFileOperations.Copy(source, target);
                    return target;
                });
                copied++;
            }
        }
        finally
        {
            // Keep successful copies visible even if a later source becomes unavailable.
            if (last is not null) await FolderExplorer.RevealCreatedAsync(last);
        }
        UpdateStatus(Loc.Plural("PastedItemsOne", "PastedItemsMany", copied));
    }

    private async Task MoveExplorerFileAsync(ExplorerMoveRequest request)
    {
        var root = FolderExplorer.OpenedFolder ?? throw new InvalidOperationException(Loc.T("OpenFolderFirst"));
        if (request.Sources is { Count: > 1 } sources) { await MoveManyAsync(root, sources, request.Directory); return; }
        var target = ExplorerMove.Destination(root, request.Source, request.Directory)
            ?? throw new IOException(Loc.T("CannotMoveHere"));
        if (AllDocuments.Any(document => document.Path is not null && SamePath(document.Path, target)))
            throw new IOException(Loc.T("DestinationOpen"));
        await Task.Run(() => ExplorerFileOperations.Move(request.Source, target));
        UpdateMovedDocuments(request.Source, target);
        if (ExplorerClipboard is { } clipboard && SamePath(clipboard.Path, request.Source))
            ExplorerClipboard = (target, clipboard.IsCut);
        await FolderExplorer.RevealCreatedAsync(target);
        UpdateStatus(Loc.F("Moved", target));
    }
}
