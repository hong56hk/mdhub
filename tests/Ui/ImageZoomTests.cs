using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;

// Item 37: image viewer zoom math and the viewer's zoom-around-pointer behaviour, headless.
internal static class ImageZoomTests
{
    public static void Run()
    {
        Check(ImageZoom.Clamp(0.01) == ImageZoom.Min && ImageZoom.Clamp(50) == ImageZoom.Max && ImageZoom.Clamp(double.NaN) == 1, "scale clamps to 10%-1000%");
        Check(Math.Abs(ImageZoom.Wheel(1, 1) - 1.1) < 1e-9 && Math.Abs(ImageZoom.Wheel(1, -1) - 1 / 1.1) < 1e-9, "one wheel notch is a 10% step");
        var s = 1.0; for (var i = 0; i < 200; i++) s = ImageZoom.Wheel(s, 1);
        Check(s == ImageZoom.Max, "zooming in stops at 1000%");
        for (var i = 0; i < 200; i++) s = ImageZoom.Wheel(s, -1);
        Check(s == ImageZoom.Min, "zooming out stops at 10%");
        var (x, y) = ImageZoom.ZoomAt(10, 20, 1, 2, 110, 70);
        Check(x == -90 && y == -30, "zoom keeps the image point under the cursor fixed");
        // The image point under the cursor before and after must be the same.
        double ox = 37, oy = -12, old = 0.8, next = ImageZoom.Wheel(old, 3), px = 250, py = 140;
        var (nx, ny) = ImageZoom.ZoomAt(ox, oy, old, next, px, py);
        Check(Math.Abs((px - ox) / old - (px - nx) / next) < 1e-9 && Math.Abs((py - oy) / old - (py - ny) / next) < 1e-9, "image coordinate under the pointer is invariant");
        Check(ImageZoom.Fit(2000, 1000, 500, 500) == 0.25 && ImageZoom.Fit(100, 100, 500, 500) == 1, "fit shrinks large images but never enlarges small ones");
        Check(ImageZoom.Percent(1.234) == 123, "percent rounds");

        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var viewer = new ImageViewer();
        var window = new Window { Width = 440, Height = 440, Content = viewer };
        window.Show();
        var bitmap = new RenderTargetBitmap(new PixelSize(800, 400));
        var reported = new List<int>();
        viewer.ZoomChanged += reported.Add;
        viewer.ShowImage(bitmap); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Check(viewer.IsFit && viewer.ZoomPercent == 50, $"a new image fits the viewer (zoom={viewer.ZoomPercent})");
        var point = new Point(150, 200);
        var before = viewer.Offset; var beforeScale = viewer.Scale;
        Check(viewer.ZoomAt(2, point), "Ctrl+wheel zooms");
        var after = viewer.Offset;
        var local = point - new Point(20, 20); // the viewer's padding
        Check(Math.Abs((local.X - before.X) / beforeScale - (local.X - after.X) / viewer.Scale) < 1e-6, "viewer keeps the point under the cursor fixed");
        Check(viewer.ZoomPercent == 61 && reported.Last() == 61, $"zoom change is reported for the status bar ({viewer.ZoomPercent})");
        Check(viewer.ResetZoom() && viewer.ZoomPercent == 100 && !viewer.IsFit, "reset shows the image at 100%");
        viewer.ShowImage(new RenderTargetBitmap(new PixelSize(100, 100)));
        Check(viewer.IsFit && viewer.ZoomPercent == 100, "another image starts fitted again");
        window.Close();
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}