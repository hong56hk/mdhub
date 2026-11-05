using Mdhub.Localization;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Mdhub.Services;

namespace Mdhub.Components;

// Item 91: the viewer's pointer tools (Photoshop-style: H hand, Z zoom, R rotate view; Space holds the hand).
public enum ImageTool { Hand, Zoom, Rotate }

// Item 91: one image's view (zoom, pan, rotation, flip). View-only: nothing here touches the file or the document.
public sealed class ImageViewState
{
    public double? Scale; // null = fit to the viewer
    public Point Offset;
    public double Rotation;
    public bool FlipHorizontal, FlipVertical;
}

public partial class ImageViewer : UserControl
{
    // Kept per image (each tab has its own bitmap) for the session, so switching tabs or panes restores the view.
    private static readonly ConditionalWeakTable<Bitmap, ImageViewState> States = new();
    private ImageViewState _state = new();
    private ImageTool _tool = ImageTool.Hand;
    private bool _spaceHand;
    private enum Drag { None, Pan, Rotate }
    private Drag _drag;
    private Point _dragStart;
    private Point _dragOffset;
    private double _dragRotation, _dragAngle;
    public event Action<int>? ZoomChanged;
    public event Action<PixelPoint?>? CursorPixelChanged;
    public const double WheelPan = 50;

    public ImageViewer()
    {
        InitializeComponent();
        Surface.SizeChanged += (_, _) => Arrange();
        Surface.PointerPressed += OnSurfacePressed;
        Surface.PointerMoved += OnSurfaceMoved;
        Surface.PointerReleased += OnSurfaceReleased;
        Surface.PointerCaptureLost += (_, _) => EndDrag();
        Surface.PointerExited += (_, _) => SetCursorPixel(null);
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Bubble);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Bubble);
        LostFocus += (_, _) => { if (!IsKeyboardFocusWithin) SetSpaceHand(false); };
        HandButton.Click += (_, _) => Tool = ImageTool.Hand;
        ZoomToolButton.Click += (_, _) => Tool = ImageTool.Zoom;
        ZoomOutButton.Click += (_, _) => StepZoom(-1);
        AttachedToVisualTree += (_, _) => { Loc.Changed += UpdateZoomOutTip; Shortcuts.Changed += UpdateZoomOutTip; UpdateZoomOutTip(); };
        DetachedFromVisualTree += (_, _) => { Loc.Changed -= UpdateZoomOutTip; Shortcuts.Changed -= UpdateZoomOutTip; };
        UpdateZoomOutTip();
        UpdateZoomOut();
        RotateToolButton.Click += (_, _) => Tool = ImageTool.Rotate;
        RotateLeftButton.Click += (_, _) => Rotate90(-1);
        RotateRightButton.Click += (_, _) => Rotate90(1);
        FlipHorizontalButton.Click += (_, _) => Flip(true);
        FlipVerticalButton.Click += (_, _) => Flip(false);
        FitButton.Click += (_, _) => FitToWindow();
        ActualSizeButton.Click += (_, _) => ActualSize();
        MenuRotateLeft.Click += (_, _) => Rotate90(-1);
        MenuRotateRight.Click += (_, _) => Rotate90(1);
        MenuFlipHorizontal.Click += (_, _) => Flip(true);
        MenuFlipVertical.Click += (_, _) => Flip(false);
        MenuFit.Click += (_, _) => FitToWindow();
        MenuActualSize.Click += (_, _) => ActualSize();
        MenuResetView.Click += (_, _) => ResetView();
        UpdateTool();
    }

    public void ShowImage(Bitmap? image)
    {
        if (!ReferenceEquals(Picture.Source, image))
        {
            _state = image is null ? new() : States.GetValue(image, _ => new());
            EndDrag();
        }
        Picture.Source = image;
        Arrange();
    }

    private Size ImageSize => Picture.Source is Bitmap b ? b.Size : default;
    private Size View => Surface.Bounds.Size;
    private Point ViewCenter => new(View.Width / 2, View.Height / 2);
    public ImageViewState State => _state;
    public bool IsFit => _state.Scale is null;
    public double Scale => _state.Scale ?? ImageZoom.Fit(ImageSize.Width, ImageSize.Height, View.Width, View.Height, _state.Rotation);
    public int ZoomPercent => ImageZoom.Percent(Scale);
    public Point Offset => _state.Scale is null ? CenterOffset(Scale) : _state.Offset;
    public double Rotation => _state.Rotation;
    public bool FlipHorizontal => _state.FlipHorizontal;
    public bool FlipVertical => _state.FlipVertical;
    public PixelPoint? CursorPixel { get; private set; }

    public ImageTool Tool
    {
        get => _tool;
        set { _tool = value; UpdateTool(); }
    }
    // The tool the pointer uses now: Space held is a temporary hand.
    public ImageTool EffectiveTool => _spaceHand ? ImageTool.Hand : _tool;

    private Point CenterOffset(double scale)
    {
        var (x, y) = ImageZoom.Center(ImageSize.Width, ImageSize.Height, scale, View.Width, View.Height);
        return new(x, y);
    }

    // Leaves fit mode keeping what is on screen (before a pan, zoom or free rotation).
    private void Pin()
    {
        if (_state.Scale is not null) return;
        var scale = Scale;
        _state.Offset = CenterOffset(scale);
        _state.Scale = scale;
    }

    // Zooms to a scale keeping the given Surface point fixed.
    private bool ZoomTo(double next, Point local)
    {
        if (Picture.Source is null) return false;
        var old = Scale;
        next = ImageZoom.Clamp(next);
        var origin = Offset;
        var (x, y) = ImageZoom.ZoomAt(origin.X, origin.Y, old, next, local.X, local.Y);
        _state.Scale = next;
        _state.Offset = new(x, y);
        Arrange();
        return true;
    }

    private Point ToSurface(Point point) => this.TranslatePoint(point, Surface) ?? point;

    // Ctrl+wheel / Alt+wheel at a point in this control's coordinates.
    public bool ZoomAt(double delta, Point point) =>
        Picture.Source is not null && delta != 0 && ZoomTo(ImageZoom.Wheel(Scale, delta), ToSurface(point));

    public void ZoomAt(double delta, PointerEventArgs e) => ZoomAt(delta, e.GetPosition(this));

    // Ctrl+= / Ctrl+- (and the Zoom tool's click): the next preset, anchored at a point (the view centre by default).
    public bool StepZoom(int direction, Point? point = null) =>
        Picture.Source is not null && ZoomTo(ImageZoom.StepZoom(Scale, direction), point is { } p ? ToSurface(p) : ViewCenter);

    // Ctrl+0: fit to the window (rotation and flip stay).
    public bool FitToWindow()
    {
        if (Picture.Source is null) return false;
        _state.Scale = null;
        Arrange();
        return true;
    }

    // Ctrl+1: actual size (100%), centred.
    public bool ActualSize()
    {
        if (Picture.Source is null) return false;
        _state.Scale = 1;
        _state.Offset = CenterOffset(1);
        Arrange();
        return true;
    }

    // Esc / double-click: fit, no pan, no rotation, no flip.
    public bool ResetView()
    {
        if (Picture.Source is null) return false;
        _state.Scale = null;
        _state.Rotation = 0;
        _state.FlipHorizontal = _state.FlipVertical = false;
        Arrange();
        return true;
    }

    public bool Pan(double dx, double dy)
    {
        if (Picture.Source is null || dx == 0 && dy == 0) return false;
        Pin();
        _state.Offset += new Point(dx, dy);
        Arrange();
        return true;
    }

    // Sets the view rotation around the view centre (fit mode stays fitted to the rotated image).
    public bool SetRotation(double degrees)
    {
        if (Picture.Source is null) return false;
        degrees = ImageZoom.Normalize(degrees);
        if (_state.Scale is not null)
        {
            var c = ViewCenter;
            var (x, y) = ImageZoom.RotateAbout(_state.Offset.X, _state.Offset.Y, ImageSize.Width, ImageSize.Height, Scale, c.X, c.Y, degrees - _state.Rotation);
            _state.Offset = new(x, y);
        }
        _state.Rotation = degrees;
        Arrange();
        return true;
    }

    // Toolbar: a quarter turn left (-1) or right (+1), snapped to whole quarter turns.
    public bool Rotate90(int direction) => SetRotation(Math.Round((_state.Rotation + 90 * Math.Sign(direction)) / 90) * 90);

    public bool Flip(bool horizontal)
    {
        if (Picture.Source is null) return false;
        if (horizontal) _state.FlipHorizontal = !_state.FlipHorizontal; else _state.FlipVertical = !_state.FlipVertical;
        Arrange();
        return true;
    }

    // The image pixel under a point in this control's coordinates (null outside the image).
    public PixelPoint? PixelAt(Point point)
    {
        if (Picture.Source is not Bitmap b) return null;
        var p = ToSurface(point);
        var o = Offset;
        return ImageZoom.ToPixel(p.X, p.Y, o.X, o.Y, Scale, b.Size.Width, b.Size.Height, b.PixelSize.Width, b.PixelSize.Height,
            _state.Rotation, _state.FlipHorizontal, _state.FlipVertical) is { } px ? new PixelPoint(px.X, px.Y) : null;
    }

    // The image point (image units, not rounded) under a point in this control's coordinates.
    public Point? ImagePointAt(Point point)
    {
        if (Picture.Source is not Bitmap b) return null;
        var p = ToSurface(point);
        var o = Offset;
        return ImageZoom.ToImage(p.X, p.Y, o.X, o.Y, Scale, b.Size.Width, b.Size.Height, _state.Rotation, _state.FlipHorizontal, _state.FlipVertical) is { } q ? new Point(q.X, q.Y) : null;
    }

    private void SetCursorPixel(PixelPoint? pixel)
    {
        if (CursorPixel == pixel) return;
        CursorPixel = pixel;
        CursorPixelChanged?.Invoke(pixel);
    }

    // ---- pointer
    private void OnSurfacePressed(object? sender, PointerPressedEventArgs e)
    {
        Focus();
        var point = e.GetCurrentPoint(Surface);
        if (!point.Properties.IsLeftButtonPressed || Picture.Source is null) return;
        e.Handled = true;
        // Double-click resets the view; with the zoom tool every click zooms, so quick clicks keep zooming.
        if (e.ClickCount >= 2 && EffectiveTool != ImageTool.Zoom) { EndDrag(); ResetView(); return; }
        switch (EffectiveTool)
        {
            case ImageTool.Hand:
                Pin();
                BeginDrag(Drag.Pan, point.Position, e);
                break;
            case ImageTool.Zoom:
                StepZoom(e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? -1 : 1, e.GetPosition(this));
                break;
            case ImageTool.Rotate:
                Pin();
                _dragRotation = _state.Rotation;
                _dragAngle = AngleAt(point.Position);
                BeginDrag(Drag.Rotate, point.Position, e);
                break;
        }
    }

    private void BeginDrag(Drag drag, Point start, PointerPressedEventArgs e)
    {
        _drag = drag;
        _dragStart = start;
        _dragOffset = _state.Offset;
        e.Pointer.Capture(Surface);
        UpdateCursor();
    }

    private double AngleAt(Point p) { var c = ViewCenter; return Math.Atan2(p.Y - c.Y, p.X - c.X) * 180 / Math.PI; }

    private void OnSurfaceMoved(object? sender, PointerEventArgs e)
    {
        var p = e.GetPosition(Surface);
        if (_drag == Drag.Pan) { _state.Offset = _dragOffset + (p - _dragStart); Arrange(); }
        else if (_drag == Drag.Rotate) RotateDrag(AngleAt(p), e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        SetCursorPixel(PixelAt(e.GetPosition(this)));
    }

    // Free rotate: the drag's angle around the view centre; Shift snaps the result to 15 degrees.
    public void RotateDrag(double pointerAngle, bool snap)
    {
        var target = _dragRotation + pointerAngle - _dragAngle;
        SetRotation(snap ? ImageZoom.Snap(target) : target);
    }
    public void BeginRotateDrag(double pointerAngle) { Pin(); _dragRotation = _state.Rotation; _dragAngle = pointerAngle; }

    private void OnSurfaceReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_drag == Drag.None) return;
        e.Pointer.Capture(null);
        EndDrag();
    }

    private void EndDrag() { _drag = Drag.None; UpdateCursor(); }

    // Wheel scrolls (Shift: sideways); Alt+wheel zooms at the pointer (Ctrl/Cmd+wheel arrives via the window, item 37).
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (Picture.Source is null || e.Handled) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) { e.Handled = ZoomAt(e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X, e.GetPosition(this)); return; }
        double dx = e.Delta.X, dy = e.Delta.Y;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && dx == 0) (dx, dy) = (dy, 0);
        e.Handled = Pan(dx * WheelPan, dy * WheelPan);
    }

    // ---- keys (only while the viewer has focus, so H/Z/R/Space/Esc never reach it from the editor)
    private static bool IsTempHandKey(Key key) => Shortcuts.Get("image.tempHand").Gestures.Any(g => g.Key == key.ToString());

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || Picture.Source is null) return;
        var id = ShortcutKeys.Match(e, s => s.ImageOnly);
        switch (id)
        {
            case "image.handTool": Tool = ImageTool.Hand; break;
            case "image.zoomTool": Tool = ImageTool.Zoom; break;
            case "image.rotateTool": Tool = ImageTool.Rotate; break;
            case "image.tempHand": SetSpaceHand(true); break;
            case "image.resetView": ResetView(); break;
            case "image.actualSize": ActualSize(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (_spaceHand && IsTempHandKey(e.Key)) { SetSpaceHand(false); e.Handled = true; }
    }

    private void SetSpaceHand(bool on)
    {
        if (_spaceHand == on) return;
        _spaceHand = on;
        UpdateTool();
    }

    private void UpdateTool()
    {
        HandButton.Classes.Set("on", EffectiveTool == ImageTool.Hand);
        ZoomToolButton.Classes.Set("on", EffectiveTool == ImageTool.Zoom);
        RotateToolButton.Classes.Set("on", EffectiveTool == ImageTool.Rotate);
        UpdateCursor();
    }

    // Avalonia has no grab/grabbing cursors: an open hand while the hand tool is ready, the move cursor while dragging.
    private void UpdateCursor() => Surface.Cursor = new Cursor(CursorType);
    public StandardCursorType CursorType => _drag switch
    {
        Drag.Pan => StandardCursorType.SizeAll,
        Drag.Rotate => StandardCursorType.Cross,
        _ => EffectiveTool switch { ImageTool.Hand => StandardCursorType.Hand, _ => StandardCursorType.Cross }
    };

    private void Arrange()
    {
        var scale = Scale;
        var offset = Offset;
        Picture.Width = ImageSize.Width * scale;
        Picture.Height = ImageSize.Height * scale;
        Canvas.SetLeft(Picture, offset.X);
        Canvas.SetTop(Picture, offset.Y);
        var flip = new ScaleTransform(_state.FlipHorizontal ? -1 : 1, _state.FlipVertical ? -1 : 1);
        Picture.RenderTransform = _state.Rotation == 0 && !_state.FlipHorizontal && !_state.FlipVertical ? null
            : new TransformGroup { Children = { flip, new RotateTransform(_state.Rotation) } };
        UpdateZoomOut();
        ZoomChanged?.Invoke(ZoomPercent);
    }

    // Item 96: disabled at the 10% floor (and with no image).
    private void UpdateZoomOut() => ZoomOutButton.IsEnabled = Picture.Source is not null && ImageZoom.StepZoom(Scale, -1) < Scale - 1e-9;
    private void UpdateZoomOutTip() =>
        ToolTip.SetTip(ZoomOutButton, Loc.F("ImageZoomOutTip", Shortcuts.Format("zoom.out", ShortcutKeys.IsMac).Split(" / ")[0]));
}
