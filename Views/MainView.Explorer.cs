using Mdhub.Services;

namespace Mdhub.Views;

public partial class MainView
{
    private async Task CreateExplorerItemAsync(bool isFolder)
    {
        var directory = FolderExplorer.GetCreationDirectory(_active?.Path);
        var name = await DialogService.PromptNameAsync(Host, isFolder ? "New folder" : "New file", directory,
            isFolder ? "New folder" : "Untitled.md");
        if (name is null) return;
        var path = await FolderService.CreateAsync(directory, name, isFolder);
        await FolderExplorer.RevealCreatedAsync(path);
        if (!isFolder) await OpenPathAsync(path);
        UpdateStatus($"Created {(isFolder ? "folder" : "file")} · {path}");
    }
}
