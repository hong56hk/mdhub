using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;
using Mdhub.Views;

// Item 109: while panning a PDF (Hand tool or right-drag), show a cached generated open-hand cursor
// and restore the previous cursor on release and on pointer capture lost.
internal static class PdfCursorTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        static string CursorName(Cursor? c) => c?.ToString() ?? "(null)";
        static string HandName() => new Cursor(StandardCursorType.Hand).ToString();
        static string IbeamName() => new Cursor(StandardCursorType.Ibeam).ToString();

        var standard = PdfCursors.OpenHand(1);
        var alsoStandard = PdfCursors.OpenHand(1.2);
        var highDpi = PdfCursors.OpenHand(2);
        var alsoHigh = PdfCursors.OpenHand(1.5);
        Check(ReferenceEquals(standard, alsoStandard) && ReferenceEquals(highDpi, alsoHigh) && !ReferenceEquals(standard, highDpi),
            "the open-hand cursor is cached, with a 32x32 and a 64x64 high-DPI variant");
        Check(CursorName(standard) != HandName(),
            "the pan cursor is a custom open hand, not StandardCursorType.Hand");
        Check(PdfCursors.Hotspot(32) == new PixelPoint(17, 20) && PdfCursors.Hotspot(64) == new PixelPoint(34, 40),
            "the hotspot is at the palm centre");

        var root = Path.Combine(Path.GetTempPath(), "mdhub-pdfcursor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var good = Path.Combine(root, "report.pdf");
        File.WriteAllBytes(good, PdfFixtures.MultiPage("Page one: hello mdhub", "Page two: more text", "Page three: last page"));
        var w = new MainWindow { Width = 900, Height = 520 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Task Invoke(string m, params object?[] args) => (Task)typeof(MainView).GetMethod(m, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(v, args)!;
            Wait(Invoke("OpenPathAsync", good));
            Pump(); Pump();
            var viewer = w.GetVisualDescendants().OfType<PdfViewer>().First(x => x.IsVisible);
            var scroller = viewer.FindControl<ScrollViewer>("Scroller") ?? throw new Exception("missing Scroller");
            var menu = scroller.ContextMenu!;
            for (var i = 0; i < 20 && viewer.ZoomPercent != 100; i++) { if (viewer.ZoomPercent > 100) viewer.StepZoom(-1); else viewer.StepZoom(1); }
            Pump(); Pump();
            viewer.ScrollToPage(1); Pump(); Pump();
            var image = viewer.GetVisualDescendants().OfType<Image>().First(img => img.IsVisible && img.Source is not null);
            var mid = image.TranslatePoint(new Point(image.Bounds.Width / 2, 200), w)!.Value;
            var scale = TopLevel.GetTopLevel(viewer)?.RenderScaling ?? 1;
            var open = PdfCursors.OpenHand(scale);

            viewer.Tool = PdfTool.Hand; Pump();
            Check(CursorName(viewer.PageCursor) == HandName() && !viewer.IsPanning,
                "the Hand tool uses StandardCursorType.Hand at rest");

            var before = scroller.Offset.Y;
            w.MouseDown(mid, MouseButton.Left); Pump();
            Check(viewer.IsPanning && ReferenceEquals(viewer.PageCursor, open),
                "a left-button Hand pan shows the open-hand cursor");
            w.MouseMove(mid + new Point(0, -80)); Pump();
            Check(viewer.IsPanning && scroller.Offset.Y != before && ReferenceEquals(viewer.PageCursor, open),
                "the open-hand cursor stays in place while the pages pan");
            w.MouseUp(mid + new Point(0, -80), MouseButton.Left); Pump();
            Check(!viewer.IsPanning && CursorName(viewer.PageCursor) == HandName(),
                "releasing the left button restores the Hand cursor");

            // Capture lost: start a pan, then release capture without a MouseUp first.
            Wait(Task.Delay(700));
            viewer.ScrollToPage(1); Pump(); Pump();
            image = viewer.GetVisualDescendants().OfType<Image>().First(img => img.IsVisible && img.Source is not null);
            mid = image.TranslatePoint(new Point(image.Bounds.Width / 2, 200), w)!.Value;
            IPointer? pointer = null;
            void Save(object? s, PointerPressedEventArgs e) { pointer = e.Pointer; }
            viewer.AddHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Save, RoutingStrategies.Tunnel);
            w.MouseDown(mid, MouseButton.Left); Pump();
            viewer.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Save);
            Check(viewer.IsPanning && ReferenceEquals(viewer.PageCursor, open) && pointer is not null,
                "a second left pan shows the open-hand cursor again");
            pointer!.Capture(null);
            Pump();
            Check(!viewer.IsPanning && CursorName(viewer.PageCursor) == HandName(),
                "losing pointer capture restores the Hand cursor");
            // Release the headless mouse so later gestures are not still "pressed".
            w.MouseUp(mid, MouseButton.Left); Pump();
            Check(!viewer.IsPanning && CursorName(viewer.PageCursor) == HandName(),
                "a MouseUp after capture lost leaves the rest cursor in place");

            // Right-drag panning (any tool), same cursor. First make a selection so a click can open the menu.
            viewer.Tool = PdfTool.Select; Pump();
            Check(CursorName(viewer.PageCursor) == IbeamName(), "the Select tool uses the I-beam at rest");
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
            Check(viewer.HasSelection && viewer.SelectedText.Contains("hello"), $"a selection is ready for the context menu ({viewer.SelectedText})");

            var selected = viewer.SelectedText;
            var offsetBeforeRight = scroller.Offset;
            var pRight = mid + new Point(20, 20);
            w.MouseDown(pRight, MouseButton.Right); Pump();
            Check(!viewer.IsPanning && !viewer.IsRightPanning && CursorName(viewer.PageCursor) == IbeamName(),
                "a right press under the move threshold does not pan or change the cursor");
            // Drag up so Offset.Y increases even when the scroller is already at the top.
            w.MouseMove(pRight + new Point(0, -80)); Pump();
            Check(viewer.IsRightPanning && viewer.IsPanning && ReferenceEquals(viewer.PageCursor, open),
                "right-drag panning with the Select tool shows the same open-hand cursor");
            Check(scroller.Offset != offsetBeforeRight && viewer.SelectedText == selected,
                "right-drag pans the pages and keeps the text selection");
            w.MouseUp(pRight + new Point(0, -80), MouseButton.Right); Pump();
            Check(!viewer.IsPanning && !viewer.IsRightPanning && CursorName(viewer.PageCursor) == IbeamName() && !menu.IsOpen,
                "releasing a right-drag restores the I-beam and does not open Add to notes");

            w.MouseDown(pRight, MouseButton.Right); Pump();
            w.MouseUp(pRight + new Point(3, -3), MouseButton.Right); Pump();
            Check(menu.IsOpen && viewer.SelectedText == selected && !viewer.IsRightPanning,
                "a right click (moved < 4 px) opens Add to notes and does not pan");
            menu.Close(); Pump();

            viewer.Tool = PdfTool.Hand; Pump();
            Wait(Task.Delay(700));
            image = viewer.GetVisualDescendants().OfType<Image>().First(img => img.IsVisible && img.Source is not null);
            pRight = image.TranslatePoint(new Point(image.Bounds.Width / 2, 200), w)!.Value;
            var offsetBeforeHandRight = scroller.Offset;
            w.MouseDown(pRight, MouseButton.Right); Pump();
            w.MouseMove(pRight + new Point(0, -80)); Pump();
            Check(viewer.IsRightPanning && ReferenceEquals(viewer.PageCursor, open),
                "right-drag panning with the Hand tool also uses the open-hand cursor");
            w.MouseUp(pRight + new Point(0, -80), MouseButton.Right); Pump();
            Check(!viewer.IsPanning && CursorName(viewer.PageCursor) == HandName() && scroller.Offset != offsetBeforeHandRight && !menu.IsOpen,
                "releasing a Hand-tool right-drag restores the Hand cursor and does not open the menu");
        }
        finally
        {
            try { w.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"PDF cursor tests passed: {passed}");
    }
}
