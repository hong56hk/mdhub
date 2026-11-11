using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;
using Mdhub.Views;

// Item 105: the PDF viewer must not enter an infinite layout loop. Two triggers are covered:
// Auto-scrollbar / fit-to-window oscillation on resize, and selecting text while the viewer
// is still in the default fit mode (Hong's crash). The image viewer's zoom-to-fit is on the
// same resize sweep.
internal static class PdfLayoutTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name)
        {
            if (!ok) throw new Exception(name);
            passed++;
            Console.WriteLine("PASS: " + name);
        }

        var root = Path.Combine(Path.GetTempPath(), "mdhub-pdflayout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var letter = Path.Combine(root, "letter.pdf");
            var landscape = Path.Combine(root, "wide.pdf");
            File.WriteAllBytes(letter, PdfFixtures.MultiPage("Page one", "Page two", "Page three"));
            File.WriteAllBytes(landscape, PdfFixtures.Pages(1200, 400, "wide"));

            using (var doc = PdfDocument.Open(letter))
            {
                var viewer = new PdfViewer();
                var w = new Window { Width = 500, Height = 900, Content = viewer };
                w.Show();
                viewer.Show(doc);
                PumpSettle(w);
                Check(viewer.PageCount == 3 && viewer.Scale > 0, "a letter PDF opens in a narrow window and fits");
                Check(FitsHorizontally(viewer), "a width-constrained fit stays within the viewport (no horizontal Auto scrollbar)");
                var scale = viewer.Scale;
                PumpSettle(w);
                Check(Math.Abs(viewer.Scale - scale) < 1e-6, "fit scale is stable after further layout passes");

                Sweep(w, WidthsNear(500, 400, 800), 900);
                Check(true, "resizing a 3-page letter PDF across the width scrollbar threshold does not loop");
                var after = viewer.Scale;
                PumpSettle(w);
                Check(Math.Abs(viewer.Scale - after) < 1e-6, "fit scale settles after the width sweep");

                w.Width = 900;
                Sweep(w, HeightsNear(700, 500, 1100), 900, horizontal: false);
                Check(true, "resizing across the height scrollbar threshold does not loop");
                w.Close();
            }

            using (var doc = PdfDocument.Open(landscape))
            {
                var viewer = new PdfViewer();
                var w = new Window { Width = 700, Height = 500, Content = viewer };
                w.Show();
                viewer.Show(doc);
                PumpSettle(w);
                Check(viewer.PageCount == 1 && viewer.Scale > 0, "a wide single-page PDF opens and fits");
                var scale = viewer.Scale;
                PumpSettle(w);
                Check(Math.Abs(viewer.Scale - scale) < 1e-6, "wide-page fit scale is stable");
                Sweep(w, WidthsNear(700, 450, 1300), 500);
                Check(true, "resizing a wide PDF across the width scrollbar threshold does not loop");
                w.Close();
            }

            // A single letter page in a window whose aspect is near the page's: the Auto
            // scrollbar can appear on one axis, shrink the other, and the fit scale then
            // makes that first bar unnecessary -- the classic two-axis oscillation.
            var single = Path.Combine(root, "single.pdf");
            File.WriteAllBytes(single, PdfFixtures.Pages(612, 792, "one"));
            using (var doc = PdfDocument.Open(single))
            {
                var viewer = new PdfViewer();
                var w = new Window { Width = 660, Height = 880, Content = viewer };
                w.Show();
                viewer.Show(doc);
                PumpSettle(w);
                Check(viewer.PageCount == 1 && viewer.Scale > 0, "a single letter page opens near the page aspect");
                var scale = viewer.Scale;
                PumpSettle(w);
                Check(Math.Abs(viewer.Scale - scale) < 1e-6, "single-page fit scale is stable at the aspect corner");
                // Sweep a band around the expected threshold (page + margins + toolbar).
                Sweep(w, WidthsNear(660, 600, 720), 880);
                Sweep(w, HeightsNear(880, 800, 960), 660, horizontal: false);
                Check(true, "resizing a single page around the two-axis scrollbar threshold does not loop");
                var after = viewer.Scale;
                PumpSettle(w);
                Check(Math.Abs(viewer.Scale - after) < 1e-6, "single-page fit scale settles after the aspect sweep");
                w.Close();
            }

            // Image viewer: fit-to-window on Surface.SizeChanged. Canvas-hosted, but run the same
            // near-threshold sweep so a loop there would fail this mode too.
            var image = new ImageViewer();
            var iw = new Window { Width = 440, Height = 440, Content = image };
            iw.Show();
            var bitmap = new RenderTargetBitmap(new PixelSize(800, 400));
            image.ShowImage(bitmap);
            PumpSettle(iw);
            Check(image.IsFit && image.ZoomPercent > 0, "image viewer starts in fit mode");
            var imgScale = image.Scale;
            PumpSettle(iw);
            Check(image.IsFit && Math.Abs(image.Scale - imgScale) < 1e-6, "image fit scale is stable");
            Sweep(iw, WidthsNear(440, 200, 900), 440);
            Sweep(iw, HeightsNear(300, 200, 700), 440, horizontal: false);
            Check(image.IsFit, "image viewer stays in fit mode through the resize sweep");
            var imgAfter = image.Scale;
            PumpSettle(iw);
            Check(image.IsFit && Math.Abs(image.Scale - imgAfter) < 1e-6, "image fit scale settles after the sweep");
            iw.Close();
            bitmap.Dispose();

            // Hong's exact crash: open a PDF, click Notes (right split), select text on the PDF
            // while it is still in the default fit-to-window mode. Existing --pdfselect-only /
            // --pdfnotes-only step to 100% first, which leaves fit mode and hid the loop.
            var selectPdf = Path.Combine(root, "select.pdf");
            File.WriteAllBytes(selectPdf, PdfFixtures.MultiPage("Page one: hello mdhub", "Page two: more text", "Page three: last page"));
            var shell = new MainWindow { Width = 1100, Height = 520 };
            try
            {
                shell.Show();
                PumpSettle(shell);
                var view = shell.GetVisualDescendants().OfType<MainView>().Single();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                Task Invoke(string m, params object?[] args) => (Task)typeof(MainView).GetMethod(m, flags)!.Invoke(view, args)!;
                void Wait(Task t)
                {
                    var until = DateTime.UtcNow.AddSeconds(15);
                    while (!t.IsCompleted && DateTime.UtcNow < until) PumpSettle(shell);
                    t.GetAwaiter().GetResult();
                    PumpSettle(shell);
                }
                Wait(Invoke("OpenPathAsync", selectPdf));
                var viewer = shell.GetVisualDescendants().OfType<PdfViewer>().First(x => x.IsVisible);
                Check(viewer.PageCount == 3 && viewer.Scale > 0, "open a PDF: it starts in fit-to-window mode");
                var notesButton = viewer.FindControl<Button>("NotesButton") ?? throw new Exception("missing NotesButton");
                notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Wait(Task.Delay(50));
                var paneCount = ((System.Collections.IEnumerable)typeof(MainView).GetProperty("EditorPanes", flags)!.GetValue(view)!).Cast<object>().Count();
                Check(paneCount == 2 && viewer.NotesOpen, $"clicking Notes opens a right split (panes={paneCount}, on={viewer.NotesOpen})");
                PumpSettle(shell);
                viewer = shell.GetVisualDescendants().OfType<PdfViewer>().First(x => x.IsVisible);
                Check(viewer.NotesOpen && viewer.Scale > 0, "the PDF stays fitted after the notes split narrows it");
                viewer.Tool = PdfTool.Select;
                viewer.Focus();
                PumpSettle(shell);
                DragSelectHello(shell, viewer);
                PumpSettle(shell);
                Check(viewer.HasSelection && viewer.SelectedText.Contains("hello") && viewer.SelectionPage == 1,
                    $"selecting text on the PDF with the notes split open does not loop ({viewer.SelectedText})");
                Check(viewer.NotesOpen, "the notes split is still open after the selection");
            }
            finally { try { shell.Close(); } catch { } }
        }
        finally { try { Directory.Delete(root, true); } catch { } }
        Console.WriteLine($"ok {passed}");
    }

    // Enough layout+render passes to trip Avalonia's infinite-layout detector if a pass keeps invalidating.
    static void PumpSettle(Window w)
    {
        try
        {
            for (var i = 0; i < 8; i++)
            {
                w.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                using var frame = w.CaptureRenderedFrame();
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Infinite layout loop", StringComparison.Ordinal))
        {
            throw new Exception($"infinite layout loop at {w.Width}x{w.Height}: {ex.Message}", ex);
        }
    }

    static int IndexOf(IReadOnlyList<PdfDocument.PdfChar> glyphs, string word)
    {
        var chars = glyphs.Select(g => g.Char).ToArray();
        return new string(chars).IndexOf(word, StringComparison.Ordinal);
    }

    static Point OnPage(Window w, Image image, PdfDocument doc, PdfDocument.PdfChar c, double xFrac, double yFrac)
    {
        var height = doc.Pages[0].Height;
        var left = Math.Min(c.Left, c.Right);
        var right = Math.Max(c.Left, c.Right);
        double top, bottom;
        if (c.Top <= c.Bottom) { top = c.Top; bottom = c.Bottom; }
        else
        {
            top = height - c.Top;
            bottom = height - c.Bottom;
            if (top > bottom) (top, bottom) = (bottom, top);
        }
        var sx = image.Bounds.Width / doc.Pages[0].Width;
        var sy = image.Bounds.Height / height;
        var local = new Point((left + (right - left) * xFrac) * sx, (top + (bottom - top) * yFrac) * sy);
        return image.TranslatePoint(local, w)!.Value;
    }

    static void DragSelectHello(Window w, PdfViewer viewer)
    {
        var image = viewer.GetVisualDescendants().OfType<Image>().First(img => img.IsVisible && img.Source is not null);
        var glyphs = viewer.Document!.Characters(0);
        var helloAt = IndexOf(glyphs, "hello");
        if (helloAt < 0) throw new Exception("hello is not on page 1 (glyphs=" + glyphs.Count + ")");
        var start = OnPage(w, image, viewer.Document, glyphs[helloAt], 0.2, 0.5);
        var stop = OnPage(w, image, viewer.Document, glyphs[helloAt + 4], 0.8, 0.5);
        w.MouseDown(start, MouseButton.Left);
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.MouseMove(stop);
        w.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        w.MouseUp(stop, MouseButton.Left);
    }

    static bool FitsHorizontally(PdfViewer viewer)
    {
        var scroller = viewer.GetVisualDescendants().OfType<ScrollViewer>().First();
        return scroller.Extent.Width <= scroller.Viewport.Width + 0.5;
    }

    static IEnumerable<double> WidthsNear(double around, double from, double to)
    {
        for (var x = from; x <= to; x += 8) yield return x;
        // Fine steps around the starting size (the Auto-scrollbar threshold is usually within a bar's width).
        for (var x = around - 24; x <= around + 24; x += 2) yield return x;
    }

    static IEnumerable<double> HeightsNear(double around, double from, double to) => WidthsNear(around, from, to);

    static void Sweep(Window w, IEnumerable<double> values, double fixedSide, bool horizontal = true)
    {
        foreach (var v in values)
        {
            if (horizontal) w.Width = v; else w.Height = v;
            if (horizontal) w.Height = fixedSide; else w.Width = fixedSide;
            PumpSettle(w);
        }
    }
}
