using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Mdhub.Services;

namespace Mdhub.Components;

public partial class FolderExplorer
{
    private TextBox? _renameBox;
    private TreeViewItem? _renameItem;
    private object? _renameHeader;
    private bool _renameSaving;
    private string? _renameErrorText;
    public bool IsRenaming => _renameBox is not null;
    public Func<string, string, Task<string?>>? RenameRequested { get; set; }

    public void BeginRename(string? path = null)
    {
        if (_busy || _renameSaving) return;
        var item = path is null ? FolderTree.SelectedItem as TreeViewItem
            : TreeNodes(FolderTree.Items.OfType<TreeViewItem>()).FirstOrDefault(i => i.Tag is FolderEntry e && e.Path == path);
        if (item?.Tag is not FolderEntry entry) return;
        CancelRename();
        var name = Path.GetFileName(entry.Path);
        var box = new TextBox { Name = "ExplorerRenameBox", Text = name, MinWidth = 100, Padding = new Thickness(3, 0), BorderThickness = new Thickness(1), FontSize = 13 };
        // Pull the box left by its border and padding so the edited text starts where the row's name starts.
        box.Margin = new Thickness(-(box.Padding.Left + box.BorderThickness.Left), 0, 0, 0);
        _renameItem = item;
        _renameHeader = item.Header;
        _renameBox = box;
        FolderTree.SelectedItem = item;
        item.Header = box;
        box.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; CancelRename(); FocusTree(); }
            else if (e.Key == Key.Enter) { e.Handled = true; await CommitRenameAsync(keepFocus: true); }
        }, RoutingStrategies.Tunnel);
        box.TextChanged += (_, _) => ClearRenameError(box);
        // Leaving the field commits, like VS Code; an invalid name keeps the box open with its error.
        box.LostFocus += async (_, _) => { if (_renameBox == box && !_renameSaving) await CommitRenameAsync(keepFocus: false); };
        Dispatcher.UIThread.Post(() =>
        {
            if (_renameBox != box) return;
            box.Focus();
            box.SelectionStart = 0;
            box.SelectionEnd = RenameSelectionLength(name, entry.IsDirectory);
        }, DispatcherPriority.Loaded);
    }

    // Case-only renames (notes.md -> Notes.md) are allowed; any other existing sibling is a duplicate.
    public static string? DuplicateError(string path, string name)
    {
        var current = Path.GetFileName(path);
        if (string.Equals(current, name, StringComparison.OrdinalIgnoreCase)) return null;
        var target = Path.Combine(Path.GetDirectoryName(path)!, name);
        return File.Exists(target) || Directory.Exists(target) ? Mdhub.Localization.Loc.F("NameExistsIn", name) : null;
    }

    // Selection for the inline box: the name without its extension; folders, extensionless names and dotfiles select everything.
    public static int RenameSelectionLength(string name, bool isDirectory)
    {
        if (isDirectory) return name.Length;
        var dot = name.LastIndexOf('.');
        return dot > 0 ? dot : name.Length;
    }

    private void ClearRenameError(TextBox box)
    {
        // TextChanged can arrive after the commit that set the error, so only a real edit clears it.
        if (ToolTip.GetTip(box) is null || box.Text == _renameErrorText) return;
        box.ClearValue(TextBox.BorderBrushProperty);
        ToolTip.SetTip(box, null);
        box.Classes.Remove("renameError");
    }

    private async Task CommitRenameAsync(bool keepFocus)
    {
        if (_renameSaving || _renameBox is not { } box || _renameItem?.Tag is not FolderEntry entry) return;
        var name = box.Text ?? "";
        var error = FolderService.ValidateName(name) ?? DuplicateError(entry.Path, name);
        if (error is null && name == Path.GetFileName(entry.Path)) { CancelRename(); if (keepFocus) FocusTree(); return; }
        if (error is null && RenameRequested is { } rename)
        {
            _renameSaving = true;
            box.IsReadOnly = true;
            try { error = await rename(entry.Path, name); }
            catch (Exception ex) { error = ex.Message; }
            finally { _renameSaving = false; box.IsReadOnly = false; }
        }
        if (_renameBox != box) return;
        if (error is null) { CancelRename(); if (keepFocus) FocusTree(); }
        else
        {
            box.BorderBrush = Mdhub.Services.ThemeService.Brush("RenameErrorBorder");
            box.Classes.Add("renameError");
            _renameErrorText = name;
            ToolTip.SetTip(box, error);
            MessageChanged?.Invoke(error);
            if (keepFocus) box.Focus();
        }
    }

    private void CancelRename()
    {
        var item = _renameItem;
        var header = _renameHeader;
        _renameBox = null;
        _renameItem = null;
        _renameHeader = null;
        if (item is not null) item.Header = header;
    }
}
