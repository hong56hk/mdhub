using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mdhub.Services;
using Mdhub.Localization;

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
            var item = LocResources.Item(header);
            item.Name = name;
            item.Click += (_, _) =>
            {
                if (action == ExplorerAction.Rename) BeginRename(path);
                else ExplorerRequested?.Invoke(new ExplorerRequest(action, path));
            };
            return item;
        }
        Separator Line() => new() { Background = Mdhub.Services.ThemeService.Brush("SeparatorForeground"), Foreground = Mdhub.Services.ThemeService.Brush("SeparatorForeground") };
        return new ContextMenu
        {
            // Headers are resource keys; Localized() refreshes them on open after a language switch.
            Items =
            {
                Item("RevealMenuItem", "Reveal", ExplorerAction.Reveal), Line(),
                Item("CutMenuItem", "MenuCut", ExplorerAction.Cut), Item("CopyMenuItem", "MenuCopy", ExplorerAction.Copy),
                Item("DuplicateMenuItem", "Duplicate", ExplorerAction.Duplicate), Line(),
                Item("RenameMenuItem", "Rename", ExplorerAction.Rename), Item("DeleteMenuItem", "Delete", ExplorerAction.Delete)
            }
        }.Localized();
    }
    private void FolderName_Click(object? sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke();
    public Func<bool>? CanPaste { get; set; }
    private bool _createInRoot;
    public FolderExplorer()
    {
        InitializeComponent();
        InitializeDragDrop();
        FolderTree.AddHandler(PointerPressedEvent, FolderRow_PointerPressed, RoutingStrategies.Tunnel);
        FolderTree.ContextMenu = CreateBackgroundMenu();
        UpdateLanguage();
        AttachedToVisualTree += (_, _) => { Loc.Changed += UpdateLanguage; UpdateLanguage(); };
        DetachedFromVisualTree += (_, _) => Loc.Changed -= UpdateLanguage;
    }
    // Code-set texts follow a language switch: the header, and the tooltips and placeholders of tree items.
    private void UpdateLanguage()
    {
        if (_folder is null) { FolderName.Text = Loc.T("OpenFolderButton"); ToolTip.SetTip(FolderNameButton, Loc.T("OpenFolderTip")); }
        else ToolTip.SetTip(FolderNameButton, Loc.F("FolderTip", _folder));
        void Walk(ItemsControl parent)
        {
            foreach (var item in parent.Items.OfType<TreeViewItem>())
            {
                if (item.Tag is FolderEntry entry) { ToolTip.SetTip(item, TipFor(entry)); Walk(item); }
                else if (item.Tag is string key) item.Header = Loc.T(key);
            }
        }
        Walk(FolderTree);
    }
    private static string TipFor(FolderEntry entry) =>
        !entry.IsDirectory && !entry.IsSupported ? entry.Path + "\n" + Loc.T("UnsupportedTip")
        : entry.IsDirectory && entry.IsLink ? entry.Path + Loc.T("LinkedFolder")
        : entry.Path;
    // Empty-space menu: tree items have their own menu, so this opens only for the background.
    private ContextMenu CreateBackgroundMenu()
    {
        MenuItem Item(string name, string header, Action action)
        {
            var item = LocResources.Item(header);
            item.Name = name;
            item.Click += (_, _) => action();
            return item;
        }
        Separator Line() => new() { Background = Mdhub.Services.ThemeService.Brush("SeparatorForeground"), Foreground = Mdhub.Services.ThemeService.Brush("SeparatorForeground") };
        void Create(bool isFolder) { FolderTree.SelectedItem = null; _createInRoot = true; NewItem(isFolder); }
        var newFile = Item("BackgroundNewFileMenuItem", "NewFile", () => Create(false));
        var newFolder = Item("BackgroundNewFolderMenuItem", "NewFolder", () => Create(true));
        var paste = Item("BackgroundPasteMenuItem", "MenuPaste", () => { if (_folder is not null) ExplorerRequested?.Invoke(new ExplorerRequest(ExplorerAction.Paste, _folder)); });
        var reload = Item("BackgroundReloadMenuItem", "Reload", () => RefreshFolder_Click(null, new RoutedEventArgs()));
        var menu = new ContextMenu { Items = { newFile, newFolder, Line(), paste, Line(), reload } }.Localized();
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
        if (_createInRoot) { _createInRoot = false; return _folder ?? throw new InvalidOperationException(Loc.T("OpenFolderFirst")); }
        return FolderService.CreationDirectory(
        (FolderTree.SelectedItem as TreeViewItem)?.Tag is FolderEntry entry ? entry.Path : null,
        activeFilePath, _folder ?? throw new InvalidOperationException(Loc.T("OpenFolderFirst")));
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
            if (_busy || _dragging || IsRenaming || _refreshing > 0 || _folder is null) { _watchTimer?.Start(); return; }
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
        if (!_renameSaving) CancelRename();
        var entries = await FolderService.ReadAsync(path);
        if (restartWatcher || _watcher is null) StartWatcher(path);
        _folder = path;
        _loadChildren.Clear();
        FolderActions.IsEnabled = !_busy;
        FolderName.Text = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        ToolTip.SetTip(FolderNameButton, Loc.F("FolderTip", path));
        FolderTree.Items.Clear();
        foreach (var entry in entries) FolderTree.Items.Add(CreateTreeItem(entry));
        MessageChanged?.Invoke(Loc.F("OpenedFolder", path));
    }
    private TreeViewItem CreateTreeItem(FolderEntry entry)
    {
        var item = new ExplorerTreeViewItem { Header = (entry.IsDirectory ? "" : "▤  ") + Path.GetFileName(entry.Path), Tag = entry };
        ToolTip.SetTip(item, TipFor(entry));
        if (!entry.IsDirectory && !entry.IsSupported)
        {
            item.Opacity = 0.4;
        }
        item.ContextMenu = CreateContextMenu(entry.Path);
        if (entry.IsDirectory && !entry.IsLink)
        {
            item.Items.Add(new TreeViewItem { Header = Loc.T("Loading"), Tag = "Loading", IsEnabled = false });
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
                    item.Items.Add(new TreeViewItem { Header = Loc.T("UnableToReadFolder"), Tag = "UnableToReadFolder", IsEnabled = false });
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
        return item;
    }
    private void FolderRow_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsRenaming && ItemAt(e.Source) == _renameItem) return;
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
        if (IsRenaming || FolderTree.SelectedItem is not TreeViewItem { Tag: FolderEntry entry } || entry.IsDirectory || !entry.IsSupported) return;
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

    // Keyboard focus for Ctrl+Shift+E: the selected item, else the tree itself.
    public void FocusTree()
    {
        if (FolderTree.SelectedItem is TreeViewItem { IsEffectivelyVisible: true } item && item.Focus()) return;
        if (FolderTree.Items.OfType<TreeViewItem>().FirstOrDefault() is { } first && first.Focus()) return;
        // An empty tree has no item to focus; let the tree itself take focus so keys leave the editor.
        FolderTree.Focusable = true;
        FolderTree.Focus();
    }
}
