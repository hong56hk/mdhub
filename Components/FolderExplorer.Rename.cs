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
        var box = new TextBox { Name = "ExplorerRenameBox", Text = name, MinWidth = 100, Padding = new Thickness(3, 0), FontSize = 13 };
        _renameItem = item;
        _renameHeader = item.Header;
        _renameBox = box;
        FolderTree.SelectedItem = item;
        item.Header = box;
        box.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; CancelRename(); FolderTree.Focus(); }
            else if (e.Key == Key.Enter) { e.Handled = true; await CommitRenameAsync(); }
        }, RoutingStrategies.Tunnel);
        // Leaving the field cancels; Enter is the explicit commit.
        box.LostFocus += (_, _) => { if (!_renameSaving) CancelRename(); };
        Dispatcher.UIThread.Post(() =>
        {
            if (_renameBox != box) return;
            box.Focus();
            box.SelectionStart = 0;
            var stemLength = entry.IsDirectory ? name.Length : Path.GetFileNameWithoutExtension(name).Length;
            box.SelectionEnd = stemLength > 0 ? stemLength : name.Length;
        }, DispatcherPriority.Loaded);
    }

    private async Task CommitRenameAsync()
    {
        if (_renameSaving || _renameBox is not { } box || _renameItem?.Tag is not FolderEntry entry) return;
        var name = box.Text ?? "";
        var error = FolderService.ValidateName(name);
        if (error is null && name == Path.GetFileName(entry.Path)) { CancelRename(); FolderTree.Focus(); return; }
        if (error is null && RenameRequested is { } rename)
        {
            _renameSaving = true;
            box.IsReadOnly = true;
            try { error = await rename(entry.Path, name); }
            catch (Exception ex) { error = ex.Message; }
            finally { _renameSaving = false; box.IsReadOnly = false; }
        }
        if (error is null) { CancelRename(); FolderTree.Focus(); }
        else
        {
            box.BorderBrush = Brushes.IndianRed;
            ToolTip.SetTip(box, error);
            MessageChanged?.Invoke(error);
            box.Focus();
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
