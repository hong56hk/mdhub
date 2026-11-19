using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.Utilities;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Text;
using Mdhub.Views;

// Item 118: CreateTextLayout no longer builds a throwaway stock layout first.
internal static class LayoutBaseTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly FieldInfo ConstraintField = typeof(TextPresenter).GetField("_constraint", Flags)!;

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
        HighlightSelectionCaretAndStock(Check, Pump);
        Preedit(Check, Pump);
        Console.WriteLine("All layout-base tests passed.");
    }

    static void Type80Kb(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, bool wrap)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-118lb-" + Guid.NewGuid().ToString("N")[..8])).FullName;
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

    static void HighlightSelectionCaretAndStock(Action<bool, string> Check, Action Pump)
    {
        const string source = "# Heading\n\nA **bold** word and `inline` code.\n";
        var document = new NoteDocument { Path = "layout.md" };
        document.Editor.Text = source;
        document.Refresh();
        var view = new TextEditor(); view.ShowDocument(document);
        var window = new Window { Content = view, Width = 900, Height = 650 };
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        try
        {
            window.Show(); Layout();
            var presenter = document.Editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
            var editor = document.Editor;

            // Highlighting styles are still applied.
            TextRunProperties? RunAt(int index)
            {
                var line = presenter.TextLayout.TextLines[presenter.TextLayout.GetLineIndexFromCharacterIndex(index, false)];
                var position = line.FirstTextSourceIndex;
                foreach (var run in line.TextRuns)
                {
                    if (index >= position && index < position + run.Length) return run.Properties;
                    position += run.Length;
                }
                return null;
            }
            var heading = RunAt(source.IndexOf("Heading", StringComparison.Ordinal));
            var bold = RunAt(source.IndexOf("bold", StringComparison.Ordinal));
            var inline = RunAt(source.IndexOf("inline", StringComparison.Ordinal));
            var headingColor = (heading?.ForegroundBrush as ISolidColorBrush)?.Color;
            var bodyColor = (editor.Foreground as ISolidColorBrush)?.Color;
            Check(heading is not null && headingColor is not null && headingColor != bodyColor,
                "highlighting styles are still applied (heading colour)");
            Check(bold is not null && bold.Typeface.Weight == FontWeight.Bold, "highlighting styles are still applied (bold)");
            Check(inline?.BackgroundBrush is not null, "highlighting styles are still applied (inline code background)");

            // Isolated TextEditor host may not yet have theme selection brushes; set them so the presenter binds them.
            editor.SelectionBrush ??= new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF));
            editor.SelectionForegroundBrush ??= Brushes.White;
            Layout();
            // Selection rendering: foreground override in the layout, and the selection brush is present.
            var start = source.IndexOf("bold", StringComparison.Ordinal);
            editor.SelectionStart = start;
            editor.SelectionEnd = start + 4;
            editor.CaretIndex = start + 4;
            Layout();
            Check(presenter.SelectionBrush is not null && presenter.SelectionForegroundBrush is not null,
                "selection brush and selection foreground are present");
            var selected = RunAt(start + 1);
            var expectedSel = (presenter.SelectionForegroundBrush as ISolidColorBrush)?.Color;
            var selectedColor = (selected?.ForegroundBrush as ISolidColorBrush)?.Color;
            Check(selected is not null && expectedSel is not null && selectedColor == expectedSel,
                "selection foreground is applied to the selected text");
            var range = presenter.TextLayout.HitTestTextRange(start, 4).ToList();
            Check(range.Count > 0 && range.All(r => r.Width > 0 && r.Height > 0),
                "selection range maps to visible layout rectangles");

            editor.SelectionStart = editor.SelectionEnd = editor.CaretIndex = start;
            Layout();

            foreach (var wrap in new[] { TextWrapping.NoWrap, TextWrapping.Wrap })
            {
                editor.TextWrapping = wrap;
                Layout();
                var label = wrap == TextWrapping.Wrap ? "wrap on" : "wrap off";
                var index = source.IndexOf("word", StringComparison.Ordinal);
                editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = index;
                Layout();
                var caretRect = presenter.TextLayout.HitTestTextPosition(index);
                Check(caretRect.Height > 0, "caret placement has a layout rect (" + label + ")");
                var clickLocal = new Point(caretRect.X + Math.Min(3, Math.Max(1, caretRect.Width / 2)), caretRect.Y + caretRect.Height / 2);
                var click = presenter.TranslatePoint(clickLocal, window) ?? throw new Exception("no window point for caret (" + label + ")");
                window.MouseDown(click, MouseButton.Left); window.MouseUp(click, MouseButton.Left);
                Pump(); Layout();
                var hit = presenter.TextLayout.HitTestPoint(clickLocal).TextPosition;
                Check(editor.CaretIndex == index && hit == index,
                    "caret placement and click hit-testing map to the right index (" + label + ", caret=" + editor.CaretIndex + ", hit=" + hit + ")");
            }

            // Width/height match a stock TextPresenter for the same text and constraint (unstyled, so typography is identical).
            CompareWithStock(Check, "plain line without wrapping markers", TextWrapping.NoWrap);
            CompareWithStock(Check, "plain line without wrapping markers", TextWrapping.Wrap);
            CompareWithStock(Check, "a somewhat longer paragraph that will wrap when the width is narrow enough to fold", TextWrapping.NoWrap);
            CompareWithStock(Check, "a somewhat longer paragraph that will wrap when the width is narrow enough to fold", TextWrapping.Wrap);
        }
        finally { window.Close(); EditorBehavior.Import(null); }
    }

    static void CompareWithStock(Action<bool, string> Check, string text, TextWrapping wrap)
    {
        var ours = new MarkdownTextPresenter
        {
            HighlightMarkdown = false,
            Text = text,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Foreground = Brushes.Black,
            TextWrapping = wrap,
            Width = 400,
            Height = 200
        };
        var stock = new TextPresenter
        {
            Text = text,
            FontFamily = ours.FontFamily,
            FontSize = 14,
            Foreground = Brushes.Black,
            TextWrapping = wrap,
            Width = 400,
            Height = 200
        };
        var host = new Window { Content = new Grid { Children = { ours, stock } }, Width = 500, Height = 300 };
        try
        {
            host.Show();
            host.UpdateLayout(); Dispatcher.UIThread.RunJobs(); host.UpdateLayout();

            foreach (var constraint in new[] { new Size(240, 200), new Size(0, 0), new Size(800, 0) })
            {
                foreach (var presenter in new TextPresenter[] { ours, stock })
                {
                    ConstraintField.SetValue(presenter, constraint);
                    typeof(TextPresenter).GetMethod("InvalidateTextLayout", Flags)!.Invoke(presenter, null);
                }
                var a = ours.TextLayout;
                var b = stock.TextLayout;
                var label = $"wrap={(wrap == TextWrapping.Wrap ? "on" : "off")} constraint={Fmt(constraint)}";
                Check(Same(a.MaxWidth, b.MaxWidth) && Same(a.MaxHeight, b.MaxHeight),
                    "layout MaxWidth/MaxHeight match the stock presenter (" + label + $": ours={a.MaxWidth}x{a.MaxHeight}, stock={b.MaxWidth}x{b.MaxHeight})");
                Check(Same(a.WidthIncludingTrailingWhitespace, b.WidthIncludingTrailingWhitespace) && Same(a.Height, b.Height),
                    "layout width and height are identical to the stock presenter (" + label
                    + $": ours={a.WidthIncludingTrailingWhitespace:F2}x{a.Height:F2}, stock={b.WidthIncludingTrailingWhitespace:F2}x{b.Height:F2})");
                if (IsZero(constraint.Width))
                    Check(double.IsPositiveInfinity(a.MaxWidth), "zero constraint width means infinity (" + label + ")");
                if (IsZero(constraint.Height))
                    Check(double.IsPositiveInfinity(a.MaxHeight), "zero constraint height means infinity (" + label + ")");
            }
        }
        finally { host.Close(); }
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
                "IME preedit is included in the stock preedit layout (covered=" + covered + ")");
            var underlined = layout.TextLines.SelectMany(l => l.TextRuns)
                .Any(r => r.Properties?.TextDecorations == TextDecorations.Underline && (r.Text.ToString().Contains("XYZ") || r.Length > 0));
            Check(underlined || layout.TextLines.Sum(l => l.Length) >= "hello world".Length + "XYZ".Length,
                "IME preedit layout is built (underline or combined length)");
        }
        finally { window.Close(); }
    }

    static TextBox? ActiveEditor(MainView view) =>
        ((NoteDocument?)typeof(MainView).GetProperty("ActiveDocument", Flags)?.GetValue(view)
         ?? (NoteDocument?)typeof(MainView).GetField("_active", Flags)?.GetValue(view))?.Editor;

    static bool IsZero(double value) => Math.Abs(value) < 10.0 * 2.2204460492503131e-016;

    static bool Same(double a, double b) =>
        double.IsPositiveInfinity(a) && double.IsPositiveInfinity(b)
        || Math.Abs(a - b) < 0.01;

    static string Fmt(Size s) =>
        $"{(double.IsInfinity(s.Width) ? "inf" : s.Width.ToString("F0"))}x{(double.IsInfinity(s.Height) ? "inf" : s.Height.ToString("F0"))}";

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