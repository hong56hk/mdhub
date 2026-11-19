using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub.Services;

namespace Mdhub.Components;

// Reuse the editor's shaped, syntax-coloured text, including wrapped lines. Navigation changes
// only the scroll offset, leaving the caret, selection and undo history untouched.
// Item 120: snapshot the scaled text and debounce rebuilding it after edits so typing does not
// redraw the full minimap on every key. Scrolling still updates the viewport indicator immediately.
public sealed class EditorMinimap : Control
{
    internal const int ContentDebounceMilliseconds = 200;

    private TextBox? _editor;
    private TextPresenter? _presenter;
    private ScrollViewer? _scroll;
    private double? _dragOffset;
    private RenderTargetBitmap? _snapshot;
    private Size _snapshotSize;
    private bool _textDirty;
    private Timer? _flushTimer;

    public EditorMinimap()
    {
        Width = 100;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
        AttachedToVisualTree += (_, _) =>
        {
            _editor = this.GetVisualAncestors().OfType<TextBox>().FirstOrDefault();
            EditorBehavior.Changed += UpdateVisibility;
            ThemeService.Changed += OnThemeChanged;
            EditorFonts.Changed += RefreshNow;
            EditorColors.Changed += RefreshNow;
            UpdateVisibility();
            LayoutUpdated += OnLayout;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            EditorBehavior.Changed -= UpdateVisibility;
            ThemeService.Changed -= OnThemeChanged;
            EditorFonts.Changed -= RefreshNow;
            EditorColors.Changed -= RefreshNow;
            LayoutUpdated -= OnLayout;
            _flushTimer?.Dispose();
            _flushTimer = null;
            if (_textDirty) RebuildSnapshot();
            Unhook();
            _editor = null;
        };
    }

    // Test hooks: a full content rebuild (not a viewport-only paint).
    internal int ContentRedraws { get; private set; }
    internal string? SnapshotText { get; private set; }

    private void UpdateVisibility()
    {
        IsVisible = EditorBehavior.Current.Minimap;
        if (IsVisible && (_textDirty || _snapshot is null)) RefreshNow();
    }

    private void OnThemeChanged(AppTheme _) => RefreshNow();

    private void OnLayout(object? sender, EventArgs e)
    {
        Hook();
        // Start the idle delay after typetext layout finishes, not at the Text change
        // (an 80 KB key can take longer than the debounce if the timer starts too early).
        if (_textDirty) { ArmFlushTimer(); return; }
        if (_snapshotSize != Bounds.Size) RefreshNow();
    }

    private void OnScroll(object? sender, ScrollChangedEventArgs e) => InvalidateVisual();

    private void ScheduleContentRefresh()
    {
        _textDirty = true;
        _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void ArmFlushTimer()
    {
        if (!IsVisible) return;
        // A thread-pool timer: DispatcherTimer and UI-thread Task.Delay are flushed by RunJobs during typetext.
        _flushTimer ??= new Timer(_ => Dispatcher.UIThread.Post(FlushIfDue), null, Timeout.Infinite, Timeout.Infinite);
        _flushTimer.Change(ContentDebounceMilliseconds, Timeout.Infinite);
    }

    private void FlushIfDue()
    {
        if (!_textDirty) return;
        RebuildSnapshot();
        InvalidateVisual();
    }

    private void RefreshNow()
    {
        _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        RebuildSnapshot();
        InvalidateVisual();
    }

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty) { ScheduleContentRefresh(); return; }
        if (e.Property == TextBox.TextWrappingProperty || e.Property == TextBox.FontSizeProperty
            || e.Property == TextBox.FontFamilyProperty || e.Property == TextBox.FontWeightProperty
            || e.Property == TextBox.FontStyleProperty || e.Property == TextBox.FontStretchProperty
            || e.Property == TextBox.LineHeightProperty || e.Property == TextBox.LetterSpacingProperty)
            RefreshNow();
    }

    private void Hook()
    {
        if (_presenter is not null || _editor is null) return;
        _presenter = _editor.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
        _scroll = _editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_scroll is not null) _scroll.ScrollChanged += OnScroll;
        _editor.PropertyChanged += OnEditorPropertyChanged;
        if (_presenter is not null && _snapshot is null) RefreshNow();
    }

    private void Unhook()
    {
        if (_editor is not null) _editor.PropertyChanged -= OnEditorPropertyChanged;
        if (_scroll is not null) _scroll.ScrollChanged -= OnScroll;
        _presenter = null;
        _scroll = null;
        _dragOffset = null;
        _snapshot?.Dispose();
        _snapshot = null;
        _snapshotSize = default;
    }

    private double Scale => _scroll is null ? 0 : Math.Min(0.16, Bounds.Height / Math.Max(1, _scroll.Extent.Height));
    internal Rect ViewportRect => _scroll is null ? default : new Rect(0, _scroll.Offset.Y * Scale,
        Bounds.Width, Math.Min(Bounds.Height, _scroll.Viewport.Height * Scale));

    private void RebuildSnapshot()
    {
        Hook();
        ContentRedraws++;
        SnapshotText = _editor?.Text ?? _presenter?.Text;
        _textDirty = false;
        _snapshot?.Dispose();
        _snapshot = null;
        _snapshotSize = Bounds.Size;
        if (_presenter is null || _scroll is null || Scale <= 0 || Bounds.Width < 1 || Bounds.Height < 1) return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var width = Math.Max(1, (int)Math.Ceiling(Bounds.Width * scaling));
        var height = Math.Max(1, (int)Math.Ceiling(Bounds.Height * scaling));
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96 * scaling, 96 * scaling));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(ThemeService.Brush("EditorTextBackground"), new Rect(Bounds.Size));
            using (context.PushClip(new Rect(Bounds.Size)))
            using (context.PushTransform(Matrix.CreateScale(0.16, Scale)))
                _presenter.TextLayout.Draw(context, new Point(8, 0));
        }
        _snapshot = bitmap;
    }

    public override void Render(DrawingContext context)
    {
        Hook();
        context.FillRectangle(ThemeService.Brush("EditorTextBackground"), new Rect(Bounds.Size));
        if (_snapshot is not null)
            context.DrawImage(_snapshot, new Rect(Bounds.Size));
        if (_scroll is null || Scale <= 0) return;
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