using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Rendering;
using Mdhub.Services;
using Mdhub.Views;

// Item 102d: clicking a file.pdf#page=N link in the editor (Ctrl/Cmd+click) or the preview opens or focuses that PDF and scrolls to the page.
internal static class PdfPageLinkTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); Pump(); }
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var root = Path.Combine(Path.GetTempPath(), "mdhub-pdfpagelink-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pdf = Path.Combine(root, "my report.pdf");
        File.WriteAllBytes(pdf, PdfFixtures.MultiPage("Page one: hello", "Page two: more", "Page three: last"));
        var notesPath = PdfNotes.PathFor(pdf);
        Directory.CreateDirectory(Path.GetDirectoryName(notesPath)!);
        File.WriteAllText(notesPath, "See [p. 2](../my%20report.pdf#page=2) then [p. last](../my%20report.pdf#page=99) or [p. start](../my%20report.pdf#page=0).\n");
        var w = new MainWindow { Width = 1100, Height = 560 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Task Invoke(string m, params object?[] args) => (Task)typeof(MainView).GetMethod(m, Flags)!.Invoke(v, args)!;
            IReadOnlyList<EditorPane> Panes() => ((IReadOnlyList<EditorPane>)typeof(MainView).GetProperty("EditorPanes", Flags)!.GetValue(v)!).ToList();
            IEnumerable<NoteDocument> AllDocs() => (IEnumerable<NoteDocument>)typeof(MainView).GetProperty("AllDocuments", Flags)!.GetValue(v)!;
            EditorPane Active() => (EditorPane)typeof(MainView).GetProperty("ActivePane", Flags)!.GetValue(v)!;
            int PdfViews() => AllDocs().Count(d => string.Equals(d.Path, pdf, StringComparison.OrdinalIgnoreCase));

            Wait(Invoke("OpenPathAsync", pdf));
            Pump(); Pump();
            var pdfPane = Panes().Single();
            var notesButton = pdfPane.FindControl<Button>("NotesButton") ?? w.GetVisualDescendants().OfType<PdfViewer>().First(x => x.IsVisible).FindControl<Button>("NotesButton")!;
            notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(Task.Delay(80)); Pump(); Pump(); Pump(); Pump();
            Check(Panes().Count == 2 && PdfViews() == 1, "the PDF is open in one tab and notes are in a right split");
            var notesPane = Panes()[1];
            var notesDoc = notesPane.Documents.Single(d => string.Equals(d.Path, notesPath, StringComparison.OrdinalIgnoreCase));
            var md = notesPane.FindControl<MdEditor>("MdEditor") ?? notesPane.GetVisualDescendants().OfType<MdEditor>().Single();
            for (var i = 0; i < 10 && md.GetVisualDescendants().OfType<TextBlock>().All(t => t.Inlines is not { Count: > 0 }); i++) Pump();

            Point? PreviewLink(string label)
            {
                foreach (var tb in md.GetVisualDescendants().OfType<TextBlock>())
                {
                    if (tb.Inlines is not { Count: > 0 } inlines || tb.TextLayout is null) continue;
                    var text = string.Concat(Flatten(inlines));
                    var at = text.IndexOf(label, StringComparison.Ordinal);
                    if (at < 0) continue;
                    var origin = tb.TranslatePoint(new Point(0, 0), w);
                    if (origin is null) continue;
                    var r = tb.TextLayout.HitTestTextPosition(at + label.Length / 2);
                    return origin.Value + new Point(r.X + 1, r.Y + r.Height / 2);
                }
                return null;
            }
            static IEnumerable<string> Flatten(InlineCollection inlines)
            {
                foreach (var inline in inlines)
                {
                    if (inline is Run run) yield return run.Text ?? "";
                    else if (inline is Span span) foreach (var s in Flatten(span.Inlines)) yield return s;
                    else yield return "";
                }
            }

            var two = PreviewLink("p. 2");
            Check(two is not null, "the preview rendered the page-2 link");
            w.MouseDown(two!.Value, MouseButton.Left); Pump();
            w.MouseUp(two.Value, MouseButton.Left); Pump();
            Wait(Task.Delay(80)); Pump(); Pump(); Pump();
            var viewer = Panes().SelectMany(p => p.GetVisualDescendants().OfType<PdfViewer>()).First(x => x.IsVisible);
            Check(Active() == Panes()[0] && viewer.CurrentPage == 2 && PdfViews() == 1,
                $"a preview click opens the existing PDF tab and scrolls to page 2 (page {viewer.CurrentPage}, views {PdfViews()})");

            var last = PreviewLink("p. last");
            Check(last is not null, "the preview rendered the out-of-range link");
            w.MouseDown(last!.Value, MouseButton.Left); Pump();
            w.MouseUp(last.Value, MouseButton.Left); Pump();
            Wait(Task.Delay(80)); Pump(); Pump(); Pump();
            viewer = Panes().SelectMany(p => p.GetVisualDescendants().OfType<PdfViewer>()).First(x => x.IsVisible);
            Check(viewer.CurrentPage == 3 && PdfViews() == 1, $"#page=99 is clamped to the last page ({viewer.CurrentPage})");

            var start = PreviewLink("p. start");
            Check(start is not null, "the preview rendered the page-0 link");
            w.MouseDown(start!.Value, MouseButton.Left); Pump();
            w.MouseUp(start.Value, MouseButton.Left); Pump();
            Wait(Task.Delay(80)); Pump(); Pump(); Pump();
            viewer = Panes().SelectMany(p => p.GetVisualDescendants().OfType<PdfViewer>()).First(x => x.IsVisible);
            Check(viewer.CurrentPage == 1 && PdfViews() == 1, $"#page=0 is clamped to the first page ({viewer.CurrentPage})");

            // Editor: Ctrl+click (Cmd+click on macOS) on the same encoded page-2 link.
            var editor = notesDoc.Editor;
            notesPane = Panes()[1];
            ActivateNotes();
            void ActivateNotes()
            {
                var pane = Panes()[1];
                pane.FindControl<Control>("TextEditor")?.Focus();
                editor.Focus(); Pump();
            }
            ActivateNotes();
            var hrefAt = notesDoc.Text.IndexOf("../my%20report.pdf#page=2", StringComparison.Ordinal);
            Check(hrefAt >= 0, "the notes source contains the encoded page-2 link");
            var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().First();
            Pump(); Pump();
            var box = presenter.TextLayout.HitTestTextPosition(hrefAt + 5);
            var editorPt = presenter.TranslatePoint(new Point(box.X + 1, box.Y + box.Height / 2), w);
            Check(editorPt is not null, "the editor laid out the link");
            var cmd = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            w.MouseDown(editorPt!.Value, MouseButton.Left, cmd); Pump();
            w.MouseUp(editorPt.Value, MouseButton.Left, cmd); Pump();
            Wait(Task.Delay(80)); Pump(); Pump(); Pump();
            viewer = Panes().SelectMany(p => p.GetVisualDescendants().OfType<PdfViewer>()).First(x => x.IsVisible);
            Check(Active() == Panes()[0] && viewer.CurrentPage == 2 && PdfViews() == 1 && Panes().Count == 2,
                $"Ctrl+click in the editor focuses the already-open PDF tab at page 2 without duplicating it (page {viewer.CurrentPage}, views {PdfViews()}, panes {Panes().Count})");
        }
        finally
        {
            try { w.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"PDF page-link tests passed: {passed}");
    }
}