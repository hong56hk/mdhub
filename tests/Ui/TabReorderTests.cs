using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;

internal static class TabReorderTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var docs = new ObservableCollection<NoteDocument>(new[] { "a.md", "a much longer tab name.md", "c.md" }
            .Select(name => new NoteDocument { UntitledName = name }));
        var original = docs.ToArray();
        var tabs = new FileTabs();
        tabs.SetDocuments(docs);
        var window = new Window { Content = tabs, Width = 900, Height = 100 };
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
        ListBox List() => tabs.FindControl<ListBox>("Tabs")!;
        Point At(NoteDocument doc) => ((ListBoxItem)List().ContainerFromIndex(docs.IndexOf(doc))!)
            .TranslatePoint(new Point(30, 15), window)!.Value;
        void Drag(NoteDocument doc, Point destination)
        {
            var start = At(doc);
            window.MouseDown(start, MouseButton.Left); Pump();
            window.MouseMove(destination, RawInputModifiers.LeftMouseButton); Pump();
            window.MouseUp(destination, MouseButton.Left); Pump();
        }
        try
        {
            window.Show(); Pump();
            original[0].Editor.Text = "unsaved text";
            original[0].Editor.CaretIndex = 4;
            Drag(original[0], new Point(650, At(original[0]).Y));
            Check(docs.SequenceEqual([original[1], original[2], original[0]]), "Drag first tab to last: " + string.Join(", ", docs.Select(d => d.Title)));
            Check(tabs.SelectedDocument == original[0], "Dragged tab stays selected");
            Check(original[0].Text == "unsaved text" && original[0].Editor.CaretIndex == 4, "Editor state survives reordering");
            Drag(original[0], new Point(1, At(original[0]).Y));
            Check(docs.SequenceEqual(original), "Drag last tab to first across tabs of different widths");
            var p = At(original[1]);
            window.MouseDown(p, MouseButton.Left); window.MouseMove(p + new Vector(2, 0)); window.MouseUp(p, MouseButton.Left); Pump();
            Check(docs.SequenceEqual(original) && tabs.SelectedDocument == original[1], "Click and small movement select without reordering");
            var closed = false;
            tabs.CloseRequested += d => closed = d == original[1];
            var close = ((ListBoxItem)List().ContainerFromIndex(1)!).GetVisualDescendants().OfType<Button>().Single();
            var cp = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
            window.MouseDown(cp, MouseButton.Left); window.MouseUp(cp, MouseButton.Left); Pump();
            Check(closed && docs.SequenceEqual(original), "Close button still works without reordering");
            tabs.SetBusy(true);
            Drag(original[0], new Point(650, At(original[0]).Y));
            Check(docs.SequenceEqual(original), "Busy tab strip cannot be reordered");
        }
        finally { window.Close(); }
    }
}
