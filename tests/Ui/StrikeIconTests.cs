using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;

// Item 36: the format bar's Strikethrough icon draws its line through the middle of the "S".
internal static class StrikeIconTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        var w = new MainWindow { Width = 1100, Height = 700 };
        try
        {
            w.Show(); Pump();
            var strike = w.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "strike"));
            var bold = w.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Tag, "bold"));
            var glyph = strike.GetVisualDescendants().OfType<StrikeGlyph>().Single();
            Check(strike.Content is StrikeGlyph && !(glyph.Text ?? "").Any(c => c > 0x7f), "The icon is a plain S with a drawn line (no combining stroke)");
            Check(strike.Classes.Contains("tool") && glyph.FontSize == bold.FontSize && glyph.FontFamily == bold.FontFamily, "Same tool style and font as the other format icons");

            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme, persist: false); Pump(); Pump();
                var boldText = bold.GetVisualDescendants().OfType<TextBlock>().First();
                Check(glyph.Foreground is ISolidColorBrush g && boldText.Foreground is ISolidColorBrush b && g.Color == b.Color, $"{theme}: same colour as the Bold icon");
                for (var zoom = ZoomLevel.Min; zoom <= ZoomLevel.Max; zoom += ZoomLevel.Step)
                {
                    w.SetZoom(zoom, announce: false); Pump(); Pump();
                    var frame = w.CaptureRenderedFrame();
                    Pump();
                    var ink = glyph.GlyphBounds;
                    var y = glyph.LineStart.Y;
                    var ok = ink.Height > 0
                        && Math.Abs(y - ink.Center.Y) <= Math.Max(0.5, ink.Height * 0.08)
                        && glyph.LineStart.X < ink.Left && glyph.LineEnd.X > ink.Right
                        && ink.Left - glyph.LineStart.X <= ink.Width * 0.5 && glyph.LineEnd.X - ink.Right <= ink.Width * 0.5
                        && Math.Abs((ink.Left - glyph.LineStart.X) - (glyph.LineEnd.X - ink.Right)) < 0.01
                        && y < ink.Bottom - ink.Height * 0.3;
                    // Pixels: the overhang just past each side of the letter is painted in the icon colour; above it is not.
                    if (ok && frame is not null && zoom >= 100)
                    {
                        var fg = ((ISolidColorBrush)glyph.Foreground!).Color;
                        var px = Pixels(frame);
                        Point P(double x, double yy) => glyph.TranslatePoint(new Point(x, yy), w) is { } p ? p * frame.Dpi.X / 96 : default;
                        Color At(Point p) => px.TryGetValue(((int)Math.Floor(p.X), (int)Math.Floor(p.Y)), out var c) ? c : default;
                        var bgc = At(P(glyph.LineStart.X, ink.Top - 2));
                        // Painted: much closer to the icon colour than the button background is (anti-aliasing allowed).
                        bool Ink(Point p) => Dist(At(p), bgc) > Dist(fg, bgc) * 0.35;
                        var left = P(glyph.LineStart.X + (ink.Left - glyph.LineStart.X) * 0.5, y);
                        var right = P(glyph.LineEnd.X - (glyph.LineEnd.X - ink.Right) * 0.5, y);
                        var above = P(glyph.LineStart.X + (ink.Left - glyph.LineStart.X) * 0.5, y - Math.Max(2.5, glyph.LineThickness * 2));
                        ok = Ink(left) && Ink(right) && !Ink(above);
                    }
                    Check(ok, $"{theme} {zoom}%: the line crosses the middle of the S, centred, slightly past both sides");
                }
            }
            w.SetZoom(ZoomLevel.Default, announce: false);
        }
        finally
        {
            w.Close(); Pump();
            ThemeService.Apply(AppTheme.Dark, persist: false);
        }
        Console.WriteLine($"Strike icon tests: {passed} passed");
    }

    private static int Dist(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private static PixelReader Pixels(WriteableBitmap bmp)
    {
        using var fb = bmp.Lock();
        var bytes = new byte[fb.RowBytes * fb.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(fb.Address, bytes, 0, bytes.Length);
        return new PixelReader(bytes, fb.RowBytes, fb.Size.Width, fb.Size.Height, fb.Format == Avalonia.Platform.PixelFormat.Rgba8888);
    }

    private sealed class PixelReader(byte[] bytes, int stride, int width, int height, bool rgba)
    {
        public bool TryGetValue((int X, int Y) p, out Color c)
        {
            c = default;
            if (p.X < 0 || p.Y < 0 || p.X >= width || p.Y >= height) return false;
            var i = p.Y * stride + p.X * 4;
            c = rgba ? Color.FromArgb(bytes[i + 3], bytes[i], bytes[i + 1], bytes[i + 2]) : Color.FromArgb(bytes[i + 3], bytes[i + 2], bytes[i + 1], bytes[i]);
            return true;
        }
    }
}
