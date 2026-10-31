using Avalonia.Controls;
using Avalonia.Input.Platform;
using Mdhub.Localization;
using Mdhub.Services;

namespace Mdhub.Views;

// Item 80: explorer actions on a multi-selection (paths already reduced to top-level items).
public partial class MainView
{
    // Every path of the last Cut/Copy; ExplorerClipboard keeps the first one for the single-item code paths.
    internal IReadOnlyList<string>? ExplorerClipboardPaths { get; private set; }

    private async Task HandleMultiExplorerRequestAsync(ExplorerRequest request, string root)
    {
        var paths = request.Paths!.Select(Path.GetFullPath).ToList();
        switch (request.Action)
        {
            case ExplorerAction.Reveal:
                // One window per folder, not one per item.
                foreach (var path in paths.GroupBy(p => Path.GetDirectoryName(p) ?? p, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
                    (RevealOverride ?? ExplorerFileOperations.Reveal)(path);
                break;
            case ExplorerAction.CopyPath:
                if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                    await clipboard.SetTextAsync(string.Join(Environment.NewLine, paths));
                UpdateStatus(Loc.Plural("PathsCopiedOne", "PathsCopiedMany", paths.Count));
                break;
            case ExplorerAction.Cut:
            case ExplorerAction.Copy:
            {
                var cut = request.Action == ExplorerAction.Cut;
                ExplorerClipboard = (paths[0], cut);
                ExplorerClipboardPaths = paths;
                await PublishClipboardFilesAsync(paths);
                UpdateStatus(Loc.Plural(cut ? "CutItemsOne" : "CopiedItemsOne", cut ? "CutItemsMany" : "CopiedItemsMany", paths.Count));
                break;
            }
            case ExplorerAction.Delete:
            {
                var answer = await DialogService.ShowAsync(Host, Loc.T("Delete"), Loc.Plural("DeleteItemsConfirmOne", "DeleteItemsConfirmMany", paths.Count), "MoveItemsToRecycleBin", "Cancel");
                if (answer != "MoveItemsToRecycleBin") return;
                var affected = _documents.Where(d => d.Path is not null && paths.Any(p => IsSameOrInside(p, d.Path))).ToList();
                foreach (var document in affected)
                    if (!await ConfirmCloseAsync(document)) return;
                var recycle = RecycleOverride;
                await Task.Run(() => { foreach (var path in paths) { if (recycle is null) ExplorerFileOperations.Recycle(root, path); else recycle(path); } });
                foreach (var document in affected) RemoveDocument(document);
                FolderExplorer.ClearMultiSelection();
                await FolderExplorer.RevealCreatedAsync(Path.GetDirectoryName(paths[0])!);
                UpdateStatus(Loc.Plural("MovedItemsToBinOne", "MovedItemsToBinMany", paths.Count));
                break;
            }
        }
    }

    // Paste of a multi-item clipboard into one folder; a name that exists gets the usual -copy suffix.
    private async Task PasteManyAsync(string directory, IReadOnlyList<string> sources, bool cut)
    {
        string? last = null;
        foreach (var source in sources)
        {
            if (!File.Exists(source) && !Directory.Exists(source)) continue;
            var target = Path.Combine(directory, Path.GetFileName(source));
            if (cut && string.Equals(Path.GetFullPath(target), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(target) || Directory.Exists(target)) target = ExplorerFileOperations.UniqueCopyPath(target);
            if (cut)
            {
                await Task.Run(() => ExplorerFileOperations.Move(source, target));
                UpdateMovedDocuments(source, target);
            }
            else await Task.Run(() => ExplorerFileOperations.Copy(source, target));
            last = target;
        }
        if (cut) { ExplorerClipboard = null; ExplorerClipboardPaths = null; }
        if (last is not null) await FolderExplorer.RevealCreatedAsync(last);
        UpdateStatus(Loc.Plural(cut ? "MovedItemsOne" : "PastedItemsOne", cut ? "MovedItemsMany" : "PastedItemsMany", sources.Count));
    }

    private async Task MoveManyAsync(string root, IReadOnlyList<string> sources, string directory)
    {
        string? last = null;
        foreach (var source in sources)
        {
            var target = ExplorerMove.Destination(root, source, directory);
            if (target is null) continue;
            if (_documents.Any(document => document.Path is not null && SamePath(document.Path, target))) throw new IOException(Loc.T("DestinationOpen"));
            await Task.Run(() => ExplorerFileOperations.Move(source, target));
            UpdateMovedDocuments(source, target);
            if (ExplorerClipboardPaths is { } clip && clip.Any(p => SamePath(p, source)))
                ExplorerClipboardPaths = clip.Select(p => SamePath(p, source) ? target : p).ToList();
            last = target;
        }
        if (last is null) throw new IOException(Loc.T("CannotMoveHere"));
        FolderExplorer.ClearMultiSelection();
        await FolderExplorer.RevealCreatedAsync(last);
        UpdateStatus(Loc.Plural("MovedItemsOne", "MovedItemsMany", sources.Count));
    }

    private async Task OpenManyAsync(IReadOnlyList<string> paths)
    {
        foreach (var path in paths) await OpenPathAsync(path);
    }
}
