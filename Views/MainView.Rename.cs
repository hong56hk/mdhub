using Mdhub.Services;

using Mdhub.Localization;

namespace Mdhub.Views;

public partial class MainView
{
    private async Task<string?> RenameExplorerItemAsync(string path, string name)
    {
        if (_busy) return Loc.T("WaitForOperation");
        if (FolderService.ValidateName(name) is { } error) return error;
        if (FolderExplorer.OpenedFolder is not { } root || !ExplorerFileOperations.Contains(root, path)) return Loc.T("OutsideFolder");
        var parent = Path.GetDirectoryName(path)!;
        var target = Path.Combine(parent, name);
        if (Components.FolderExplorer.DuplicateError(path, name) is { } duplicate) return duplicate;
        if (_documents.Any(d => d.Path is { } p && SamePath(p, target) && !SamePath(p, path))) return Loc.T("NameOpenInTab");
        SetBusy(true);
        try
        {
            await Task.Run(() =>
            {
                if (OperatingSystem.IsWindows() && SamePath(path, target))
                {
                    var temporary = Path.Combine(parent, $".mdhub-rename-{Guid.NewGuid():N}.tmp");
                    ExplorerFileOperations.Move(path, temporary);
                    try { ExplorerFileOperations.Move(temporary, target); }
                    catch { ExplorerFileOperations.Move(temporary, path); throw; }
                }
                else ExplorerFileOperations.Move(path, target);
            });
            UpdateMovedDocuments(path, target);
            if (ExplorerClipboard is { } clipboard && IsSameOrInside(path, clipboard.Path))
            {
                var relative = Path.GetRelativePath(path, clipboard.Path);
                ExplorerClipboard = (relative == "." ? target : Path.Combine(target, relative), clipboard.IsCut);
            }
            await FolderExplorer.RevealCreatedAsync(target);
            UpdateStatus(Loc.F("Renamed", target));
            return null;
        }
        catch (Exception ex) { return ex.Message; }
        finally { SetBusy(false); }
    }
}
