using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Services;
using Mdhub.Views;

// Item 104: PDF toolbar — Hand (default), Selection, page field, zoom in/out, reserved Notes slot, Open with default app.
internal static class PdfToolbarTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        void Press(Window w, Key k) { w.KeyPress(k, RawInputModifiers.None, PhysicalKey.None, null); w.KeyRelease(k, RawInputModifiers.None, PhysicalKey.None, null); Pump(); }

        var root = Path.Combine(Path.GetTempPath(), "mdhub-pdftoolbar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var good = Path.Combine(root, "report.pdf");
        File.WriteAllBytes(good, PdfFixtures.MultiPage("Page one: hello mdhub", "Page two: more text", "Page three: last page"));
        var launched = new List<string>();
        DefaultApp.LauncherOverride = launched.Add;
        var previousTheme = ThemeService.Current;
        var w = new MainWindow { Width = 900, Height = 520 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Task Invoke(string m, params object?[] args) => (Task)typeof(MainView).GetMethod(m, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(v, args)!;
            PdfViewer Viewer() => w.GetVisualDescendants().OfType<PdfViewer>().First(x => x.IsVisible);
            T Named<T>(PdfViewer viewer, string name) where T : Control => viewer.FindControl<T>(name) ?? throw new Exception("missing " + name);
            var status = w.GetVisualDescendants().OfType<BottomStatusBar>().Single();

            var idle = w.GetVisualDescendants().OfType<PdfViewer>().First();
            Check(!Named<Button>(idle, "HandButton").IsEnabled && !Named<Button>(idle, "SelectButton").IsEnabled
                && !Named<TextBox>(idle, "PageInput").IsEnabled && !Named<Button>(idle, "ZoomInButton").IsEnabled
                && !Named<Button>(idle, "ZoomOutButton").IsEnabled && !Named<Button>(idle, "OpenButton").IsEnabled,
                "every tool is disabled when no document is open");

            Wait(Invoke("OpenPathAsync", good));
            Pump(); Pump();
            var viewer = Viewer();
            var hand = Named<Button>(viewer, "HandButton");
            var select = Named<Button>(viewer, "SelectButton");
            var pageInput = Named<TextBox>(viewer, "PageInput");
            var pageCount = Named<TextBlock>(viewer, "PageCountLabel");
            var zoomIn = Named<Button>(viewer, "ZoomInButton");
            var zoomOut = Named<Button>(viewer, "ZoomOutButton");
            var open = Named<Button>(viewer, "OpenButton");
            var toolbar = Named<Border>(viewer, "Toolbar");
            var scroller = Named<ScrollViewer>(viewer, "Scroller");
            Check(viewer.Tool == PdfTool.Hand, "Hand is the default tool");
            var named = toolbar.GetVisualDescendants().OfType<Control>().Where(c => c.Name is "HandButton" or "SelectButton" or "PageInput" or "ZoomInButton" or "ZoomOutButton" or "NotesSlot" or "OpenButton").ToList();
            Check(string.Join(",", named.Select(c => c.Name)) == "HandButton,SelectButton,PageInput,ZoomInButton,ZoomOutButton,NotesSlot,OpenButton",
                $"tools are in Hong's order, Notes slot before Open, Open last ({string.Join(",", named.Select(c => c.Name))})");
            Check(open == named.Last() && Equals(ToolTip.GetTip(open), Loc.T("OpenWithDefaultApp")),
                "Open with default app is the last toolbar button and has a localized tooltip");
            Check(hand.IsEnabled && select.IsEnabled && pageInput.IsEnabled && open.IsEnabled,
                "tools are enabled when a PDF is open");

            ThemeService.Apply(AppTheme.Dark, persist: false); Pump();
            var pages = Named<StackPanel>(viewer, "Pages");
            Check(pages.Children.Count > 0, "the open PDF has at least one page host");
            var pageHost = (Border)pages.Children[0];
            var paper = (pageHost.Background as ISolidColorBrush)?.Color;
            Check(paper == Colors.White || paper == Color.FromRgb(255, 255, 255),
                $"the PDF page background is white in the dark theme ({paper})");
            Check(toolbar.Background is ISolidColorBrush tb && tb.Color != Colors.White,
                "the toolbar still follows the dark theme");

            for (var i = 0; i < 20 && viewer.ZoomPercent != 100; i++) { if (viewer.ZoomPercent > 100) viewer.StepZoom(-1); else viewer.StepZoom(1); }
            Pump(); Pump();
            viewer.Tool = PdfTool.Hand; Pump();
            var image = viewer.GetVisualDescendants().OfType<Image>().First(img => img.IsVisible && img.Source is not null);
            var mid = image.TranslatePoint(new Point(image.Bounds.Width / 2, 200), w)!.Value;
            var before = scroller.Offset.Y;
            w.MouseDown(mid, MouseButton.Left); Pump();
            w.MouseMove(mid + new Point(0, -80)); Pump();
            w.MouseUp(mid + new Point(0, -80), MouseButton.Left); Pump();
            Check(!viewer.HasSelection && scroller.Offset.Y != before,
                $"the Hand tool pans the pages and does not select text (offset {before} -> {scroller.Offset.Y})");

            viewer.Tool = PdfTool.Select; Pump();
            var glyphs = viewer.Document!.Characters(0);
            var word = new string(glyphs.Select(g => g.Char).ToArray());
            var helloAt = word.IndexOf("hello", StringComparison.Ordinal);
            Check(helloAt >= 0, "hello is on page 1");
            Point OnGlyph(int i, double xFrac)
            {
                var c = glyphs[i];
                var height = viewer.Document!.Pages[0].Height;
                var left = Math.Min(c.Left, c.Right);
                var right = Math.Max(c.Left, c.Right);
                double top = c.Top, bottom = c.Bottom;
                if (top > bottom) { top = height - c.Top; bottom = height - c.Bottom; if (top > bottom) (top, bottom) = (bottom, top); }
                var sx = image.Bounds.Width / viewer.Document.Pages[0].Width;
                var sy = image.Bounds.Height / height;
                return image.TranslatePoint(new Point((left + (right - left) * xFrac) * sx, (top + (bottom - top) * 0.5) * sy), w)!.Value;
            }
            w.MouseDown(OnGlyph(helloAt, 0.2), MouseButton.Left); Pump();
            w.MouseMove(OnGlyph(helloAt + 4, 0.8)); Pump();
            w.MouseUp(OnGlyph(helloAt + 4, 0.8), MouseButton.Left); Pump();
            Check(viewer.HasSelection && viewer.SelectedText.Contains("hello"), $"the Selection tool drag-selects text ({viewer.SelectedText})");

            pageInput.Text = "3";
            pageInput.Focus(); Pump();
            Press(w, Key.Enter); Pump();
            Check(viewer.CurrentPage == 3 && pageInput.Text == "3" && pageCount.Text == "3",
                $"Enter in the page field jumps to that page ({viewer.CurrentPage}, {pageInput.Text})");
            pageInput.Text = "99";
            pageInput.Focus(); Pump();
            Press(w, Key.Enter); Pump();
            Check(viewer.CurrentPage == 3 && pageInput.Text == "3", "an out-of-range page is clamped to the last page");
            pageInput.Text = "abc";
            pageInput.Focus(); Pump();
            Press(w, Key.Enter); Pump();
            Check(viewer.CurrentPage == 3 && pageInput.Text == "3", "invalid page input reverts to the current page");
            viewer.Focus(); Pump();
            viewer.ScrollToPage(1); Pump(); Pump();
            Check(viewer.CurrentPage == 1 && pageInput.Text == "1", "the page field updates when the view scrolls");

            var startZoom = viewer.ZoomPercent;
            Check(zoomIn.IsEnabled && zoomOut.IsEnabled, "zoom buttons are enabled between the limits");
            zoomIn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(viewer.ZoomPercent == ImageZoom.Percent(ImageZoom.StepZoom(startZoom / 100.0, 1)),
                $"Zoom In steps once ({startZoom} -> {viewer.ZoomPercent})");
            for (var i = 0; i < 40 && zoomOut.IsEnabled; i++) { viewer.StepZoom(-1); Pump(); }
            Check(viewer.ZoomPercent == ImageZoom.Percent(ImageZoom.Min) && !zoomOut.IsEnabled && zoomIn.IsEnabled,
                $"Zoom Out is disabled at the minimum ({viewer.ZoomPercent})");
            for (var i = 0; i < 40 && zoomIn.IsEnabled; i++) { viewer.StepZoom(1); Pump(); }
            Check(viewer.ZoomPercent == ImageZoom.Percent(ImageZoom.Max) && !zoomIn.IsEnabled && zoomOut.IsEnabled,
                $"Zoom In is disabled at the maximum ({viewer.ZoomPercent})");

            launched.Clear();
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Pump();
            Check(launched.SequenceEqual([good]), $"Open with default app calls the launcher with the PDF path ({string.Join(",", launched)})");

            DefaultApp.LauncherOverride = _ => throw new IOException("no app");
            open.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Pump();
            var expected = Loc.F("UnableToOpenDefaultApp", "no app");
            var statusText = status.FindControl<TextBlock>("Status")?.Text;
            Check(statusText == expected, $"a failed launch shows the localized error ({statusText})");
        }
        finally
        {
            DefaultApp.LauncherOverride = null;
            ThemeService.Apply(previousTheme, persist: false);
            try { w.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"PDF toolbar tests passed: {passed}");
    }
}