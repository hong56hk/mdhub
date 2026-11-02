using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub.Services;

namespace Mdhub.Components;

public partial class FolderExplorer
{
    // Paths: every top-level item of a multi-selection (item 80), else just Path.
    private sealed record DraggedFile(FolderExplorer Owner, string Path, IReadOnlyList<string>? Paths = null);
    private static readonly DataFormat<DraggedFile> FileDragFormat = DataFormat.CreateInProcessFormat<DraggedFile>("mdhub.explorer.file");
    private PointerPressedEventArgs? _dragPress;
    private Point _dragOrigin;
    private string? _pendingDragPath;
    private IReadOnlyList<string>? _pendingDragPaths;
    private bool _dragging;
    private Control? _dropTarget;
    private readonly DispatcherTimer _expandTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    public event Action<ExplorerMoveRequest>? MoveRequested;
    public event Action<ExplorerImportRequest>? ImportRequested;

    private void InitializeDragDrop()
    {
        FolderTree.AddHandler(PointerPressedEvent, OnDragPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        FolderTree.AddHandler(PointerMovedEvent, OnDragMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        FolderTree.AddHandler(PointerReleasedEvent, (_, _) => ClearPendingDrag(), RoutingStrategies.Tunnel, handledEventsToo: true);
        DragDrop.SetAllowDrop(FolderTree, true);
        DragDrop.SetAllowDrop(FolderNameButton, true);
        DragDrop.SetAllowDrop(FolderPanel, true);
        foreach (var surface in new Control[] { FolderTree, FolderNameButton, FolderPanel })
        {
            surface.AddHandler(DragDrop.DragOverEvent, OnDragOver);
            surface.AddHandler(DragDrop.DragLeaveEvent, (_, _) => ClearDropTarget());
            surface.AddHandler(DragDrop.DropEvent, OnFileDrop);
        }
        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer.Stop();
            if (_dropTarget is TreeViewItem { Tag: FolderEntry { IsDirectory: true, IsLink: false } } item) item.IsExpanded = true;
        };
        DetachedFromVisualTree += (_, _) => { ClearPendingDrag(); ClearDropTarget(); };
    }

    private static TreeViewItem? ItemAt(object? source) => source is Visual visual
        ? visual.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault() : null;

    private void OnDragPressed(object? sender, PointerPressedEventArgs e)
    {
        ClearPendingDrag();
        if (_busy || _dragging || IsRenaming || !e.GetCurrentPoint(FolderTree).Properties.IsLeftButtonPressed) return;
        var pressed = ItemAt(e.Source);
        if (IsMultiSelect && pressed is not null && _multi.Contains(pressed) && ActionPaths is { Count: > 0 } paths)
        {
            _dragPress = e;
            _dragOrigin = e.GetPosition(FolderTree);
            _pendingDragPath = paths[0];
            _pendingDragPaths = paths;
            return;
        }
        if (pressed?.Tag is not FolderEntry { IsDirectory: false, IsLink: false } entry) return;
        _dragPress = e;
        _dragOrigin = e.GetPosition(FolderTree);
        _pendingDragPath = entry.Path;
    }

    private async void OnDragMoved(object? sender, PointerEventArgs e)
    {
        if (_busy || _dragging || _dragPress is null || _pendingDragPath is null) return;
        if (!e.GetCurrentPoint(FolderTree).Properties.IsLeftButtonPressed) { ClearPendingDrag(); return; }
        var delta = e.GetPosition(FolderTree) - _dragOrigin;
        if (Math.Abs(delta.X) < 6 && Math.Abs(delta.Y) < 6) return;
        var press = _dragPress;
        var path = _pendingDragPath;
        var paths = _pendingDragPaths;
        ClearPendingDrag();
        _clearOnRelease = null;
        _dragging = true;
        try
        {
            using var data = new DataTransfer();
            data.Add(DataTransferItem.Create(FileDragFormat, new DraggedFile(this, path, paths)));
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
        }
        catch (Exception ex) { MessageChanged?.Invoke(Mdhub.Localization.Loc.F("DragFailed", ex.Message)); }
        finally { _dragging = false; ClearDropTarget(); }
    }

    private ExplorerMoveRequest? DropRequest(object? sender, DragEventArgs e, out Control? highlight)
    {
        highlight = null;
        if (_busy || _folder is null || e.DataTransfer.TryGetValue(FileDragFormat) is not { } file || file.Owner != this) return null;
        var item = ReferenceEquals(sender, FolderNameButton) ? null : ItemAt(e.Source);
        var directory = item?.Tag is FolderEntry entry ? (entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path)!) : _folder;
        var sources = file.Paths ?? [file.Path];
        if (!sources.Any(source => ExplorerMove.Destination(_folder, source, directory) is not null)) return null;
        highlight = item?.Tag is FolderEntry { IsDirectory: true } ? item
            : item?.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault() as Control ?? FolderNameButton;
        return new(file.Path, directory, file.Paths);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        var request = DropRequest(sender, e, out var highlight);
        var import = request is null ? ImportRequest(sender, e, out highlight) : null;
        e.DragEffects = request is not null ? DragDropEffects.Move : import is not null ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (_dropTarget == highlight) return;
        ClearDropTarget();
        _dropTarget = highlight;
        _dropTarget?.Classes.Add("dropTarget");
        if (_dropTarget is TreeViewItem) _expandTimer.Start();
    }

    private void OnFileDrop(object? sender, DragEventArgs e)
    {
        var request = DropRequest(sender, e, out _);
        var import = request is null ? ImportRequest(sender, e, out _) : null;
        ClearDropTarget();
        e.Handled = true;
        e.DragEffects = request is not null ? DragDropEffects.Move : import is not null ? DragDropEffects.Copy : DragDropEffects.None;
        if (request is not null) MoveRequested?.Invoke(request);
        else if (import is not null) ImportRequested?.Invoke(import);
    }

    private ExplorerImportRequest? ImportRequest(object? sender, DragEventArgs e, out Control? highlight)
    {
        highlight = null;
        if (_busy || IsRenaming || _folder is null || !e.DragEffects.HasFlag(DragDropEffects.Copy)
            || e.DataTransfer.TryGetValue(FileDragFormat) is not null) return null;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return null;
        var item = ReferenceEquals(sender, FolderNameButton) ? null : ItemAt(e.Source);
        var directory = item?.Tag is FolderEntry entry ? (entry.IsDirectory ? entry.Path : Path.GetDirectoryName(entry.Path)!) : _folder;
        var sources = files.Select(f => f.TryGetLocalPath()).OfType<string>()
            .Where(p => ExplorerImport.Destination(_folder, p, directory) is not null)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToList();
        if (sources.Count == 0) return null;
        highlight = item?.Tag is FolderEntry { IsDirectory: true } ? item
            : item?.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault() as Control ?? FolderNameButton;
        return new(sources, directory);
    }

    private void ClearPendingDrag() { _dragPress = null; _pendingDragPath = null; _pendingDragPaths = null; }
    private void ClearDropTarget()
    {
        _expandTimer.Stop();
        _dropTarget?.Classes.Remove("dropTarget");
        _dropTarget = null;
    }
}
