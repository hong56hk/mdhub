using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Mdhub.Models;

namespace Mdhub.Components;

public partial class FileTabs
{
    private NoteDocument? _dragDocument;
    private IPointer? _dragPointer;
    private Point _dragOrigin;
    private double _lastDragX;
    private bool _tabDragging, _reordering;

    private void InitializeReordering()
    {
        Tabs.AddHandler(PointerPressedEvent, TabDragPressed, RoutingStrategies.Tunnel, true);
        Tabs.AddHandler(PointerMovedEvent, TabDragMoved, RoutingStrategies.Tunnel, true);
        Tabs.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.Pointer != _dragPointer || e.InitialPressMouseButton != MouseButton.Left) return;
            if (_tabDragging) e.Handled = true;
            ClearTabDrag();
        }, RoutingStrategies.Tunnel, true);
        Tabs.PointerCaptureLost += (_, e) => { if (e.Pointer == _dragPointer && e.Pointer.Captured != Tabs) ClearTabDrag(); };
        DetachedFromVisualTree += (_, _) => ClearTabDrag();
    }

    private void TabDragPressed(object? sender, PointerPressedEventArgs e)
    {
        ClearTabDrag();
        if (!Tabs.IsEnabled || !e.GetCurrentPoint(Tabs).Properties.IsLeftButtonPressed) return;
        if (e.Source is not Visual source) return;
        var ancestors = source.GetSelfAndVisualAncestors().TakeWhile(v => v != Tabs).ToList();
        if (ancestors.Any(v => v is Button or ScrollBar)) return;
        if (ancestors.OfType<ListBoxItem>().FirstOrDefault()?.DataContext is not NoteDocument document) return;
        _dragDocument = document;
        _dragPointer = e.Pointer;
        _dragOrigin = e.GetPosition(Tabs);
        _lastDragX = _dragOrigin.X;
    }

    private void TabDragMoved(object? sender, PointerEventArgs e)
    {
        if (_dragDocument is not { } document || e.Pointer != _dragPointer) return;
        if (!e.GetCurrentPoint(Tabs).Properties.IsLeftButtonPressed) { ClearTabDrag(); return; }
        var point = e.GetPosition(Tabs);
        if (!_tabDragging)
        {
            if (Math.Abs(point.X - _dragOrigin.X) < 6) return;
            _tabDragging = true;
            e.Pointer.Capture(Tabs);
            SelectedDocument = document;
        }
        e.Handled = true;
        if (Tabs.ItemsSource is not ObservableCollection<NoteDocument> documents) return;
        var from = documents.IndexOf(document);
        if (from < 0) { ClearTabDrag(); return; }
        var to = from;
        // Cross the neighbour's midpoint in the direction of travel. This prevents a
        // wide tab from jumping back when moving it changes the neighbouring widths.
        for (var i = 0; i < documents.Count; i++)
        {
            if (i == from || Tabs.ContainerFromIndex(i) is not ListBoxItem item) continue;
            if (item.TranslatePoint(new Point(item.Bounds.Width / 2, 0), Tabs) is not { } middle) continue;
            if (point.X > _lastDragX && i > from && point.X >= middle.X) to = i;
            if (point.X < _lastDragX && i < from && point.X <= middle.X) { to = i; break; }
        }
        _lastDragX = point.X;
        if (to == from) return;
        _reordering = true;
        try
        {
            documents.Move(from, to);
            SelectedDocument = document;
        }
        finally { _reordering = false; }
    }

    private void ClearTabDrag()
    {
        var pointer = _dragPointer;
        _dragDocument = null;
        _dragPointer = null;
        _tabDragging = false;
        if (pointer?.Captured == Tabs) pointer.Capture(null);
    }
}
