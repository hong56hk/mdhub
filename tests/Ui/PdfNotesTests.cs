using System.Reflection;
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
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 102c: Notes button in the reserved toolbar slot; Add to notes from a PDF text selection.
internal static class PdfNotesTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var root = Path.Combine(Path.GetTempPath(), "mdhub-pdfnotes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var good = Path.Combine(root, "my report.pdf");
        File.WriteAllBytes(good, PdfFixtures.MultiPage("Page one: hello mdhub", "Page two: more text"));
        var notesPath = PdfNotes.PathFor(good);
        var pdfBytes = File.ReadAllBytes(good);
        var w = new MainWindow { Width = 1100, Height = 520 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Task Invoke(string m, params object?[] args) => (Task)typeof(MainView).GetMethod(m, Flags)!.Invoke(v, args)!;
            IReadOnlyList<EditorPane> Panes() => ((IReadOnlyList<EditorPane>)typeof(MainView).GetProperty("EditorPanes", Flags)!.GetValue(v)!).ToList();
            PdfViewer Viewer() => w.GetVisualDescendants().OfType<PdfViewer>().First(x => x.IsVisible);
            T Named<T>(PdfViewer viewer, string name) where T : Control => viewer.FindControl<T>(name) ?? throw new Exception("missing " + name);

            var idle = w.GetVisualDescendants().OfType<PdfViewer>().First();
            Check(!Named<Button>(idle, "NotesButton").IsEnabled, "the Notes button is disabled when no document is open");
            Check(!Directory.Exists(Path.Combine(root, ".assets")) && !File.Exists(notesPath), "opening the window does not create the notes file");

            Wait(Invoke("OpenPathAsync", good));
            Pump(); Pump();
            Check(!Directory.Exists(Path.Combine(root, ".assets")) && !File.Exists(notesPath), "opening the PDF does not create the notes file");

            var viewer = Viewer();
            var notesButton = Named<Button>(viewer, "NotesButton");
            var open = Named<Button>(viewer, "OpenButton");
            var slot = Named<Border>(viewer, "NotesSlot");
            Check(notesButton.IsEnabled && Equals(ToolTip.GetTip(notesButton), Loc.T("PdfAddNotes")), "the Notes button is enabled and has a localized Add Notes tooltip");
            Check(slot.GetVisualDescendants().Contains(notesButton) && notesButton.Bounds.Left <= open.Bounds.Left,
                "the Notes button sits in the reserved slot, before Open with default app");

            notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(Task.Delay(50)); Pump(); Pump(); Pump();
            Check(File.Exists(notesPath), "clicking Notes creates .assets/<stem>.notes.md");
            var panes = Panes();
            Check(panes.Count == 2, $"Notes opens a right split ({panes.Count} panes)");
            var pdfPane = panes[0];
            var notesPane = panes[1];
            var notesDoc = notesPane.Documents.SingleOrDefault(d => string.Equals(d.Path, notesPath, StringComparison.OrdinalIgnoreCase));
            Check(notesDoc is { IsMarkdown: true, HasPreview: true }, "the notes tab is a normal Markdown document with preview");
            Check(notesPane.FindControl<Control>("TextEditor")!.IsVisible && notesPane.FindControl<Control>("MdEditor")!.IsVisible, "the notes split shows the editor and the preview");
            Check(pdfPane.FindControl<PdfViewer>("PdfViewer")!.IsVisible && pdfPane == (EditorPane)typeof(MainView).GetProperty("ActivePane", Flags)!.GetValue(v)!,
                "the PDF pane stays active after Notes opens the split");
            Check(viewer.NotesOpen && notesButton.Classes.Contains("on"), "the Notes button is on, in theme colours, while the notes split is open");

            File.WriteAllText(notesPath, "kept");
            // Re-open would overwrite only if we recreated; the file already exists, toggle close then... first close the split.
            notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(Task.Delay(50)); Pump(); Pump();
            Check(Panes().Count == 1, "clicking Notes again closes the notes split");
            viewer = Viewer();
            notesButton = Named<Button>(viewer, "NotesButton");
            Check(!viewer.NotesOpen && !notesButton.Classes.Contains("on"), "the Notes button turns off when the split is toggled closed");
            Check(File.ReadAllText(notesPath) == "kept" || File.ReadAllText(notesPath) == "", "toggling closed leaves the notes file (empty on first create)");

            // Restore 'kept' and open again: existing content must not be overwritten.
            File.WriteAllText(notesPath, "kept");
            notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(Task.Delay(50)); Pump(); Pump();
            notesDoc = Panes()[1].Documents.Single(d => string.Equals(d.Path, notesPath, StringComparison.OrdinalIgnoreCase));
            Check(notesDoc.Text == "kept", "opening existing notes does not overwrite them");

            // Close again, then Add to notes should reopen and append.
            notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(Task.Delay(50)); Pump(); Pump();

            viewer = Viewer();
            for (var i = 0; i < 20 && viewer.ZoomPercent != 100; i++) { if (viewer.ZoomPercent > 100) viewer.StepZoom(-1); else viewer.StepZoom(1); }
            Pump(); Pump();
            viewer.Tool = PdfTool.Select; Pump();
            var image = viewer.GetVisualDescendants().OfType<Image>().First(img => img.IsVisible && img.Source is not null);
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
            Check(viewer.HasSelection && viewer.SelectedText.Contains("hello") && viewer.SelectionPage == 1, $"selected hello on page 1 ({viewer.SelectedText})");

            var menu = Named<ScrollViewer>(viewer, "Scroller").ContextMenu!;
            var item = menu.Items.OfType<MenuItem>().Single(i => i.Name == "AddToNotesMenuItem");
            Check(Equals(item.Header, Loc.T("PdfAddToNotes")) || (item.Header as string) == Loc.T("PdfAddToNotes"),
                "the context menu has the localized Add to notes item");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Wait(Task.Delay(50)); Pump(); Pump(); Pump();

            Check(Panes().Count == 2, "Add to notes opens the notes split if it was closed");
            notesDoc = Panes()[1].Documents.Single(d => string.Equals(d.Path, notesPath, StringComparison.OrdinalIgnoreCase));
            static string Norm(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');
            var expected = PdfNotes.ApplyAppend("kept", viewer.SelectedText, good, 1);
            Check(Norm(notesDoc.Text) == expected && Norm(File.ReadAllText(notesPath)) == expected,
                $"Add to notes appends a blank line, the quote and a relative page link ({notesDoc.Text})");
            Check(expected.Contains("../my%20report.pdf#page=1"), "spaces in the PDF file name are URL-encoded in the link");
            Check(Panes()[0].FindControl<PdfViewer>("PdfViewer")!.IsVisible && (EditorPane)typeof(MainView).GetProperty("ActivePane", Flags)!.GetValue(v)! == Panes()[0],
                "focus stays on the PDF after Add to notes");
            Check(File.ReadAllBytes(good).SequenceEqual(pdfBytes), "Add to notes never modifies the PDF");
            viewer = Viewer();
            Check(viewer.NotesOpen && Named<Button>(viewer, "NotesButton").Classes.Contains("on"), "Add to notes that reopens the split turns the Notes button on");
            typeof(MainView).GetMethod("ActivatePane", Flags)!.Invoke(v, [Panes()[1]]);
            Panes()[1].FindControl<Control>("TextEditor")!.Focus(); Pump();
            var closeMods = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            w.KeyPress(Key.W, closeMods, PhysicalKey.None, null); w.KeyRelease(Key.W, closeMods, PhysicalKey.None, null);
            Wait(Task.Delay(80)); Pump(); Pump();
            Check(Panes().Count == 1 && !Viewer().NotesOpen && !Named<Button>(Viewer(), "NotesButton").Classes.Contains("on"),
                $"closing the notes tab with Ctrl+W turns the Notes button off (panes {Panes().Count}, on={Viewer().NotesOpen})");
        }
        finally
        {
            try { w.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"PDF notes tests passed: {passed}");
    }
}