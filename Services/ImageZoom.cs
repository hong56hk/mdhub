namespace Mdhub.Services;

// Item 37: zoom math for the image viewer (UI-free so it is testable headlessly). Scale 1 is 100% (one image pixel
// per device-independent pixel); Offset is the image's top-left corner in viewer coordinates.
// Item 91: 10%-3200%, zoom steps, and the rotate/flip view math (rotation in degrees, clockwise on screen, applied
// after the flip, both around the image centre).
public static class ImageZoom
{
    public const double Min = 0.1;
    public const double Max = 32;
    public const double Step = 1.1;
    public const double RotateSnap = 15;

    // Ctrl+= / Ctrl+- step through these scales (Photoshop-like presets).
    public static readonly IReadOnlyList<double> Steps =
        [0.1, 0.125, 0.1667, 0.25, 1 / 3.0, 0.5, 2 / 3.0, 1, 1.5, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 24, 32];

    public static double Clamp(double scale) => Math.Clamp(double.IsFinite(scale) ? scale : 1, Min, Max);
    // One wheel notch multiplies or divides the scale by Step; trackpads send fractional deltas.
    public static double Wheel(double scale, double delta) => Clamp(scale * Math.Pow(Step, delta));
    public static int Percent(double scale) => (int)Math.Round(scale * 100);

    // The next preset above (direction > 0) or below the scale; stays at the limit.
    public static double StepZoom(double scale, int direction)
    {
        const double eps = 1e-6;
        return direction > 0
            ? Steps.FirstOrDefault(s => s > scale * (1 + eps), Max)
            : Steps.LastOrDefault(s => s < scale * (1 - eps), Min);
    }

    // The scale that fits the image inside the viewport (never above 100%, like the Uniform/Down stretch it replaces).
    public static double Fit(double imageWidth, double imageHeight, double viewWidth, double viewHeight) =>
        imageWidth <= 0 || imageHeight <= 0 || viewWidth <= 0 || viewHeight <= 0 ? 1
            : Clamp(Math.Min(1, Math.Min(viewWidth / imageWidth, viewHeight / imageHeight)));

    // Fit for a rotated image: the rotated bounding box must fit.
    public static double Fit(double imageWidth, double imageHeight, double viewWidth, double viewHeight, double degrees)
    {
        var (w, h) = RotatedBounds(imageWidth, imageHeight, degrees);
        return Fit(w, h, viewWidth, viewHeight);
    }

    // Offset that centres the scaled image in the viewport.
    public static (double X, double Y) Center(double imageWidth, double imageHeight, double scale, double viewWidth, double viewHeight) =>
        ((viewWidth - imageWidth * scale) / 2, (viewHeight - imageHeight * scale) / 2);

    // New offset after changing the scale so the image point under (pointX, pointY) stays under it.
    // (Rotation and flip are around the image centre, so this holds for a rotated or flipped image too.)
    public static (double X, double Y) ZoomAt(double offsetX, double offsetY, double oldScale, double newScale, double pointX, double pointY)
    {
        var ratio = newScale / oldScale;
        return (pointX - (pointX - offsetX) * ratio, pointY - (pointY - offsetY) * ratio);
    }

    public static double Normalize(double degrees) { var d = degrees % 360; return d < 0 ? d + 360 : d; }
    public static double Snap(double degrees, double step = RotateSnap) => Normalize(Math.Round(degrees / step) * step);

    // Screen rotation (y down, positive = clockwise).
    public static (double X, double Y) Rotate(double x, double y, double degrees)
    {
        var r = degrees * Math.PI / 180;
        double c = Math.Cos(r), s = Math.Sin(r);
        // Exact quarter turns so 90-degree steps do not drift.
        if (Math.Abs(c) < 1e-12) c = 0; if (Math.Abs(s) < 1e-12) s = 0;
        return (x * c - y * s, x * s + y * c);
    }

    public static (double W, double H) RotatedBounds(double width, double height, double degrees)
    {
        var (c, s) = Rotate(1, 0, degrees);
        return (Math.Abs(width * c) + Math.Abs(height * s), Math.Abs(width * s) + Math.Abs(height * c));
    }

    // New top-left offset after rotating the view by delta degrees around (pivotX, pivotY): the image centre orbits the pivot.
    public static (double X, double Y) RotateAbout(double offsetX, double offsetY, double imageWidth, double imageHeight, double scale, double pivotX, double pivotY, double delta)
    {
        double hw = imageWidth * scale / 2, hh = imageHeight * scale / 2;
        var (x, y) = Rotate(offsetX + hw - pivotX, offsetY + hh - pivotY, delta);
        return (pivotX + x - hw, pivotY + y - hh);
    }

    // The image point (in image units, 0..width/height) under a viewer point, undoing rotation then flip; null outside the image.
    public static (double X, double Y)? ToImage(double pointX, double pointY, double offsetX, double offsetY, double scale,
        double imageWidth, double imageHeight, double degrees, bool flipHorizontal, bool flipVertical)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || scale <= 0) return null;
        double hw = imageWidth * scale / 2, hh = imageHeight * scale / 2;
        var (x, y) = Rotate(pointX - offsetX - hw, pointY - offsetY - hh, -degrees);
        if (flipHorizontal) x = -x;
        if (flipVertical) y = -y;
        double u = x / scale + imageWidth / 2, v = y / scale + imageHeight / 2;
        return u < 0 || v < 0 || u >= imageWidth || v >= imageHeight ? null : (u, v);
    }

    // The same as a whole pixel of a bitmap pixelWidth x pixelHeight shown at imageWidth x imageHeight units.
    public static (int X, int Y)? ToPixel(double pointX, double pointY, double offsetX, double offsetY, double scale,
        double imageWidth, double imageHeight, int pixelWidth, int pixelHeight, double degrees, bool flipHorizontal, bool flipVertical) =>
        ToImage(pointX, pointY, offsetX, offsetY, scale, imageWidth, imageHeight, degrees, flipHorizontal, flipVertical) is { } p
            ? (Math.Min(pixelWidth - 1, (int)Math.Floor(p.X * pixelWidth / imageWidth)), Math.Min(pixelHeight - 1, (int)Math.Floor(p.Y * pixelHeight / imageHeight)))
            : null;
}
