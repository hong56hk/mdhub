using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Mdhub.Services;

namespace Mdhub.Components;

public partial class ImageViewer : UserControl
{
    // null = fit to the viewer (the default for a newly shown image); otherwise the user's zoom.
    private double? _scale;
    private Point _offset;
    public event Action<int>? ZoomChanged;

    public ImageViewer()
    {
        InitializeComponent();
        Surface.SizeChanged += (_, _) => Arrange();
    }

    public void ShowImage(Bitmap? image)
    {
        if (!ReferenceEquals(Picture.Source, image)) _scale = null;
        Picture.Source = image;
        Arrange();
    }

    private Size ImageSize => Picture.Source is Bitmap b ? b.Size : default;
    private Size View => Surface.Bounds.Size;
    public bool IsFit => _scale is null;
    public double Scale => _scale ?? ImageZoom.Fit(ImageSize.Width, ImageSize.Height, View.Width, View.Height);
    public int ZoomPercent => ImageZoom.Percent(Scale);
    public Point Offset => _scale is null ? CenterOffset(Scale) : _offset;

    private Point CenterOffset(double scale)
    {
        var (x, y) = ImageZoom.Center(ImageSize.Width, ImageSize.Height, scale, View.Width, View.Height);
        return new(x, y);
    }

    // Ctrl+wheel at a point in this control's coordinates.
    public bool ZoomAt(double delta, Point point)
    {
        if (Picture.Source is null || delta == 0) return false;
        var old = Scale;
        var next = ImageZoom.Wheel(old, delta);
        var origin = Offset;
        var local = this.TranslatePoint(point, Surface) ?? point;
        var (x, y) = ImageZoom.ZoomAt(origin.X, origin.Y, old, next, local.X, local.Y);
        _scale = next;
        _offset = new(x, y);
        Arrange();
        return true;
    }

    public void ZoomAt(double delta, PointerEventArgs e) => ZoomAt(delta, e.GetPosition(this));

    // Ctrl+0: actual size (100%), centred.
    public bool ResetZoom()
    {
        if (Picture.Source is null) return false;
        _scale = 1;
        _offset = CenterOffset(1);
        Arrange();
        return true;
    }

    private void Arrange()
    {
        var scale = Scale;
        var offset = Offset;
        Picture.Width = ImageSize.Width * scale;
        Picture.Height = ImageSize.Height * scale;
        Canvas.SetLeft(Picture, offset.X);
        Canvas.SetTop(Picture, offset.Y);
        ZoomChanged?.Invoke(ZoomPercent);
    }
}
