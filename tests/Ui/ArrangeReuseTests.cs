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

// Item 122: ArrangeOverride reuses a still-valid NoWrap TextLayout when arrange width would not change line breaking.
internal static class ArrangeReuseTests
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
        EditorKeysSelectionCaretAndChanges(Check, Pump, Wait);
        Preedit(Check, Pump);
        Console.WriteLine("All arrange-reuse tests passed.");
    }

    static void Type80Kb(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, bool wrap)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-122ar-" + Guid.NewGuid().ToString("N")[..8])).FullName;
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
            TypeKeys(w, Pump, (Key.A, "a"), (Key.S, "s"), (Key.D, "d"));
            var label = wrap ? "wrap on" : "wrap off";
            Check(box.Text == before.Insert(caret, "asd"), "real keystrokes type 'asd' mid-document in an ~80 KB file (" + label + ")");
        }
        finally
        {
            w.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static void EditorKeysSelectionCaretAndChanges(Action<bool, string> Check, Action Pump, Action<Task> Wait)
    {
        const string source = "hello world, some more words here";
        var document = new NoteDocument { Path = "measure.md" };
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

            // Enter, Backspace, Delete
            editor.Text = "ab";
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 1;
            Layout();
            editor.Focus();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Dispatcher.UIThread.RunJobs(); Pump(); Layout();
            Check(editor.Text is "a\nb" or "a\r\nb", "Enter inserts a line break (text=" + Show(editor.Text) + ")");

            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            Dispatcher.UIThread.RunJobs(); Pump(); Layout();
            Check(editor.Text == "ab", "Backspace deletes the line break");

            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 1;
            Layout();
            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            window.KeyRelease(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
            Dispatcher.UIThread.RunJobs(); Pump(); Layout();
            Check(editor.Text == "a", "Delete removes the character after the caret");

            // Paste, undo, redo
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
                window.KeyRelease(Key.V, RawInputModifiers.Control, PhysicalKey.None, "v");
                Dispatcher.UIThread.RunJobs(); Pump(); Layout();
            }
            Check(editor.Text == source.Insert(pasteAt, "PASTE"), "paste inserts the clipboard text at the caret");
            var pasted = editor.Text;
            editor.Undo(); Pump(); Layout();
            Check(editor.Text == source, "undo restores the text from before paste");
            editor.Redo(); Pump(); Layout();
            Check(editor.Text == pasted, "redo re-applies the pasted text");
            editor.Undo(); Pump(); Layout();

            // Selection rendering
            editor.Text = source;
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 0;
            Layout();
            Check(!HasSelForeground(0), "selection foreground is gone when the caret is collapsed");
            for (var i = 0; i < 5; i++)
            {
                window.KeyPress(Key.Right, RawInputModifiers.Shift, PhysicalKey.None, null);
                window.KeyRelease(Key.Right, RawInputModifiers.Shift, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
            }
            Layout();
            Check(editor.SelectionStart != editor.SelectionEnd && HasSelForeground(1),
                "shift+arrows select and the selection foreground is present");
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = 0;
            Layout();

            // Caret placement and click hit-testing after typing
            editor.Text = source;
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = source.IndexOf("world", StringComparison.Ordinal);
            Layout();
            editor.Focus();
            TypeKeys(window, Pump, (Key.A, "a"), (Key.S, "s"), (Key.D, "d"));
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

            // Resize with wrap on and off: text is unchanged and a layout still exists.
            editor.Text = string.Join(' ', Enumerable.Repeat("wrapping text", 80));
            foreach (var wrap in new[] { TextWrapping.NoWrap, TextWrapping.Wrap })
            {
                editor.TextWrapping = wrap;
                Layout();
                var beforeHeight = presenter.TextLayout.Height;
                window.Width = wrap == TextWrapping.Wrap ? 420 : 1100;
                Layout(); Pump(); Layout();
                var label = wrap == TextWrapping.Wrap ? "wrap on" : "wrap off";
                Check(editor.Text.Contains("wrapping text") && presenter.TextLayout.Height > 0,
                    "resizing the window keeps the text and a layout (" + label + ")");
                if (wrap == TextWrapping.Wrap)
                    Check(presenter.TextLayout.Height >= beforeHeight - 0.5,
                        "wrap-on resize still produces a wrapped layout (h=" + presenter.TextLayout.Height.ToString("F1") + ")");
            }

            // Wrap toggle, theme switch, font size change, tab switch
            var longText = string.Join(' ', Enumerable.Repeat("toggle wrap words", 60));
            editor.Text = longText;
            editor.TextWrapping = TextWrapping.NoWrap;
            Layout();
            var noWrapHeight = presenter.TextLayout.Height;
            editor.TextWrapping = TextWrapping.Wrap;
            window.Width = 500;
            Layout(); Pump(); Layout();
            Check(editor.Text == longText && presenter.TextLayout.Height > noWrapHeight,
                "wrap toggle rebuilds the layout and keeps the text");

            var headingDoc = "# Heading\n\nA **bold** word.";
            editor.Text = headingDoc;
            editor.TextWrapping = TextWrapping.NoWrap;
            window.Width = 900;
            Layout();
            ISolidColorBrush? HeadingBrush()
            {
                var index = (editor.Text ?? "").IndexOf("Heading", StringComparison.Ordinal);
                if (index < 0) return null;
                var layout = presenter.TextLayout;
                var line = layout.TextLines[layout.GetLineIndexFromCharacterIndex(index, false)];
                var position = line.FirstTextSourceIndex;
                foreach (var run in line.TextRuns)
                {
                    if (index >= position && index < position + run.Length)
                        return run.Properties?.ForegroundBrush as ISolidColorBrush;
                    position += run.Length;
                }
                return null;
            }
            var previousTheme = ThemeService.Current;
            var beforeTheme = HeadingBrush()?.Color;
            ThemeService.Apply(previousTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark, persist: false);
            Layout(); Pump(); Layout();
            var afterTheme = HeadingBrush()?.Color;
            Check(editor.Text == headingDoc && afterTheme is not null && afterTheme != beforeTheme,
                "theme switch keeps the text and reapplies highlighting");
            ThemeService.Apply(previousTheme, persist: false);
            Layout(); Pump(); Layout();

            var fontBefore = editor.FontSize;
            var heightBefore = presenter.TextLayout.Height;
            editor.FontSize = fontBefore + 6;
            Layout(); Pump(); Layout();
            Check(editor.Text == headingDoc && presenter.TextLayout.Height > heightBefore,
                "font size change rebuilds the layout and keeps the text");
            editor.FontSize = fontBefore;
            Layout();

            var other = new NoteDocument { Path = "other.md" };
            other.Editor.Text = "the other tab's unique text";
            view.ShowDocument(other);
            Layout(); Pump(); Layout();
            Check(other.Editor.Text == "the other tab's unique text"
                  && other.Editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single().TextLayout.Height > 0,
                "tab switch shows the other document's layout");
            view.ShowDocument(document);
            Layout(); Pump(); Layout();
            Check(document.Editor.Text == headingDoc, "switching back restores the previous document's text");
        }
        finally
        {
            window.Close();
            ThemeService.Apply(AppTheme.Dark, persist: false);
            EditorBehavior.Import(null);
        }
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

    static void TypeKeys(Window w, Action Pump, params (Key key, string ch)[] keys)
    {
        foreach (var (key, ch) in keys)
        {
            w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, ch);
            w.KeyTextInput(ch);
            w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, ch);
            Dispatcher.UIThread.RunJobs();
            Pump();
        }
    }

    static string Show(string? text) => (text ?? "").Replace("\r", "\\r").Replace("\n", "\\n");

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