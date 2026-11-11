using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 102b: drag-select PDF text and copy it with the pdf.copy shortcut (Ctrl+C / Cmd+C), scoped to the PDF view.
internal static class PdfSelectTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }

        Check(Shortcuts.Get("pdf.copy").Area == ShortcutArea.Pdf && Shortcuts.Get("pdf.copy").PdfOnly,
            "pdf.copy is a Pdf-area, PDF-only shortcut");
        Check(Shortcuts.Format("pdf.copy", false) == "Ctrl+C" && Shortcuts.Format("pdf.copy", true) == "Cmd+C",
            "pdf.copy is Ctrl+C on Windows and Cmd+C on Mac");
        Check(!Shortcuts.Conflicts().Any(c => c.A.Id == "pdf.copy" || c.B.Id == "pdf.copy"),
            "pdf.copy does not conflict with edit.copy or explorer.copy");
        Check(Loc.En.ContainsKey("AreaPdf") && Loc.Languages.All(l => Loc.Table(l.Code).ContainsKey("AreaPdf")),
            "AreaPdf is localized in every language table");

        var root = Path.Combine(Path.GetTempPath(), "mdhub-pdfselect-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var good = Path.Combine(root, "doc.pdf");
        var note = Path.Combine(root, "note.md");
        File.WriteAllBytes(good, PdfFixtures.MultiPage("Page one: hello mdhub", "Page two: more text", "Page three: last page"));
        File.WriteAllText(note, "note body text");

        using (var doc = PdfDocument.Open(good))
        {
            var glyphs = doc.Characters(0);
            Check(glyphs.Count > 0 && new string(glyphs.Select(g => g.Char).ToArray()).Contains("hello mdhub"),
                $"page 1 glyphs include the fixture text ({glyphs.Count} chars)");
            var hello = IndexOf(glyphs, "hello");
            Check(hello >= 0 && glyphs[hello].Left > 0 && glyphs[hello].Right > glyphs[hello].Left,
                $"the word hello has a real bounding box (L={glyphs[hello].Left:0.##} T={glyphs[hello].Top:0.##})");
            Console.WriteLine($"glyph[0] {glyphs[0].Char} L={glyphs[0].Left:0.##} T={glyphs[0].Top:0.##} R={glyphs[0].Right:0.##} B={glyphs[0].Bottom:0.##} pageH={doc.Pages[0].Height}");
        }

        var w = new MainWindow { Width = 900, Height = 520 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Task Invoke(string m, params object?[] args) => (Task)typeof(MainView).GetMethod(m, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(v, args)!;
            NoteDocument? Active() => (NoteDocument?)typeof(MainView).GetField("_active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(v);
            PdfViewer Viewer() => w.GetVisualDescendants().OfType<PdfViewer>().First(x => x.IsVisible);
            var ctrl = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            void Key(Key k, RawInputModifiers m = default) { w.KeyPress(k, m, PhysicalKey.None, null); w.KeyRelease(k, m, PhysicalKey.None, null); Pump(); }

            Wait(Invoke("OpenPathAsync", good));
            Pump(); Pump();
            var viewer = Viewer();
            for (var i = 0; i < 20 && viewer.ZoomPercent > 100; i++) viewer.StepZoom(-1);
            for (var i = 0; i < 20 && viewer.ZoomPercent < 100; i++) viewer.StepZoom(1);
            Pump(); Pump();
            Check(viewer.ZoomPercent == 100, $"zoom is 100% for hit testing ({viewer.ZoomPercent})");
            viewer.Focus(); Pump();

            var image = viewer.GetVisualDescendants().OfType<Image>().First(img => img.IsVisible && img.Source is not null);
            var glyphs = viewer.Document!.Characters(0);
            var helloAt = IndexOf(glyphs, "hello");
            Check(helloAt >= 0, "hello is on page 1");
            Point OnPage(PdfDocument.PdfChar c, double xFrac, double yFrac)
            {
                var height = viewer.Document!.Pages[0].Height;
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
                var sx = image.Bounds.Width / viewer.Document.Pages[0].Width;
                var sy = image.Bounds.Height / height;
                var local = new Point((left + (right - left) * xFrac) * sx, (top + (bottom - top) * yFrac) * sy);
                return image.TranslatePoint(local, w)!.Value;
            }

            var start = OnPage(glyphs[helloAt], 0.2, 0.5);
            var stop = OnPage(glyphs[helloAt + 4], 0.8, 0.5);
            w.MouseDown(start, MouseButton.Left); Pump();
            w.MouseMove(stop); Pump();
            w.MouseUp(stop, MouseButton.Left); Pump(); Pump();
            Check(viewer.HasSelection && viewer.SelectedText.Contains("hello") && viewer.SelectionPage == 1,
                $"drag selects the word hello ({viewer.SelectedText})");
            Check(!viewer.SelectedText.Contains("more text") && !viewer.SelectedText.Contains("Page two"),
                "the selection stays on the dragged page");

            Wait(w.Clipboard!.SetTextAsync("sentinel"));
            viewer.Focus(); Pump();
            Key(Avalonia.Input.Key.C, ctrl);
            var copied = w.Clipboard!.TryGetTextAsync(); Wait(copied);
            Check(copied.Result == viewer.SelectedText && viewer.LastCopiedText == viewer.SelectedText,
                $"Ctrl+C copies the selected PDF text as plain text ({copied.Result})");

            // Click well below the text line (letter page, text is near the top in image space or near y=700 in PDF space).
            var emptyLocal = new Point(image.Bounds.Width * 0.5, 220);
            var empty = image.TranslatePoint(emptyLocal, w)!.Value;
            w.MouseDown(empty, MouseButton.Left); w.MouseUp(empty, MouseButton.Left); Pump();
            Check(!viewer.HasSelection, "a click away from the text clears the selection");

            Wait(w.Clipboard!.SetTextAsync("sentinel"));
            viewer.Focus(); Pump();
            Key(Avalonia.Input.Key.C, ctrl);
            var after = w.Clipboard!.TryGetTextAsync(); Wait(after);
            Check(after.Result == "sentinel" && viewer.LastCopiedText != "sentinel",
                "Ctrl+C with no PDF selection leaves the clipboard alone");

            // Editor focus: Ctrl+C still copies the note, not the PDF shortcut.
            Wait(Invoke("OpenPathAsync", note));
            Pump();
            var editor = w.GetVisualDescendants().OfType<TextBox>().First(t => t.IsVisible && t.Text == "note body text");
            editor.Focus(); editor.SelectAll(); Pump();
            Wait(w.Clipboard!.SetTextAsync("sentinel"));
            Key(Avalonia.Input.Key.C, ctrl);
            var noteCopy = w.Clipboard!.TryGetTextAsync(); Wait(noteCopy);
            Check(noteCopy.Result == "note body text", $"Ctrl+C in the editor copies the note, not PDF text ({noteCopy.Result})");
        }
        finally { try { w.Close(); Pump(); } catch { } try { Directory.Delete(root, true); } catch { } }
        Console.WriteLine($"PDF selection tests passed: {passed}");
    }

    static int IndexOf(IReadOnlyList<PdfDocument.PdfChar> glyphs, string word)
    {
        var chars = glyphs.Select(g => g.Char).ToArray();
        var s = new string(chars);
        return s.IndexOf(word, StringComparison.Ordinal);
    }
}