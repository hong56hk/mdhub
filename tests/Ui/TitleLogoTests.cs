using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;

// Item 69: the MDHub logo sits at the top-left of the title bar, left of File, and is part of the drag area.
internal static class TitleLogoTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        var startTheme = ThemeService.Current;
        var w = new MainWindow { Width = 1000, Height = 700 };
        try
        {
            w.Show(); Settle();
            var bar = w.GetVisualDescendants().OfType<TopMenu>().Single();
            var logo = bar.FindControl<Image>("TitleLogo")!;
            var file = bar.FindControl<Menu>("MainMenu")!.Items.OfType<MenuItem>().First();
            var drag = bar.FindControl<Border>("TitleBarDragRegion")!;
            void Layout(string tag, double zoom)
            {
                var l = logo.TranslatePoint(default, bar)!.Value; var f = file.TranslatePoint(default, bar)!.Value;
                var lb = new Rect(l, logo.Bounds.Size); var fb = new Rect(f, file.Bounds.Size);
                Check(logo.IsEffectivelyVisible && logo.Source is not null, $"{tag}: the logo is shown");
                Check(logo.Bounds.Width is >= 16 and <= 18 && logo.Bounds.Height is >= 16 and <= 18, $"{tag}: the logo is 16-18 px ({logo.Bounds.Size})");
                Check(lb.X >= 8 && lb.X <= 14 && lb.Right <= fb.X, $"{tag}: the logo is at the top-left, left of File ({lb.X:0.#}..{lb.Right:0.#}, File at {fb.X:0.#})");
                var gap = fb.X - lb.Right;
                Check(gap >= 4 && gap <= 12, $"{tag}: comfortable spacing before File ({gap:0.#} px)");
                Check(Math.Abs(lb.Center.Y - bar.Bounds.Height / 2) <= 1 && Math.Abs(lb.Center.Y - fb.Center.Y) <= 1.5, $"{tag}: the logo is vertically centred ({lb.Center.Y:0.#} in {bar.Bounds.Height:0.#}, File {fb.Center.Y:0.#})");
                var at = w.ZoomScale; Check(Math.Abs(at - zoom) < 1e-9, $"{tag}: window zoom is {zoom:0%}");
                Check(WindowDecorationProperties.GetElementRole(logo) == WindowDecorationsElementRole.TitleBar,
                    $"{tag}: the logo is part of the drag area");
                Check(WindowDecorationProperties.GetElementRole(drag) == WindowDecorationsElementRole.TitleBar && drag.Bounds.Width > 200, $"{tag}: the title bar drag region is intact ({drag.Bounds.Width:0} px)");
                var hit = w.InputHitTest(logo.TranslatePoint(new Point(logo.Bounds.Width / 2, logo.Bounds.Height / 2), w)!.Value) as Visual;
                Check(hit is null || hit == logo || hit.FindAncestorOfType<MenuItem>() is null && hit.FindAncestorOfType<Button>() is null, $"{tag}: nothing clickable sits over the logo ({hit?.GetType().Name})");
            }
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                ThemeService.Apply(theme, persist: false); Settle();
                Layout($"{theme} 100%", 1.0);
                w.SetZoom(150, announce: false); Settle();
                Layout($"{theme} 150%", 1.5);
                w.SetZoom(100, announce: false); Settle();
            }
        }
        finally { ThemeService.Apply(startTheme, persist: false); w.Close(); }
        Console.WriteLine($"Title logo tests: {passed} passed");
    }
}
