using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Mdhub.Localization;
using Mdhub.Services;

namespace Mdhub.Components;

// Item 80: VS Code-style multi-select. The TreeView's SelectedItem stays the focused row (it keeps the focus
// border and drives New file/folder and Paste); the extra rows live in _multi and carry the multiSelected class.
public partial class FolderExplorer
{
    private readonly List<TreeViewItem> _multi = [];
    private TreeViewItem? _anchor;
    private TreeViewItem? _clearOnRelease;
    private ContextMenu? _multiMenu;
    public event Action<IReadOnlyList<string>>? FilesActivated;

    public bool IsMultiSelect => _multi.Count > 1;
    // The selected paths in tree order (the focused row alone when there is no multi-selection).
    public IReadOnlyList<string> SelectedPaths => IsMultiSelect
        ? VisibleRows().Where(_multi.Contains).Select(r => ((FolderEntry)r.Tag!).Path).ToList()
        : FolderTree.SelectedItem is TreeViewItem { Tag: FolderEntry entry } ? [entry.Path] : [];
    // A folder selected together with its children acts on the folder only.
    public IReadOnlyList<string> ActionPaths => TopLevelOnly(SelectedPaths);
    public ContextMenu MultiMenu => _multiMenu ??= CreateMultiMenu();

    public static IReadOnlyList<string> TopLevelOnly(IEnumerable<string> paths)
    {
        var list = paths.Select(p => Path.GetFullPath(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return list.Where(p => !list.Any(o => !string.Equals(o, p, StringComparison.OrdinalIgnoreCase) && ExplorerFileOperations.Contains(o, p))).ToList();
    }

    private void InitializeSelection()
    {
        FolderTree.AddHandler(PointerReleasedEvent, OnSelectionReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        FolderTree.AddHandler(ContextRequestedEvent, OnMultiContextRequested, RoutingStrategies.Tunnel);
    }

    private List<TreeViewItem> VisibleRows()
    {
        var rows = new List<TreeViewItem>();
        void Walk(IEnumerable<TreeViewItem> items)
        {
            foreach (var item in items)
            {
                if (item.Tag is not FolderEntry) continue;
                rows.Add(item);
                if (item.IsExpanded) Walk(item.Items.OfType<TreeViewItem>());
            }
        }
        Walk(FolderTree.Items.OfType<TreeViewItem>());
        return rows;
    }

    private void SetMulti(IEnumerable<TreeViewItem> rows)
    {
        foreach (var row in _multi) row.Classes.Remove("multiSelected");
        _multi.Clear();
        foreach (var row in rows.Distinct()) { _multi.Add(row); row.Classes.Add("multiSelected"); }
        if (_multi.Count == 1) { _multi[0].Classes.Remove("multiSelected"); _multi.Clear(); }
    }
    public void ClearMultiSelection() => SetMulti([]);

    // Moves the focus (SelectedItem) without opening the row's file.
    private void FocusRow(TreeViewItem row)
    {
        _marking = true;
        try { FolderTree.SelectedItem = row; }
        finally { _marking = false; }
        row.Focus();
        row.BringIntoView();
    }

    private IEnumerable<TreeViewItem> Range(List<TreeViewItem> rows, TreeViewItem from, TreeViewItem to)
    {
        int a = rows.IndexOf(from), b = rows.IndexOf(to);
        if (a < 0) a = b;
        return rows.GetRange(Math.Min(a, b), Math.Abs(a - b) + 1);
    }

    private List<TreeViewItem> CurrentSelection() => IsMultiSelect ? [.. _multi]
        : FolderTree.SelectedItem is TreeViewItem { Tag: FolderEntry } focused ? [focused] : [];

    // Ctrl toggles a row, Shift selects the visible range from the anchor, Ctrl+Shift adds that range.
    public void ModifierSelect(TreeViewItem row, bool ctrl, bool shift)
    {
        var rows = VisibleRows();
        if (!rows.Contains(row)) return;
        if (shift)
        {
            var anchor = _anchor is not null && rows.Contains(_anchor) ? _anchor
                : FolderTree.SelectedItem as TreeViewItem is { } f && rows.Contains(f) ? f : row;
            _anchor = anchor;
            var range = Range(rows, anchor, row);
            SetMulti(ctrl ? CurrentSelection().Concat(range) : range);
        }
        else
        {
            var current = CurrentSelection();
            if (!current.Remove(row)) current.Add(row);
            _anchor = row;
            SetMulti(current);
            if (current.Count == 0) return;
            if (!current.Contains(row)) { FocusRow(current[^1]); return; }
        }
        FocusRow(row);
    }

    public void SelectAllVisible()
    {
        var rows = VisibleRows();
        if (rows.Count == 0) return;
        SetMulti(rows);
        if (FolderTree.SelectedItem is not TreeViewItem focused || !rows.Contains(focused)) FocusRow(rows[0]);
    }

    private void ExtendByKey(int step)
    {
        var rows = VisibleRows();
        if (FolderTree.SelectedItem is not TreeViewItem focused || rows.IndexOf(focused) is var index && index < 0) return;
        var next = rows[Math.Clamp(index + step, 0, rows.Count - 1)];
        if (_anchor is null || !rows.Contains(_anchor)) _anchor = focused;
        SetMulti(Range(rows, _anchor, next));
        FocusRow(next);
    }

    // Returns true when the key was a multi-select gesture.
    private bool HandleSelectionKey(KeyEventArgs e)
    {
        if ((e.Key is Key.Up or Key.Down) && e.KeyModifiers == KeyModifiers.Shift)
        {
            e.Handled = true;
            ExtendByKey(e.Key == Key.Up ? -1 : 1);
            return true;
        }
        if (ShortcutKeys.Match(e, s => s.ExplorerOnly) == "explorer.selectAll")
        {
            e.Handled = true;
            SelectAllVisible();
            return true;
        }
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None && IsMultiSelect)
        {
            e.Handled = true;
            ClearMultiSelection();
            return true;
        }
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && IsMultiSelect)
        {
            e.Handled = true;
            var files = VisibleRows().Where(_multi.Contains).Select(r => (FolderEntry)r.Tag!).Where(f => !f.IsDirectory).Select(f => f.Path).ToList();
            if (files.Count > 0) FilesActivated?.Invoke(files);
            return true;
        }
        return false;
    }

    // Tunnel press: modifier clicks change the selection without opening; a plain click on a selected row waits
    // for the release so the whole selection can still be dragged.
    private bool HandleSelectionPress(PointerPressedEventArgs e)
    {
        _clearOnRelease = null;
        if (ItemAt(e.Source) is not { Tag: FolderEntry } row) return false;
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        bool ctrl = e.KeyModifiers.HasFlag(command), shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (ctrl || shift)
        {
            e.Handled = true;
            if (e.ClickCount == 1) ModifierSelect(row, ctrl, shift);
            return true;
        }
        if (IsMultiSelect && _multi.Contains(row) && e.ClickCount == 1)
        {
            e.Handled = true;
            _clearOnRelease = row;
            return true;
        }
        _anchor = row;
        ClearMultiSelection();
        return false;
    }

    private void OnSelectionReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_clearOnRelease is not { } row) return;
        _clearOnRelease = null;
        if (_dragging) return;
        ClearMultiSelection();
        _anchor = row;
        if (row.Tag is FolderEntry { IsDirectory: true, IsLink: false }) { FocusRow(row); row.IsExpanded = !row.IsExpanded; }
        else if (FolderTree.SelectedItem == row && row.Tag is FolderEntry file) { row.Focus(); FileRequested?.Invoke(file.Path); }
        else { FolderTree.SelectedItem = row; row.Focus(); }
    }

    // A collapsed folder's hidden children leave the selection.
    private void DeselectHidden(TreeViewItem folder)
    {
        if (_multi.Count == 0) return;
        var hidden = TreeNodes(folder.Items.OfType<TreeViewItem>()).ToHashSet();
        if (!_multi.Any(hidden.Contains)) return;
        SetMulti(_multi.Where(r => !hidden.Contains(r)).ToList());
        if (_anchor is not null && hidden.Contains(_anchor)) _anchor = folder;
    }
    private void ForgetSelection(IEnumerable<TreeViewItem> nodes)
    {
        var gone = nodes.ToHashSet();
        if (_anchor is not null && gone.Contains(_anchor)) _anchor = null;
        if (_multi.Any(gone.Contains)) SetMulti(_multi.Where(r => !gone.Contains(r)).ToList());
    }

    private void OnMultiContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (!IsMultiSelect || ItemAt(e.Source) is not { } row) return;
        if (!_multi.Contains(row)) { ClearMultiSelection(); return; }
        e.Handled = true;
        MultiMenu.Open(row);
    }

    private void RequestMulti(ExplorerAction action)
    {
        var paths = ActionPaths;
        if (paths.Count > 0) ExplorerRequested?.Invoke(new ExplorerRequest(action, paths[0], paths));
    }

    // Only the actions that make sense for many items (no Rename, New, Open or Duplicate).
    private ContextMenu CreateMultiMenu()
    {
        MenuItem Item(string name, string header, ExplorerAction action, string? shortcut = null)
        {
            var item = LocResources.Item(header);
            item.Name = name;
            if (shortcut is not null) ShortcutKeys.SetId(item, shortcut);
            item.Click += (_, _) => RequestMulti(action);
            return item;
        }
        Separator Line() => new() { Background = ThemeService.Brush("SeparatorForeground"), Foreground = ThemeService.Brush("SeparatorForeground") };
        var menu = new ContextMenu
        {
            Items =
            {
                Item("MultiRevealMenuItem", "Reveal", ExplorerAction.Reveal), Line(),
                Item("MultiCutMenuItem", "MenuCut", ExplorerAction.Cut, "explorer.cut"), Item("MultiCopyMenuItem", "MenuCopy", ExplorerAction.Copy, "explorer.copy"),
                Item("MultiCopyPathMenuItem", "CopyPaths", ExplorerAction.CopyPath), Line(),
                Item("MultiDeleteMenuItem", "Delete", ExplorerAction.Delete, "explorer.delete")
            }
        };
        ShortcutKeys.ApplyGestures(menu.Items.OfType<MenuItem>());
        return menu.Localized();
    }
}
