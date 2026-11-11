using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 108: double-click selects the word under the cursor (Hand and Select); empty space still resets the view.
internal static class PdfWordTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        static string Show(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");
        static string Fold(string s) => s.Replace((char)0x2019, '\'').Replace((char)0x2BC, '\'');
        static string Word(string s, int i)
        {
            var g = s.Select(c => new PdfText.Glyph(c, 0, 10, 12)).ToArray();
            return PdfText.WordRange(g, i) is { } x ? s[x.From..(x.To + 1)] : "";
        }

        var sample = "don't well-known hello, world";
        Check(Word(sample, sample.IndexOf('n')) == "don't" && Word(sample, sample.IndexOf('\'')) == "don't",
            "an apostrophe between letters is part of the word");
        Check(Word(sample, sample.IndexOf("known", StringComparison.Ordinal)) == "well-known",
            "a hyphen between letters is part of the word");
        Check(Word(sample, sample.IndexOf("hello", StringComparison.Ordinal)) == "hello" && Word(sample, sample.IndexOf(',')) == ",",
            "punctuation is excluded from the word and selected alone");
        Check(PdfText.WordRange(sample.Select(c => new PdfText.Glyph(c, 0, 10, 12)).ToArray(), sample.IndexOf(' ')) is null,
            "whitespace is not a word");
        Check(Word("don’t", 3) == "don’t", "a typographic apostrophe between letters is part of the word");
        Check(Word("中文测试", 1) == "中文测试"
            && Word("漢字かな", 0) == "漢字" && Word("漢字かな", 2) == "かな",
            "CJK with no spaces selects a run of the same script");
        Check(Word("hello中文", 0) == "hello" && Word("hello中文", 5) == "中文", "Latin and Han runs stay separate");
        Check(Word("안녕하세요", 2) == "안녕하세요", "a Hangul run is one word");

        var root = Path.Combine(Path.GetTempPath(), "mdhub-pdfword-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var good = Path.Combine(root, "guide.pdf");
        File.WriteAllBytes(good, PdfFixtures.MultiPage(sample, "Page two"));
        var notesPath = PdfNotes.PathFor(good);
        var w = new MainWindow { Width = 1100, Height = 520 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Task Invoke(string m, params object?[] args) => (Task)typeof(MainView).GetMethod(m, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(v, args)!;
            PdfViewer Viewer() => w.GetVisualDescendants().OfType<PdfViewer>().First(x => x.IsVisible);
            T Named<T>(PdfViewer viewer, string name) where T : Control => viewer.FindControl<T>(name) ?? throw new Exception("missing " + name);
            var ctrl = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            void Key(Key k, RawInputModifiers m = default) { w.KeyPress(k, m, PhysicalKey.None, null); w.KeyRelease(k, m, PhysicalKey.None, null); Pump(); }
            void Zoom100(PdfViewer viewer)
            {
                for (var i = 0; i < 20 && viewer.ZoomPercent != 100; i++) { if (viewer.ZoomPercent > 100) viewer.StepZoom(-1); else viewer.StepZoom(1); }
                Pump(); Pump();
            }
            void DoubleClick(Point p)
            {
                for (var i = 0; i < 2; i++) { w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left); }
                Pump(); Pump();
            }
            void TripleClick(Point p)
            {
                for (var i = 0; i < 3; i++) { w.MouseDown(p, MouseButton.Left); w.MouseUp(p, MouseButton.Left); }
                Pump(); Pump();
            }

            Wait(Invoke("OpenPathAsync", good));
            Pump(); Pump();
            var viewer = Viewer();
            Zoom100(viewer);
            Check(viewer.Tool == PdfTool.Hand, "Hand is the default tool");
            var image = viewer.GetVisualDescendants().OfType<Image>().First(img => img.IsVisible && img.Source is not null);
            var glyphs = viewer.Document!.Characters(0);
            var text = new string(glyphs.Select(g => g.Char).ToArray());
            Check(Fold(text).Contains("don't", StringComparison.Ordinal) && text.Contains("well-known", StringComparison.Ordinal),
                $"the fixture glyphs include the words ({Show(text)})");
            int At(string word) => Fold(text).IndexOf(word, StringComparison.Ordinal);
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

            foreach (var tool in new[] { PdfTool.Hand, PdfTool.Select })
            {
                viewer.Tool = tool; Pump();
                DoubleClick(OnGlyph(At("don't"), 0.5));
                Check(Fold(viewer.SelectedText) == "don't", $"{tool}: double-click selects the word with the apostrophe ({Show(viewer.SelectedText)})");
                DoubleClick(OnGlyph(At("well-known") + 5, 0.5));
                Check(viewer.SelectedText == "well-known", $"{tool}: double-click includes a hyphen between letters ({Show(viewer.SelectedText)})");
                DoubleClick(OnGlyph(At("hello"), 0.4));
                Check(viewer.SelectedText == "hello", $"{tool}: punctuation after the word is excluded ({Show(viewer.SelectedText)})");
            }

            viewer.Tool = PdfTool.Select; Pump();
            DoubleClick(OnGlyph(At("don't"), 0.5));
            Wait(w.Clipboard!.SetTextAsync("sentinel"));
            viewer.Focus(); Pump();
            Key(Avalonia.Input.Key.C, ctrl);
            var copied = w.Clipboard!.TryGetTextAsync(); Wait(copied);
            Check(Fold(copied.Result ?? "") == "don't" && Fold(viewer.LastCopiedText ?? "") == "don't", $"pdf.copy copies the double-clicked word ({copied.Result})");

            var menu = Named<ScrollViewer>(viewer, "Scroller").ContextMenu!;
            var item = menu.Items.OfType<MenuItem>().Single(i => i.Name == "AddToNotesMenuItem");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Wait(Task.Delay(50)); Pump(); Pump(); Pump();
            Check(File.Exists(notesPath), "Add to notes from a double-click selection creates the notes file");
            var notes = File.ReadAllText(notesPath).Replace("\r\n", "\n");
            var expected = PdfNotes.ApplyAppend("", viewer.SelectedText, good, 1).Replace("\r\n", "\n");
            Check(notes == expected && Fold(notes).Contains("> don't\n[p. 1]"),
                $"Add to notes quotes the double-clicked word ({Show(notes)})");

            TripleClick(OnGlyph(At("hello"), 0.5));
            Check(Fold(viewer.SelectedText) == Fold(text), $"triple-click selects the line ({Show(viewer.SelectedText)})");

            var notesButton = Named<Button>(viewer, "NotesButton");
            if (viewer.NotesOpen) { notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(Task.Delay(50)); Pump(); Pump(); }
            viewer = Viewer();
            viewer.Tool = PdfTool.Hand; Pump();
            Zoom100(viewer);
            viewer.StepZoom(1); Pump(); Pump();
            Check(!viewer.IsFitting && viewer.ZoomPercent == 150, $"zoom is 150% before the empty-space reset ({viewer.ZoomPercent})");
            viewer.ScrollToPage(1); Pump(); Pump();
            Wait(Task.Delay(700));
            var scroller = Named<ScrollViewer>(viewer, "Scroller");
            var vis = new Point(scroller.Bounds.Width * 0.5, Math.Min(scroller.Bounds.Height - 16, 240));
            var empty = scroller.TranslatePoint(vis, w)!.Value;
            var over = w.InputHitTest(empty);
            DoubleClick(empty);
            Check(viewer.IsFitting && !viewer.HasSelection,
                $"double-click on empty space still resets the view and clears the selection (over={over?.GetType().Name})");
        }
        finally { try { w.Close(); Pump(); } catch { } try { Directory.Delete(root, true); } catch { } }
        Console.WriteLine($"PDF word tests passed: {passed}");
    }
}
