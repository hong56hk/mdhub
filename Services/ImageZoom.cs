namespace Mdhub.Services;

// Item 37: zoom math for the image viewer (UI-free so it is testable headlessly). Scale 1 is 100% (one image pixel
// per device-independent pixel); Offset is the image's top-left corner in viewer coordinates.
public static class ImageZoom
{
    public const double Min = 0.1;
    public const double Max = 10;
    public const double Step = 1.1;

    public static double Clamp(double scale) => Math.Clamp(double.IsFinite(scale) ? scale : 1, Min, Max);
    // One wheel notch multiplies or divides the scale by Step; trackpads send fractional deltas.
    public static double Wheel(double scale, double delta) => Clamp(scale * Math.Pow(Step, delta));
    public static int Percent(double scale) => (int)Math.Round(scale * 100);

    // The scale that fits the image inside the viewport (never above 100%, like the Uniform/Down stretch it replaces).
    public static double Fit(double imageWidth, double imageHeight, double viewWidth, double viewHeight) =>
        imageWidth <= 0 || imageHeight <= 0 || viewWidth <= 0 || viewHeight <= 0 ? 1
            : Clamp(Math.Min(1, Math.Min(viewWidth / imageWidth, viewHeight / imageHeight)));

    // Offset that centres the scaled image in the viewport.
    public static (double X, double Y) Center(double imageWidth, double imageHeight, double scale, double viewWidth, double viewHeight) =>
        ((viewWidth - imageWidth * scale) / 2, (viewHeight - imageHeight * scale) / 2);

    // New offset after changing the scale so the image point under (pointX, pointY) stays under it.
    public static (double X, double Y) ZoomAt(double offsetX, double offsetY, double oldScale, double newScale, double pointX, double pointY)
    {
        var ratio = newScale / oldScale;
        return (pointX - (pointX - offsetX) * ratio, pointY - (pointY - offsetY) * ratio);
    }
}
