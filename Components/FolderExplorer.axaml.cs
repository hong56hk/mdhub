using Avalonia.Controls;
using Avalonia.Input;
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
    public event Action? OpenFolderRequested;
    public event Action<ExplorerRequest>? ExplorerRequested;
    public string? OpenedFolder => _folder;
    private ContextMenu CreateContextMenu(string path)
    {
        MenuItem Item(string name, string header, ExplorerAction action)
        {
            var item = new MenuItem { Name = name, Header = header };
            item.Click += (_, _) => ExplorerRequested?.Invoke(new ExplorerRequest(action, path));
            return item;
        }
        Separator Line() => new() { Background = Avalonia.Media.Brushes.LightGray, Foreground = Avalonia.Media.Brushes.LightGray };
        return new ContextMenu
        {
            Items =
            {
                Item("RevealMenuItem", "Reveal in file explorer", ExplorerAction.Reveal), Line(),
                Item("CutMenuItem", "Cut", ExplorerAction.Cut), Item("CopyMenuItem", "Copy", ExplorerAction.Copy),
                Item("DuplicateMenuItem", "Duplicate", ExplorerAction.Duplicate), Line(),
                Item("RenameMenuItem", "Rename", ExplorerAction.Rename), Item("DeleteMenuItem", "Delete", ExplorerAction.Delete)
            }
        };
    }
    private void FolderName_Click(object? sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke();
    public Func<bool>? CanPaste { get; set; }
    private bool _createInRoot;
    public FolderExplorer()
    {
        InitializeComponent();
        InitializeDragDrop();
        FolderTree.AddHandler(PointerPressedEvent, FolderRow_PointerPressed, RoutingStrategies.Tunnel);
        FolderTree.AddHandler(DoubleTappedEvent, (_, e) =>
        {
            if (ItemAt(e.Source)?.Tag is FolderEntry { IsDirectory: true, IsLink: false }) e.Handled = true;
        }, RoutingStrategies.Tunnel);
        FolderTree.ContextMenu = CreateBackgroundMenu();
    }
    // Empty-space menu: tree items have their own menu, so this opens only for the background.
    private ContextMenu CreateBackgroundMenu()
    {
        MenuItem Item(string name, string header, Action action)
        {
            var item = new MenuItem { Name = name, Header = header };
            item.Click += (_, _) => action();
            return item;
        }
        Separator Line() => new() { Background = Avalonia.Media.Brushes.LightGray, Foreground = Avalonia.Media.Brushes.LightGray };
        void Create(bool isFolder) { FolderTree.SelectedItem = null; _createInRoot = true; NewItem(isFolder); }
        var newFile = Item("BackgroundNewFileMenuItem", "New file", () => Create(false));
        var newFolder = Item("BackgroundNewFolderMenuItem", "New folder", () => Create(true));
        var paste = Item("BackgroundPasteMenuItem", "Paste", () => { if (_folder is not null) ExplorerRequested?.Invoke(new ExplorerRequest(ExplorerAction.Paste, _folder)); });
        var reload = Item("BackgroundReloadMenuItem", "Reload", () => RefreshFolder_Click(null, new RoutedEventArgs()));
        var menu = new ContextMenu { Items = { newFile, newFolder, Line(), paste, Line(), reload } };
        menu.Opening += (_, _) => UpdateBackgroundMenu(menu);
        UpdateBackgroundMenu(menu);
        return menu;
    }
    public void UpdateBackgroundMenu() => UpdateBackgroundMenu(FolderTree.ContextMenu!);
    private void UpdateBackgroundMenu(ContextMenu menu)
    {
        var open = _folder is not null && !_busy;
        foreach (var item in menu.Items.OfType<MenuItem>())
            item.IsEnabled = open && (item.Name != "BackgroundPasteMenuItem" || CanPaste?.Invoke() == true);
    }
    public void SetBusy(bool busy)
    {
        _busy = busy;
        FolderTree.IsEnabled = !busy;
        FolderActions.IsEnabled = !busy && _folder is not null;
    }
    public string GetCreationDirectory(string? activeFilePath)
    {
        if (_createInRoot) { _createInRoot = false; return _folder ?? throw new InvalidOperationException("Open a folder first."); }
        return FolderService.CreationDirectory(
        (FolderTree.SelectedItem as TreeViewItem)?.Tag is FolderEntry entry ? entry.Path : null,
        activeFilePath, _folder ?? throw new InvalidOperationException("Open a folder first."));
    }
    private FileSystemWatcher? _watcher;
    private Avalonia.Threading.DispatcherTimer? _watchTimer;
    private int _refreshing;
    public event Action? ExternalChangeRefreshed;
    public bool WatchEnabled { get; set; } = true;

    // Reloads the tree but keeps folders that were expanded open.
    public async Task RefreshAsync()
    {
        if (_folder is null) return;
        var expanded = TreeNodes(FolderTree.Items.OfType<TreeViewItem>())
            .Where(i => i.IsExpanded && i.Tag is FolderEntry).Select(i => ((FolderEntry)i.Tag!).Path)
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var selected = (FolderTree.SelectedItem as TreeViewItem)?.Tag is FolderEntry s ? s.Path : null;
        _refreshing++;
        try
        {
            await LoadRootAsync(_folder, restartWatcher: false);
            await ExpandAsync(FolderTree.Items.OfType<TreeViewItem>(), expanded);
            if (selected is not null)
                FolderTree.SelectedItem = TreeNodes(FolderTree.Items.OfType<TreeViewItem>()).FirstOrDefault(i => i.Tag is FolderEntry e && expanded.Comparer.Equals(e.Path, selected));
        }
        finally { _refreshing--; }
    }
    private async Task ExpandAsync(IEnumerable<TreeViewItem> items, HashSet<string> expanded)
    {
        foreach (var item in items.ToList())
        {
            if (item.Tag is not FolderEntry entry || !expanded.Contains(entry.Path)) continue;
            if (_loadChildren.TryGetValue(item, out var load)) await load();
            item.IsExpanded = true;
            await ExpandAsync(item.Items.OfType<TreeViewItem>(), expanded);
        }
    }
    private static IEnumerable<TreeViewItem> TreeNodes(IEnumerable<TreeViewItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in TreeNodes(item.Items.OfType<TreeViewItem>())) yield return child;
        }
    }
    public bool Contains(string path) => _folder is not null && ExplorerFileOperations.Contains(_folder, path);

    private void StartWatcher(string path)
    {
        StopWatcher();
        if (!WatchEnabled) return;
        _watchTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _watchTimer.Tick += async (_, _) =>
        {
            _watchTimer?.Stop();
            if (_busy || _dragging || _refreshing > 0 || _folder is null) { _watchTimer?.Start(); return; }
            try { await RefreshAsync(); ExternalChangeRefreshed?.Invoke(); }
            catch (Exception ex) { MessageChanged?.Invoke(ex.Message); }
        };
        var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName };
        void Changed(string changed)
        {
            var relative = Path.GetRelativePath(path, changed);
            var first = relative.Split(Path.DirectorySeparatorChar)[0];
            if (first is ".git" or ".md-trash" || FolderService.IsInternal(changed) || Path.GetFileName(changed).StartsWith(".mdhub-")) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (_watcher == watcher) { _watchTimer?.Stop(); _watchTimer?.Start(); } });
        }
        watcher.Created += (_, e) => Changed(e.FullPath);
        watcher.Deleted += (_, e) => Changed(e.FullPath);
        watcher.Renamed += (_, e) => { Changed(e.OldFullPath); Changed(e.FullPath); };
        watcher.EnableRaisingEvents = true;
        _watcher = watcher;
    }
    public void StopWatcher()
    {
        _watchTimer?.Stop();
        _watchTimer = null;
        _watcher?.Dispose();
        _watcher = null;
    }
    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        StopWatcher();
        base.OnDetachedFromVisualTree(e);
    }

    public Task LoadRootAsync(string path) => LoadRootAsync(path, restartWatcher: true);
    private async Task LoadRootAsync(string path, bool restartWatcher)
    {
        var entries = await FolderService.ReadAsync(path);
        if (restartWatcher || _watcher is null) StartWatcher(path);
        _folder = path;
        _loadChildren.Clear();
        FolderActions.IsEnabled = !_busy;
        FolderName.Text = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        ToolTip.SetTip(FolderNameButton, $"{path}`nClick to open a different folder (Ctrl+Shift+O)");
        FolderTree.Items.Clear();
        foreach (var entry in entries) FolderTree.Items.Add(CreateTreeItem(entry));
        MessageChanged?.Invoke($"Opened folder · {path}");
    }
    private TreeViewItem CreateTreeItem(FolderEntry entry)
    {
        var item = new TreeViewItem { Header = (entry.IsDirectory ? "" : "▤  ") + Path.GetFileName(entry.Path), Tag = entry };
        ToolTip.SetTip(item, entry.Path);
        item.ContextMenu = CreateContextMenu(entry.Path);
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
    private void FolderRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Console.WriteLine($"FOLDER PRESS {e.Source} busy={_busy} item={ItemAt(e.Source)?.Header} expanded={ItemAt(e.Source)?.IsExpanded}");
        if (_busy || !e.GetCurrentPoint(FolderTree).Properties.IsLeftButtonPressed) return;
        if (ItemAt(e.Source) is not { Tag: FolderEntry { IsDirectory: true, IsLink: false } } item) return;
        FolderTree.SelectedItem = item;
        item.Focus();
        item.IsExpanded = !item.IsExpanded;
        // Handle the arrow and row together, preventing the default double-click toggle.
        e.Handled = true;
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
    private void NewFile_Click(object? sender, RoutedEventArgs e) => NewItem(false);
    private void NewFolder_Click(object? sender, RoutedEventArgs e) => NewItem(true);
    private void NewItem(bool isFolder) => CreateRequested?.Invoke(isFolder);
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
