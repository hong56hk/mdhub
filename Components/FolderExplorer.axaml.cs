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
    // Enter or double-click on a file: open it and move focus to the editor.
    public event Action<string>? FileActivated;
    // Item 52: launch a file with the system default app (context menu, or double-click on a binary).
    public event Action<string>? DefaultAppRequested;

    // Item 53: explorer shortcuts. This handler sits on the tree, so it only sees keys while the explorer has focus;
    // the editor's Ctrl+C/X/V and Delete never reach it.
    public static ExplorerAction? ShortcutAction(string? id) => id switch
    {
        "explorer.cut" => ExplorerAction.Cut,
        "explorer.copy" => ExplorerAction.Copy,
        "explorer.paste" => ExplorerAction.Paste,
        "explorer.duplicate" => ExplorerAction.Duplicate,
        "explorer.delete" => ExplorerAction.Delete,
        _ => null
    };
    // Paste goes into a selected folder, beside a selected file (its folder), or into the root with nothing selected.
    public static string PasteTarget(string root, string? selectedPath, bool selectedIsDirectory) =>
        selectedPath is null ? root : selectedIsDirectory ? selectedPath : Path.GetDirectoryName(selectedPath) ?? root;
    private bool HandleExplorerShortcut(KeyEventArgs e)
    {
        if (_busy || _folder is null) return false;
        if (ShortcutAction(ShortcutKeys.Match(e, s => s.ExplorerOnly)) is not { } action) return false;
        var selected = FolderTree.SelectedItem is TreeViewItem { Tag: FolderEntry entry } ? entry : null;
        if (action == ExplorerAction.Paste)
        {
            e.Handled = true;
            if (CanPaste?.Invoke() == true) ExplorerRequested?.Invoke(new ExplorerRequest(action, PasteTarget(_folder, selected?.Path, selected?.IsDirectory == true)));
            return true;
        }
        if (selected is null) return false;
        e.Handled = true;
        ExplorerRequested?.Invoke(new ExplorerRequest(action, selected.Path));
        return true;
    }

    private void FolderTree_KeyDown(object? sender, KeyEventArgs e)
    {
        if (IsRenaming) return;
        if (HandleExplorerShortcut(e)) return;
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        if (FolderTree.SelectedItem is not TreeViewItem { Tag: FolderEntry { IsDirectory: false } entry }) return;
        e.Handled = true;
        FileActivated?.Invoke(entry.Path);
    }
    public event Action<string>? MessageChanged;
    public event Action? RefreshRequested;
    public event Action<bool>? CreateRequested;
    public event Action? OpenFolderRequested;
    public event Action<ExplorerRequest>? ExplorerRequested;
    public string? OpenedFolder => _folder;
    private ContextMenu CreateContextMenu(string path, bool isDirectory = false)
    {
        MenuItem Item(string name, string header, ExplorerAction action, string? shortcut = null)
        {
            var item = LocResources.Item(header);
            item.Name = name;
            // Item 53: right-aligned hint from the live shortcut table (follows rebinds).
            if (shortcut is not null) ShortcutKeys.SetId(item, shortcut);
            item.Click += (_, _) =>
            {
                if (action == ExplorerAction.Rename) BeginRename(path);
                // Paste on a file lands beside it (its folder); on a folder, inside it.
                else if (action == ExplorerAction.Paste) { if (_folder is not null && CanPaste?.Invoke() == true) ExplorerRequested?.Invoke(new ExplorerRequest(action, PasteTarget(_folder, path, isDirectory))); }
                else ExplorerRequested?.Invoke(new ExplorerRequest(action, path));
            };
            return item;
        }
        Separator Line() => new() { Background = Mdhub.Services.ThemeService.Brush("SeparatorForeground"), Foreground = Mdhub.Services.ThemeService.Brush("SeparatorForeground") };
        var menu = new ContextMenu
        {
            // Headers are resource keys; Localized() refreshes them on open after a language switch.
            Items =
            {
                Item("RevealMenuItem", "Reveal", ExplorerAction.Reveal), Line(),
                Item("CutMenuItem", "MenuCut", ExplorerAction.Cut, "explorer.cut"), Item("CopyMenuItem", "MenuCopy", ExplorerAction.Copy, "explorer.copy"),
                Item("PasteMenuItem", "MenuPaste", ExplorerAction.Paste, "explorer.paste"),
                Item("DuplicateMenuItem", "Duplicate", ExplorerAction.Duplicate, "explorer.duplicate"), Line(),
                Item("RenameMenuItem", "Rename", ExplorerAction.Rename, "explorer.rename"), Item("DeleteMenuItem", "Delete", ExplorerAction.Delete, "explorer.delete")
            }
        };
        ShortcutKeys.ApplyGestures(menu.Items.OfType<MenuItem>());
        UpdateItemPaste(menu);
        menu.Opening += (_, _) => UpdateItemPaste(menu);
        if (!isDirectory)
        {
            var open = LocResources.Item("OpenWithDefaultApp");
            open.Name = "OpenDefaultMenuItem";
            open.Click += (_, _) => DefaultAppRequested?.Invoke(path);
            menu.Items.Insert(0, open);
            menu.Items.Insert(1, Line());
        }
        return menu.Localized();
    }
    private void FolderName_Click(object? sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke();
    public Func<bool>? CanPaste { get; set; }
    private bool _createInRoot;
    public FolderExplorer()
    {
        InitializeComponent();
        InitializeDragDrop();
        FolderTree.AddHandler(PointerPressedEvent, FolderRow_PointerPressed, RoutingStrategies.Tunnel);
        FolderTree.AddHandler(KeyDownEvent, FolderTree_KeyDown, RoutingStrategies.Tunnel);
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
        ShortcutKeys.SetId(paste, "explorer.paste");
        var reload = Item("BackgroundReloadMenuItem", "Reload", () => RefreshFolder_Click(null, new RoutedEventArgs()));
        var menu = new ContextMenu { Items = { newFile, newFolder, Line(), paste, Line(), reload } }.Localized();
        ShortcutKeys.ApplyGestures([paste]);
        menu.Opening += (_, _) => UpdateBackgroundMenu(menu);
        UpdateBackgroundMenu(menu);
        return menu;
    }
    // Also refreshes Paste in the row menus, so it follows the explorer clipboard everywhere.
    public void UpdateBackgroundMenu()
    {
        UpdateBackgroundMenu(FolderTree.ContextMenu!);
        foreach (var row in TreeNodes(FolderTree.Items.OfType<TreeViewItem>()))
            if (row.ContextMenu is { } menu) UpdateItemPaste(menu);
    }
    private void UpdateItemPaste(ContextMenu menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
            if (item.Name == "PasteMenuItem") item.IsEnabled = _folder is not null && !_busy && CanPaste?.Invoke() == true;
    }
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
    // Item 42: New file / New folder follow the explorer selection: a selected folder gets the item inside it, a
    // selected file gets it beside it (in its folder), and with nothing selected it goes in the open folder's root.
    // The background menu's New file / New folder always use the root.
    public string GetCreationDirectory()
    {
        var root = _folder ?? throw new InvalidOperationException(Loc.T("OpenFolderFirst"));
        if (_createInRoot) { _createInRoot = false; return root; }
        if ((FolderTree.SelectedItem as TreeViewItem)?.Tag is not FolderEntry entry) return root;
        var directory = entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path);
        return directory is not null && (directory == root || Contains(directory)) ? directory : root;
    }

    // The "Create in:" text: the open folder's name, then the path below it (e.g. "notes\2026\october").
    public string DisplayDirectory(string directory)
    {
        if (_folder is null) return directory;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(_folder));
        if (string.IsNullOrEmpty(name)) return directory;
        var relative = Path.GetRelativePath(_folder, directory);
        if (relative == ".") return name;
        return Path.IsPathRooted(relative) || relative.StartsWith("..") ? directory : Path.Combine(name, relative);
    }
    private FileSystemWatcher? _watcher;
    private Avalonia.Threading.DispatcherTimer? _watchTimer;
    private int _refreshing;
    private readonly HashSet<TreeViewItem> _loaded = [];
    public event Action? ExternalChangeRefreshed;
    public bool WatchEnabled { get; set; } = true;

    // Bug 54: updates the tree in place instead of rebuilding it. Rows that still exist keep their
    // TreeViewItem, so expanded folders, the selection and the scroll offset survive; only rows that
    // appeared or vanished are inserted or removed (in sorted position), and loaded folders are
    // reconciled recursively. Unloaded folders are left alone: they read their children when expanded.
    public async Task RefreshAsync()
    {
        if (_folder is null) return;
        if (!_renameSaving) CancelRename();
        _refreshing++;
        try
        {
            var entries = await FolderService.ReadAsync(_folder);
            await ReconcileAsync(FolderTree.Items, entries);
        }
        finally { _refreshing--; }
    }
    private async Task ReconcileAsync(ItemCollection items, IReadOnlyList<FolderEntry> entries)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var existing = new Dictionary<string, TreeViewItem>(comparer);
        foreach (var item in items.OfType<TreeViewItem>())
            if (item.Tag is FolderEntry e) existing.TryAdd(e.Path, item);
        var desired = new List<TreeViewItem>(entries.Count);
        foreach (var entry in entries)
        {
            // Same path but now a different kind (file <-> folder, link): rebuild that one row.
            if (existing.TryGetValue(entry.Path, out var keep) && keep.Tag is FolderEntry old && old == entry
                && Path.GetFileName(old.Path) == Path.GetFileName(entry.Path))
            {
                existing.Remove(entry.Path);
                desired.Add(keep);
            }
            else desired.Add(CreateTreeItem(entry));
        }
        var wanted = desired.ToHashSet();
        foreach (var stale in items.OfType<object>().Where(i => i is not TreeViewItem t || !wanted.Contains(t)).ToList())
        {
            items.Remove(stale);
            if (stale is TreeViewItem t) Forget(t);
        }
        for (var i = 0; i < desired.Count; i++)
        {
            if (i < items.Count && ReferenceEquals(items[i], desired[i])) continue;
            items.Remove(desired[i]);
            items.Insert(i, desired[i]);
        }
        foreach (var item in desired)
        {
            if (!_loaded.Contains(item) || item.Tag is not FolderEntry folder) continue;
            IReadOnlyList<FolderEntry> children;
            try { children = await FolderService.ReadAsync(folder.Path); }
            catch { continue; } // vanished between reads: the next watcher pass removes it
            await ReconcileAsync(item.Items, children);
        }
    }
    private void Forget(TreeViewItem item)
    {
        foreach (var node in TreeNodes([item])) { _loadChildren.Remove(node); _loaded.Remove(node); }
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
        _loaded.Clear();
        FolderActions.IsEnabled = !_busy;
        FolderName.Text = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        ToolTip.SetTip(FolderNameButton, Loc.F("FolderTip", path));
        FolderTree.Items.Clear();
        foreach (var entry in entries) FolderTree.Items.Add(CreateTreeItem(entry));
        // Only announce an actual open: a refresh (manual or after an external change) must not overwrite
        // the status bar with "Opened folder" (it replaced the note path and the "Path copied" confirmation).
        if (restartWatcher) MessageChanged?.Invoke(Loc.F("OpenedFolder", path));
    }
    private TreeViewItem CreateTreeItem(FolderEntry entry)
    {
        var item = new ExplorerTreeViewItem { Header = entry.IsDirectory ? Path.GetFileName(entry.Path) : new FileRowHeader(entry.Path, !entry.IsSupported), Tag = entry };
        ToolTip.SetTip(item, TipFor(entry));
        // VS Code: a file's icon takes the chevron's slot (see the TreeViewItem.file style).
        if (!entry.IsDirectory) item.Classes.Add("file");
        if (!entry.IsDirectory && !entry.IsSupported)
        {
            item.Opacity = 0.4;
        }
        item.ContextMenu = CreateContextMenu(entry.Path, entry.IsDirectory);
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
                    _loaded.Add(item);
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
        // VS Code: a single click opens a file and keeps focus here; a double-click moves focus to the editor.
        if (e.ClickCount == 2 && ItemAt(e.Source) is { Tag: FolderEntry { IsDirectory: false } file })
        {
            if (file.IsSupported) FileActivated?.Invoke(file.Path); else DefaultAppRequested?.Invoke(file.Path);
            e.Handled = true;
            return;
        }
        if (ItemAt(e.Source) is not { Tag: FolderEntry { IsDirectory: true, IsLink: false } } item) return;
        FolderTree.SelectedItem = item;
        item.Focus();
        item.IsExpanded = !item.IsExpanded;
        // Handle the arrow and row together, preventing the default double-click toggle.
        e.Handled = true;
    }

    private void FolderTree_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_marking || IsRenaming || FolderTree.SelectedItem is not TreeViewItem { Tag: FolderEntry entry } || entry.IsDirectory) return;
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
        await RevealAsync(path);
    }

    // Item 41 (Reveal in Side Bar): selects the path, expanding (and loading) its parent folders, and scrolls it into
    // view. quiet: selecting does not raise FileRequested (the file is already open). Returns false outside the folder.
    public async Task<bool> RevealAsync(string path, bool quiet = false, bool focus = false)
    {
        if (_folder is null) return false;
        var relative = Path.GetRelativePath(_folder, Path.GetFullPath(path));
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar)) return false;
        var parts = relative.Split(Path.DirectorySeparatorChar);
        var items = FolderTree.Items.OfType<TreeViewItem>();
        for (var index = 0; index < parts.Length; index++)
        {
            var item = items.FirstOrDefault(i => i.Tag is FolderEntry entry && string.Equals(Path.GetFileName(entry.Path), parts[index],
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            if (item is null) return false;
            if (index == parts.Length - 1)
            {
                _marking = quiet;
                try { FolderTree.SelectedItem = item; }
                finally { _marking = false; }
                item.BringIntoView();
                // Freshly expanded rows are laid out on the next pass: scroll again once they have bounds.
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { item.BringIntoView(); if (focus) item.Focus(); }, Avalonia.Threading.DispatcherPriority.Loaded);
                if (focus) item.Focus();
                return true;
            }
            if (_loadChildren.TryGetValue(item, out var load)) await load();
            item.IsExpanded = true;
            items = item.Items.OfType<TreeViewItem>();
        }
        return false;
    }

    // Like VS Code, the selected row follows the active tab (only among rows already loaded; folders are not expanded).
    // Selecting here does not raise FileRequested.
    private bool _marking;
    public void MarkActive(string? path)
    {
        if (path is null || IsRenaming) return;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (FolderTree.SelectedItem is TreeViewItem { Tag: FolderEntry current } && string.Equals(current.Path, path, comparison)) return;
        var item = TreeNodes(FolderTree.Items.OfType<TreeViewItem>()).FirstOrDefault(i => i.Tag is FolderEntry e && string.Equals(e.Path, path, comparison));
        if (item is null) return;
        _marking = true;
        try { FolderTree.SelectedItem = item; }
        finally { _marking = false; }
        item.BringIntoView();
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
