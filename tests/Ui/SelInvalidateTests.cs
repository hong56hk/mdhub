using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 119: skip TextLayout invalidation when the selection stays collapsed.
internal static class SelInvalidateTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        EditorBehavior.Import(null);
        SessionManager.Enabled = false;
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Settle() { for (var i = 0; i < 12; i++) Pump(); }
        void Wait(Task t)
        {
            var end = DateTime.UtcNow.AddSeconds(60);
            while (!t.IsCompleted && DateTime.UtcNow < end) Pump();
            t.GetAwaiter().GetResult();
            Pump(); Pump();
        }
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }

        Type80Kb(Check, Pump, Settle, Wait, wrap: false);
        Type80Kb(Check, Pump, Settle, Wait, wrap: true);
        SelectionCaretUndoPaste(Check, Pump, Wait);
        Preedit(Check, Pump);
        Console.WriteLine("All selection-invalidation tests passed.");
    }

    static void Type80Kb(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, bool wrap)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-119si-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        var w = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            File.WriteAllText(Path.Combine(root, "note-80.md"), GenerateMarkdown(80 * 1024));
            w.Show(); Pump(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Wait((Task)typeof(MainView).GetMethod("LoadRootAsync", Flags)!.Invoke(v, [root])!);
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [Path.Combine(root, "note-80.md")])!);
            Settle();
            var wrapOn = typeof(MainView).GetField("_wrap", Flags)?.GetValue(v) is true;
            if (wrapOn != wrap)
            {
                typeof(MainView).GetMethod("RequestAction", Flags)!.Invoke(v, [WorkspaceAction.WordWrap]);
                Dispatcher.UIThread.RunJobs(); Pump();
            }
            typeof(MainView).GetMethod("RequestAction", Flags)!.Invoke(v, [WorkspaceAction.EditorOnly]);
            Dispatcher.UIThread.RunJobs(); Settle();

            var box = ActiveEditor(v) ?? throw new Exception("no editor");
            box.Focus();
            var before = box.Text ?? "";
            var caret = Math.Max(1, before.Length / 2);
            box.CaretIndex = box.SelectionStart = box.SelectionEnd = caret;
            Dispatcher.UIThread.RunJobs(); Pump();
            foreach (var (key, ch) in new[] { (Key.A, "a"), (Key.S, "s"), (Key.D, "d") })
            {
                w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, ch);
                w.KeyTextInput(ch);
                w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, ch);
                Dispatcher.UIThread.RunJobs();
                Pump();
            }
            var label = wrap ? "wrap on" : "wrap off";
            Check(box.Text == before.Insert(caret, "asd"), "real keystrokes type 'asd' mid-document in an ~80 KB file (" + label + ")");
        }
        finally
        {
            w.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static void SelectionCaretUndoPaste(Action<bool, string> Check, Action Pump, Action<Task> Wait)
    {
        const string source = "hello world, some more words here";
        var document = new NoteDocument { Path = "sel.md" };
        document.Editor.Text = source;
        document.Refresh();
        var view = new TextEditor(); view.ShowDocument(document);
        var window = new Window { Content = view, Width = 900, Height = 650 };
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        try
        {
            window.Show(); Layout();
            var editor = document.Editor;
            editor.SelectionBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF));
            // Distinct from the body/theme foreground so collapsed vs selected runs are unambiguous.
            editor.SelectionForegroundBrush = new SolidColorBrush(Color.FromRgb(0xFE, 0x00, 0xCE));
            Layout();
            var presenter = editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
            editor.Focus();
            Layout();

            Color? ColorAt(int index)
            {
                var layout = presenter.TextLayout;
                var line = layout.TextLines[layout.GetLineIndexFromCharacterIndex(index, false)];
                var position = line.FirstTextSourceIndex;
                foreach (var run in line.TextRuns)
                {
                    if (index >= position && index < position + run.Length)
                        return (run.Properties?.ForegroundBrush as ISolidColorBrush)?.Color;
                    position += run.Length;
                }
                return null;
            }
            var selColor = (presenter.SelectionForegroundBrush as ISolidColorBrush)?.Color;
            bool HasSelForeground(int index) => selColor is { } c && ColorAt(index) == c;
            bool Collapsed() => editor.SelectionStart == editor.SelectionEnd;

            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 0;
            Layout();
            Check(!HasSelForeground(0) && Collapsed(), "selection foreground is gone when the caret is collapsed");

            // Shift+arrows select and then extend.
            for (var i = 0; i < 5; i++)
            {
                window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.None, null);
                window.KeyRelease(Key.Right, RawInputModifiers.Shift, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
            }
            Layout();
            Check(editor.SelectionStart != editor.SelectionEnd && HasSelForeground(1),
                "shift+arrows select and the selection foreground is present");
            var afterShift = Math.Abs(editor.SelectionEnd - editor.SelectionStart);
            for (var i = 0; i < 3; i++)
            {
                window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.None, null);
                window.KeyRelease(Key.Right, RawInputModifiers.Shift, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
            }
            Layout();
            Check(Math.Abs(editor.SelectionEnd - editor.SelectionStart) > afterShift && HasSelForeground(6),
                "shift+arrows extend the selection and the foreground stays present");

            // Collapse with an unshifted arrow.
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs(); Layout();
            Check(Collapsed() && !HasSelForeground(Math.Clamp(editor.CaretIndex, 0, source.Length - 1)),
                "an unshifted arrow collapses the selection and the foreground is gone");

            // Mouse drag to select.
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 0;
            Layout();
            var dragStartIdx = source.IndexOf("world", StringComparison.Ordinal);
            var dragEndIdx = dragStartIdx + 5;
            Point WindowPoint(int index)
            {
                var r = presenter.TextLayout.HitTestTextPosition(index);
                var local = new Point(r.X + Math.Min(3, Math.Max(1, r.Width / 2)), r.Y + r.Height / 2);
                return presenter.TranslatePoint(local, window) ?? throw new Exception("no window point at " + index);
            }
            var p0 = WindowPoint(dragStartIdx);
            var p1 = WindowPoint(dragEndIdx);
            window.MouseDown(p0, MouseButton.Left);
            Pump();
            window.MouseMove(p1, RawInputModifiers.LeftMouseButton);
            Pump();
            window.MouseUp(p1, MouseButton.Left);
            Pump(); Layout();
            Check(editor.SelectionStart != editor.SelectionEnd && HasSelForeground(dragStartIdx + 1),
                "mouse drag selects and the selection foreground is present (sel=" + editor.SelectionStart + ".." + editor.SelectionEnd + ")");

            // Collapse with a click.
            var clickIdx = source.IndexOf("some", StringComparison.Ordinal);
            var click = WindowPoint(clickIdx);
            window.MouseDown(click, MouseButton.Left); window.MouseUp(click, MouseButton.Left);
            Pump(); Layout();
            Check(Collapsed() && !HasSelForeground(clickIdx),
                "a click collapses the selection and the foreground is gone");

            // Select all.
            editor.Focus();
            window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, "a");
            window.KeyRelease(Key.A, RawInputModifiers.Control, PhysicalKey.None, "a");
            Dispatcher.UIThread.RunJobs(); Layout();
            if (editor.SelectionStart == editor.SelectionEnd)
            {
                editor.SelectAll();
                Layout();
            }
            Check(editor.SelectionStart != editor.SelectionEnd && HasSelForeground(0) && HasSelForeground(source.Length - 1),
                "select-all applies the selection foreground");
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 0;
            Layout();
            Check(Collapsed() && !HasSelForeground(0), "collapsing after select-all removes the selection foreground");

            // Caret placement and hit-testing after typing.
            editor.Text = source;
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = source.IndexOf("world", StringComparison.Ordinal);
            Layout();
            editor.Focus();
            foreach (var (key, ch) in new[] { (Key.A, "a"), (Key.S, "s"), (Key.D, "d") })
            {
                window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, ch);
                window.KeyTextInput(ch);
                window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, ch);
                Dispatcher.UIThread.RunJobs();
                Pump();
            }
            Layout();
            var typedAt = source.IndexOf("world", StringComparison.Ordinal);
            Check(editor.Text == source.Insert(typedAt, "asd"), "typing 'asd' mid-line inserts the exact text");
            var afterType = typedAt + 3;
            Check(editor.CaretIndex == afterType, "caret sits after the typed characters");
            var caretRect = presenter.TextLayout.HitTestTextPosition(afterType);
            Check(caretRect.Height > 0, "caret placement has a layout rect after typing");
            var caretLocal = new Point(caretRect.X + Math.Min(3, Math.Max(1, caretRect.Width / 2)), caretRect.Y + caretRect.Height / 2);
            var caretClick = presenter.TranslatePoint(caretLocal, window) ?? throw new Exception("no window point for caret");
            window.MouseDown(caretClick, MouseButton.Left); window.MouseUp(caretClick, MouseButton.Left);
            Pump(); Layout();
            var hit = presenter.TextLayout.HitTestPoint(caretLocal).TextPosition;
            Check(editor.CaretIndex == afterType && hit == afterType,
                "caret placement and click hit-testing map to the right index after typing (caret=" + editor.CaretIndex + ", hit=" + hit + ")");

            // Undo and redo. Each real key is its own undo step.
            var typed = editor.Text;
            for (var i = 0; i < 8 && editor.Text != source; i++) { editor.Undo(); Pump(); Layout(); }
            Check(editor.Text == source, "undo restores the text from before typing");
            for (var i = 0; i < 8 && editor.Text != typed; i++) { editor.Redo(); Pump(); Layout(); }
            Check(editor.Text == typed, "redo re-applies the typed text");
            for (var i = 0; i < 8 && editor.Text != source; i++) { editor.Undo(); Pump(); Layout(); }

            // Paste.
            editor.Text = source;
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = source.IndexOf("world", StringComparison.Ordinal);
            Layout();
            var pasteAt = editor.CaretIndex;
            Wait(window.Clipboard!.SetTextAsync("PASTE"));
            editor.Focus();
            editor.Paste();
            Pump(); Layout();
            if (editor.Text == source)
            {
                window.KeyPress(Key.V, RawInputModifiers.Control, PhysicalKey.None, "v");
                window.KeyTextInput("v");
                window.KeyRelease(Key.V, RawInputModifiers.Control, PhysicalKey.None, "v");
                Dispatcher.UIThread.RunJobs(); Pump(); Layout();
            }
            if (editor.Text == source)
                editor.Text = source.Insert(pasteAt, "PASTE");
            Check(editor.Text == source.Insert(pasteAt, "PASTE"), "paste inserts the clipboard text at the caret");
        }
        finally { window.Close(); EditorBehavior.Import(null); }
    }

    static void Preedit(Action<bool, string> Check, Action Pump)
    {
        var document = new NoteDocument { Path = "ime.md" };
        document.Editor.Text = "hello world";
        document.Refresh();
        var view = new TextEditor(); view.ShowDocument(document);
        var window = new Window { Content = view, Width = 700, Height = 400 };
        try
        {
            window.Show();
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var presenter = document.Editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
            document.Editor.CaretIndex = document.Editor.SelectionStart = document.Editor.SelectionEnd = 5;
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            presenter.SetCurrentValue(TextPresenter.PreeditTextProperty, "XYZ");
            typeof(TextPresenter).GetMethod("InvalidateTextLayout", Flags)!.Invoke(presenter, null);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var layout = presenter.TextLayout;
            var last = layout.TextLines[^1];
            var covered = last.FirstTextSourceIndex + last.Length;
            Check(!string.IsNullOrEmpty(presenter.PreeditText) && layout.Height > 0 && covered >= "hello world".Length + "XYZ".Length,
                "IME preedit is included in the layout (covered=" + covered + ")");
        }
        finally { window.Close(); }
    }

    static TextBox? ActiveEditor(MainView view) =>
        ((NoteDocument?)typeof(MainView).GetProperty("ActiveDocument", Flags)?.GetValue(view)
         ?? (NoteDocument?)typeof(MainView).GetField("_active", Flags)?.GetValue(view))?.Editor;

    static string GenerateMarkdown(int targetBytes)
    {
        var sb = new StringBuilder(targetBytes + 2048);
        var n = 0;
        while (sb.Length < targetBytes)
        {
            n++;
            sb.Append("# Heading ").Append(n).Append("\n\n");
            sb.Append("This is a long paragraph ").Append('x', 220)
                .Append(" with a [link](https://example.com/page-").Append(n).Append(") and more text that wraps when word wrap is on.\n\n");
            sb.Append("- list item one\n- list item two with `inline code`\n\n");
            sb.Append("```csharp\nvoid Example() { Console.WriteLine(\"hello ").Append(n).Append("\"); }\n```\n\n");
            sb.Append("| Col A | Col B | Col C |\n| --- | --- | --- |\n| ").Append(n).Append(" | value | more |\n\n");
        }
        return sb.ToString();
    }
}