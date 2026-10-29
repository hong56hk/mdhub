using Avalonia.Controls;
using Avalonia.Interactivity;
using Mdhub.Services;

namespace Mdhub.Components;

public partial class FolderExplorer : UserControl
{
    private string? _folder;
    private bool _busy;
    private readonly Dictionary<TreeViewItem, Func<Task>> _loadChildren = [];
    public event Action<string>? FileRequested;
    public event Action<string>? MessageChanged;
    public event Action? RefreshRequested;
    public event Action<bool>? CreateRequested;
    public FolderExplorer() => InitializeComponent();
    public void SetBusy(bool busy)
    {
        _busy = busy;
        FolderTree.IsEnabled = !busy;
        FolderActions.IsEnabled = !busy && _folder is not null;
    }
    public string GetCreationDirectory(string? activeFilePath) => FolderService.CreationDirectory(
        (FolderTree.SelectedItem as TreeViewItem)?.Tag is FolderEntry entry ? entry.Path : null,
        activeFilePath, _folder ?? throw new InvalidOperationException("Open a folder first."));
    public Task RefreshAsync() => _folder is null ? Task.CompletedTask : LoadRootAsync(_folder);
    public async Task LoadRootAsync(string path)
    {
        var entries = await FolderService.ReadAsync(path);
        _folder = path;
        _loadChildren.Clear();
        FolderActions.IsEnabled = !_busy;
        FolderName.Text = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        ToolTip.SetTip(FolderName, path);
        FolderTree.Items.Clear();
        foreach (var entry in entries) FolderTree.Items.Add(CreateTreeItem(entry));
        MessageChanged?.Invoke($"Opened folder · {path}");
    }
    private TreeViewItem CreateTreeItem(FolderEntry entry)
    {
        var item = new TreeViewItem { Header = (entry.IsDirectory ? "▸  " : "▤  ") + Path.GetFileName(entry.Path), Tag = entry };
        ToolTip.SetTip(item, entry.Path);
        if (entry.IsDirectory && !entry.IsLink)
        {
            item.Items.Add(new TreeViewItem { Header = "Loading…", IsEnabled = false });
            Task? loading = null;
            async Task LoadAsync()
            {
                try
                {
                    var children = await FolderService.ReadAsync(entry.Path);
                    item.Items.Clear();
                    foreach (var child in children) item.Items.Add(CreateTreeItem(child));
                }
                catch (Exception ex)
                {
                    item.Items.Clear();
                    item.Items.Add(new TreeViewItem { Header = "Unable to read folder", IsEnabled = false });
                    MessageChanged?.Invoke(ex.Message);
                    loading = null;
                }
            }
            Task EnsureLoaded() => loading ??= LoadAsync();
            _loadChildren[item] = EnsureLoaded;
            item.PropertyChanged += async (_, e) =>
            {
                if (e.Property == TreeViewItem.IsExpandedProperty && item.IsExpanded) await EnsureLoaded();
            };
        }
        else if (entry.IsLink) ToolTip.SetTip(item, entry.Path + " (linked folder; not followed)");
        return item;
    }
    private void FolderTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FolderTree.SelectedItem is not TreeViewItem { Tag: FolderEntry entry } || entry.IsDirectory) return;
        if (Path.GetExtension(entry.Path).ToLowerInvariant() is not (".md" or ".markdown" or ".txt"))
        {
            MessageChanged?.Invoke("All files are shown. Select a .txt, .md, or .markdown file to edit.");
            return;
        }
        FileRequested?.Invoke(entry.Path);
    }
    private void RefreshFolder_Click(object? sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    private void NewFile_Click(object? sender, RoutedEventArgs e) => CreateRequested?.Invoke(false);
    private void NewFolder_Click(object? sender, RoutedEventArgs e) => CreateRequested?.Invoke(true);
    private void CollapseAll_Click(object? sender, RoutedEventArgs e) => CollapseAll(FolderTree.Items.OfType<TreeViewItem>());

    private static void CollapseAll(IEnumerable<TreeViewItem> items)
    {
        foreach (var item in items)
        {
            CollapseAll(item.Items.OfType<TreeViewItem>());
            item.IsExpanded = false;
        }
    }

    public async Task RevealCreatedAsync(string path)
    {
        await RefreshAsync();
        if (_folder is null) return;
        var relative = Path.GetRelativePath(_folder, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) return;
        var parts = relative.Split(Path.DirectorySeparatorChar);
        var items = FolderTree.Items.OfType<TreeViewItem>();
        for (var index = 0; index < parts.Length; index++)
        {
            var item = items.FirstOrDefault(i => i.Tag is FolderEntry entry && string.Equals(Path.GetFileName(entry.Path), parts[index],
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            if (item is null) return;
            if (index == parts.Length - 1) { FolderTree.SelectedItem = item; item.BringIntoView(); return; }
            if (_loadChildren.TryGetValue(item, out var load)) await load();
            item.IsExpanded = true;
            items = item.Items.OfType<TreeViewItem>();
        }
    }
}
