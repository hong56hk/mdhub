using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Models;
using Mdhub.Views;

// Bug 76 (Shift+Home) and bug 77 (Shift+End), VS Code behaviour: with a multi-line selection only the caret (the
// active end, SelectionEnd) moves to the start/end of its own line; the anchor (SelectionStart) stays.
internal static class SelectionHomeTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 3; i++) Pump(); }
        var w = new MainWindow { Width = 1300, Height = 700 };
        try
        {
            w.Show(); Settle();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            string[] lines = ["line one", "line two", "line three here", "    indented four", "line five", "line six is longer", "line seven", "last"];
            var nl = "\r\n";
            var text = string.Join(nl, lines);
            var doc = (NoteDocument)typeof(MainView).GetMethod("AddDocument", Flags)!.Invoke(v, [null, text, null, null])!;
            Settle();
            var ed = doc.Editor; ed.Focus(); Settle();
            int Off(int line, int col) { var o = 0; for (var i = 0; i < line - 1; i++) o += lines[i].Length + nl.Length; return o + col; } // 1-based line
            int End(int line) => Off(line, lines[line - 1].Length);
            void Select(int anchor, int caret) { ed.SelectionStart = anchor; ed.SelectionEnd = caret; Settle(); }
            void Press(Key key, RawInputModifiers mods = RawInputModifiers.Shift) { w.KeyPress(key, mods, key == Key.Home ? PhysicalKey.Home : PhysicalKey.End, null); Settle(); }
            string Sel() => $"{ed.SelectionStart}->{ed.SelectionEnd} (caret {ed.CaretIndex})";

            // --- Bug 76: Shift+Home ---
            Select(Off(3, 5), Off(6, 9));
            Press(Key.Home);
            Check(ed.SelectionStart == Off(3, 5) && ed.SelectionEnd == Off(6, 0) && ed.CaretIndex == Off(6, 0), $"Bug 76: downward selection (mid 3 -> mid 6): Shift+Home keeps the anchor, caret to the start of line 6: {Sel()}");
            Check(ed.SelectedText == text[Off(3, 5)..Off(6, 0)], "Bug 76: the selected text runs from the anchor to the start of line 6");
            Select(Off(6, 9), Off(3, 5));
            Press(Key.Home);
            Check(ed.SelectionStart == Off(6, 9) && ed.SelectionEnd == Off(3, 0) && ed.CaretIndex == Off(3, 0), $"Bug 76: upward selection (mid 6 -> mid 3): the caret moves to the start of line 3, the anchor stays: {Sel()}");
            Select(Off(5, 2), Off(5, 7));
            Press(Key.Home);
            Check(ed.SelectionStart == Off(5, 2) && ed.SelectionEnd == Off(5, 0), $"Bug 76: single-line selection: Shift+Home selects from the anchor back to column 0: {Sel()}");
            Select(Off(2, 3), Off(2, 3));
            Press(Key.Home);
            Check(ed.SelectionStart == Off(2, 3) && ed.SelectionEnd == Off(2, 0), $"Bug 76: no selection: Shift+Home selects to the line start: {Sel()}");
            // Home behaviour on an indented line: Shift+Home lands where plain Home lands (the editor has no smart Home, so column 0).
            Select(Off(4, 10), Off(4, 10));
            Press(Key.Home, RawInputModifiers.None);
            var plainHome = ed.CaretIndex;
            Select(Off(2, 4), Off(4, 10));
            Press(Key.Home);
            Check(plainHome == Off(4, 0) && ed.SelectionStart == Off(2, 4) && ed.SelectionEnd == plainHome, $"Bug 76: on an indented caret line Shift+Home goes where Home goes ({plainHome}): {Sel()}");
            Select(Off(3, 5), Off(6, 9));
            Press(Key.Home); Press(Key.Home);
            Check(ed.SelectionStart == Off(3, 5) && ed.SelectionEnd == Off(6, 0), "Bug 76: pressing Shift+Home again keeps the selection");
            Press(Key.Right);
            Check(ed.SelectionStart == Off(3, 5) && ed.SelectionEnd == Off(6, 1), $"Bug 76: the caret stays the active end (Shift+Right extends from it): {Sel()}");

            // --- Bug 77: Shift+End ---
            Select(Off(3, 5), Off(6, 9));
            Press(Key.End);
            Check(ed.SelectionStart == Off(3, 5) && ed.SelectionEnd == End(6) && ed.CaretIndex == End(6), $"Bug 77: downward selection (mid 3 -> mid 6): Shift+End keeps the anchor, caret to the end of line 6: {Sel()}");
            Check(ed.SelectedText == text[Off(3, 5)..End(6)], "Bug 77: the selected text runs from the anchor to the end of line 6 (no line break)");
            Select(Off(6, 9), Off(3, 5));
            Press(Key.End);
            Check(ed.SelectionStart == Off(6, 9) && ed.SelectionEnd == End(3) && ed.CaretIndex == End(3), $"Bug 77: upward selection (mid 6 -> mid 3): the caret moves to the end of line 3, the anchor stays: {Sel()}");
            Select(Off(5, 2), Off(5, 4));
            Press(Key.End);
            Check(ed.SelectionStart == Off(5, 2) && ed.SelectionEnd == End(5), $"Bug 77: single-line selection: Shift+End selects from the anchor to the line end: {Sel()}");
            Select(Off(5, 6), Off(5, 2));
            Press(Key.End);
            Check(ed.SelectionStart == Off(5, 6) && ed.SelectionEnd == End(5), $"Bug 77: single-line backward selection: the caret crosses the anchor to the line end: {Sel()}");
            Select(Off(8, 1), Off(8, 1));
            Press(Key.End);
            Check(ed.SelectionStart == Off(8, 1) && ed.SelectionEnd == text.Length, $"Bug 77: the last line (no terminator): Shift+End selects to the end of the text: {Sel()}");

            // Plain Home / End and Ctrl+Shift+Home still work as before.
            Select(Off(3, 5), Off(6, 9));
            Press(Key.End, RawInputModifiers.None);
            Check(ed.SelectionStart == ed.SelectionEnd && ed.CaretIndex == End(6), $"Plain End collapses to the caret line's end: {Sel()}");
            Select(Off(6, 9), Off(3, 5));
            Press(Key.Home, RawInputModifiers.None);
            Check(ed.SelectionStart == ed.SelectionEnd && ed.CaretIndex == Off(3, 0), $"Plain Home with an upward selection goes to the caret line's start: {Sel()}");
            Select(Off(3, 5), Off(6, 9));
            Press(Key.Home, RawInputModifiers.Shift | RawInputModifiers.Control);
            Check(Math.Min(ed.SelectionStart, ed.SelectionEnd) == 0, $"Ctrl+Shift+Home still selects to the document start: {Sel()}");
            doc.SavedText = doc.Text;
            Console.WriteLine($"Selection Home/End tests: {passed} passed");
        }
        finally { w.Close(); }
    }
}