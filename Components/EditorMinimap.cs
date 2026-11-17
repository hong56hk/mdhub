using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub.Services;

namespace Mdhub.Components;

// Reuse the editor's shaped, syntax-coloured text, including wrapped lines. Navigation changes
// only the scroll offset, leaving the caret, selection and undo history untouched.
public sealed class EditorMinimap : Control
{
    private TextBox? _editor;
    private TextPresenter? _presenter;
    private ScrollViewer? _scroll;
    private double? _dragOffset;
    private readonly DispatcherTimer _snapshotTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private RenderTargetBitmap? _snapshot;
    private TextLayout? _snapshotLayout;
    private Size _snapshotSize;
    private bool _snapshotDirty = true;

    public EditorMinimap()
    {
        Width = 100;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
        _snapshotTimer.Tick += (_, _) => { _snapshotTimer.Stop(); RebuildSnapshot(); InvalidateVisual(); };
        AttachedToVisualTree += (_, _) =>
        {
            _editor = this.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
            EditorBehavior.Changed += UpdateVisibility;
            UpdateVisibility();
            LayoutUpdated += OnLayout;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            EditorBehavior.Changed -= UpdateVisibility;
            LayoutUpdated -= OnLayout;
            _snapshotTimer.Stop();
            Unhook();
            _editor = null;
            DisposeSnapshot();
        };
    }

    private void UpdateVisibility() => IsVisible = EditorBehavior.Current.Minimap;

    private void OnLayout(object? sender, EventArgs e)
    {
        Hook();
        var layout = _presenter?.TextLayout;
        if (!ReferenceEquals(layout, _snapshotLayout) || _snapshotSize != Bounds.Size) _snapshotDirty = true;
        if (_snapshotDirty) { _snapshotTimer.Stop(); _snapshotTimer.Start(); }
        InvalidateVisual();
    }
    private void OnScroll(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();
    private void Hook()
    {
        if (_presenter is not null || _editor is null) return;
        _presenter = _editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        _scroll = _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scroll is not null) _scroll.ScrollChanged += OnScroll;
    }
    private void Unhook()
    {
        if (_scroll is not null) _scroll.ScrollChanged -= OnScroll;
        _presenter = null;
        _scroll = null;
        _dragOffset = null;
        _snapshotLayout = null;
        _snapshotDirty = true;
    }

    private double Scale => _scroll is null ? 0 : Math.Min(0.16, Bounds.Height / Math.Max(1, _scroll.Extent.Height));
    internal Rect ViewportRect => _scroll is null ? default : new Rect(0, _scroll.Offset.Y * Scale,
        Bounds.Width, Math.Min(Bounds.Height, _scroll.Viewport.Height * Scale));

    void DisposeSnapshot()
    {
        _snapshot?.Dispose();
        _snapshot = null;
        _snapshotLayout = null;
    }

    void RebuildSnapshot()
    {
        Hook();
        if (_presenter is null || _scroll is null || Bounds.Width <= 0 || Bounds.Height <= 0) { DisposeSnapshot(); return; }
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height));
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(ThemeService.Brush("EditorTextBackground"), new Rect(0, 0, width, height));
            var scale = Scale;
            if (scale > 0)
            {
                using (context.PushClip(new Rect(0, 0, width, height)))
                using (context.PushTransform(Matrix.CreateScale(0.16, scale)))
                    _presenter.TextLayout.Draw(context, new Point(8, 0));
            }
        }
        _snapshot?.Dispose();
        _snapshot = bitmap;
        _snapshotLayout = _presenter.TextLayout;
        _snapshotSize = Bounds.Size;
        _snapshotDirty = false;
    }

    public override void Render(DrawingContext context)
    {
        Hook();
        context.FillRectangle(ThemeService.Brush("EditorTextBackground"), new Rect(Bounds.Size));
        if (_presenter is null || _scroll is null || Scale <= 0) return;
        // First paint (open) builds a snapshot now; later text changes are debounced so typing stays smooth.
        if (_snapshot is null && _snapshotDirty) RebuildSnapshot();
        if (_snapshot is not null)
            context.DrawImage(_snapshot, new Rect(Bounds.Size));
        using (context.PushOpacity(0.22))
            context.FillRectangle(ThemeService.Brush("EditorForeground"), ViewportRect);
    }

    private void ScrollTo(double y, double grabOffset)
    {
        if (_scroll is null || Scale <= 0) return;
        var offset = Math.Clamp((y - grabOffset) / Scale, 0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height));
        _scroll.Offset = new Vector(_scroll.Offset.X, offset);
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Hook();
        var point = e.GetPosition(this);
        _dragOffset = ViewportRect.Contains(point) ? point.Y - ViewportRect.Y : ViewportRect.Height / 2;
        ScrollTo(point.Y, _dragOffset.Value);
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragOffset is { } offset) { ScrollTo(e.GetPosition(this).Y, offset); e.Handled = true; }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragOffset = null;
        e.Pointer.Capture(null);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); _dragOffset = null; }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_scroll is null) return;
        ScrollTo((_scroll.Offset.Y - e.Delta.Y * 48) * Scale, 0);
        e.Handled = true;
    }
}