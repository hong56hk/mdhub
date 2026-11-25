using System.Globalization;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 126: the gutter numbers only the lines in the viewport, from the current layout.
internal static class GutterViewportTests
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

        Type80Kb(Check, Pump, Settle, Wait, previewVisible: true);
        Type80Kb(Check, Pump, Settle, Wait, previewVisible: false);
        using (var session = Open(GenerateMarkdown(80 * 1024), Pump, Settle, Wait, previewVisible: true, wrap: true))
        {
            MatchWalkAndBound(session, Check, Pump, "80 KB wrap on, top");
            ScrollTo(session, Pump, 0.5);
            MatchWalkAndBound(session, Check, Pump, "80 KB wrap on, middle");
            ScrollTo(session, Pump, 1);
            MatchWalkAndBound(session, Check, Pump, "80 KB wrap on, bottom");
        }
        using (var session = Open(GenerateMarkdown(80 * 1024), Pump, Settle, Wait, previewVisible: true, wrap: false))
        {
            MatchWalkAndBound(session, Check, Pump, "80 KB wrap off, top");
            ScrollTo(session, Pump, 0.5);
            MatchWalkAndBound(session, Check, Pump, "80 KB wrap off, middle");
            ScrollTo(session, Pump, 1);
            MatchWalkAndBound(session, Check, Pump, "80 KB wrap off, bottom");
        }
        InsertAndDelete(Check, Pump, Settle, Wait);
        EmptyAndOneLine(Check, Pump, Settle, Wait);
        FontSizeChange(Check, Pump, Settle, Wait);
        Console.WriteLine("All gutter-viewport tests passed.");
    }

    static void Type80Kb(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, bool previewVisible)
    {
        using var session = Open(GenerateMarkdown(80 * 1024), Pump, Settle, Wait, previewVisible, wrap: true);
        var box = session.Editor;
        box.Focus();
        var before = box.Text ?? "";
        var caret = Math.Max(1, before.Length / 2);
        box.CaretIndex = box.SelectionStart = box.SelectionEnd = caret;
        Dispatcher.UIThread.RunJobs(); Pump();
        TypeKeys(session.Window, Pump, (Key.A, "a"), (Key.S, "s"), (Key.D, "d"));
        var label = previewVisible ? "preview open (Split, wrap on)" : "preview closed";
        Check(box.Text == before.Insert(caret, "asd"), "real keystrokes type 'asd' mid-document in an ~80 KB file (" + label + ")");
    }

    static void InsertAndDelete(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait)
    {
        using var session = Open(GenerateMarkdown(80 * 1024), Pump, Settle, Wait, previewVisible: true, wrap: true);
        var box = session.Editor;
        var mid = Math.Max(1, (box.Text ?? "").IndexOf("Heading 10", StringComparison.Ordinal));
        if (mid < 1) mid = (box.Text ?? "").Length / 2;
        box.CaretIndex = box.SelectionStart = box.SelectionEnd = mid;
        box.Text = (box.Text ?? "").Insert(mid, "\n\ninserted line\n\n");
        Dispatcher.UIThread.RunJobs(); Pump(); Pump();
        ScrollTo(session, Pump, 0);
        MatchWalkAndBound(session, Check, Pump, "after inserting lines, top");
        ScrollTo(session, Pump, 0.5);
        MatchWalkAndBound(session, Check, Pump, "after inserting lines, middle");
        var text = box.Text ?? "";
        var heading = text.IndexOf("Heading 8", StringComparison.Ordinal);
        if (heading >= 0)
        {
            var (start, end) = LineRange(text, heading);
            box.Text = text.Remove(start, end - start);
            Dispatcher.UIThread.RunJobs(); Pump(); Pump();
        }
        ScrollTo(session, Pump, 0);
        MatchWalkAndBound(session, Check, Pump, "after deleting a line, top");
        ScrollTo(session, Pump, 0.5);
        MatchWalkAndBound(session, Check, Pump, "after deleting a line, middle");
        ScrollTo(session, Pump, 1);
        MatchWalkAndBound(session, Check, Pump, "after deleting a line, bottom");
    }

    static void EmptyAndOneLine(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait)
    {
        using var session = Open("", Pump, Settle, Wait, previewVisible: true, wrap: true);
        Pump(); Pump();
        var empty = session.Gutter.VisibleNumbers();
        var emptyWalk = FullWalk(session);
        Check(empty.Select(n => n.Number).SequenceEqual(emptyWalk.Select(n => n.Number)),
            "empty document: numbers match a full walk (count=" + empty.Count + ")");
        Check(session.Gutter.LineCount == 1, "empty document still has one logical line");
        ForceRender(session, Pump);
        Check(Drawn(session.Gutter) <= 2, "empty document draws at most a couple of numbers (drawn=" + Drawn(session.Gutter) + ")");

        session.Editor.Text = "only one line";
        Dispatcher.UIThread.RunJobs(); Pump(); Pump();
        var one = session.Gutter.VisibleNumbers();
        var oneWalk = FullWalk(session);
        Check(one.Select(n => n.Number).SequenceEqual([1]) && one.Select(n => n.Number).SequenceEqual(oneWalk.Select(n => n.Number)),
            "one-line document draws number 1 and matches a full walk");
        ForceRender(session, Pump);
        Check(Drawn(session.Gutter) <= 2, "one-line document draws at most a couple of numbers (drawn=" + Drawn(session.Gutter) + ")");
    }

    static void FontSizeChange(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait)
    {
        using var session = Open(GenerateMarkdown(80 * 1024), Pump, Settle, Wait, previewVisible: true, wrap: true);
        ScrollTo(session, Pump, 0.5);
        var before = session.Editor.FontSize;
        session.Editor.FontSize = before + 6;
        Dispatcher.UIThread.RunJobs(); Pump(); Pump();
        MatchWalkAndBound(session, Check, Pump, "after a font size increase, middle");
        session.Editor.FontSize = before;
        Dispatcher.UIThread.RunJobs(); Pump(); Pump();
        MatchWalkAndBound(session, Check, Pump, "after restoring font size, middle");
    }

    static void MatchWalkAndBound(Session session, Action<bool, string> Check, Action Pump, string label)
    {
        Pump(); Pump();
        var visible = session.Gutter.VisibleNumbers();
        var walk = FullWalk(session);
        var visibleNums = visible.Select(n => n.Number).ToList();
        var walkNums = walk.Select(n => n.Number).ToList();
        Check(visibleNums.SequenceEqual(walkNums),
            label + ": numbers match a full walk (" + Describe(visibleNums) + " vs " + Describe(walkNums) + ")");
        Check(TopsClose(visible, walk), label + ": tops match a full walk");
        Check(visible.Count == 0 || visible.Zip(visible.Skip(1)).All(p => p.Second.Top > p.First.Top),
            label + ": numbers are ordered top to bottom");
        ForceRender(session, Pump);
        var drawn = Drawn(session.Gutter);
        var cap = ViewportCap(session);
        Check(drawn <= cap, label + ": lines drawn are bounded by the viewport, not document length (drawn=" + drawn + ", cap=" + cap + ", lines=" + session.Gutter.LineCount + ")");
        Check(session.Gutter.LineCount > cap * 2 || label.Contains("empty") || label.Contains("one-line") || session.Gutter.LineCount <= cap,
            label + ": document is long enough that a viewport bound is meaningful, or the document is short");
        if (session.Gutter.LineCount > cap * 2)
            Check(drawn < session.Gutter.LineCount, label + ": drawn count is less than the document length");
    }

    static string Describe(IReadOnlyList<int> nums)
    {
        if (nums.Count == 0) return "none";
        return nums[0] + ".." + nums[^1] + " (" + nums.Count + ")";
    }

    static bool TopsClose(IReadOnlyList<(int Number, double Top)> a, IReadOnlyList<(int Number, double Top)> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].Number != b[i].Number || Math.Abs(a[i].Top - b[i].Top) > 0.5) return false;
        return true;
    }

    // The previous O(n) VisibleNumbers algorithm, used as the oracle for the viewport walk.
    static List<(int Number, double Top)> FullWalk(Session session)
    {
        var gutter = session.Gutter;
        var editor = session.Editor;
        var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().First();
        var layout = presenter.TextLayout;
        var result = new List<(int, double)>();
        var text = editor.Text ?? "";
        var number = 1;
        var scanned = 0;
        double y = 0;
        foreach (var line in layout.TextLines)
        {
            var start = line.FirstTextSourceIndex;
            for (; scanned < Math.Min(start, text.Length); scanned++)
                if (text[scanned] == '\n' || (text[scanned] == '\r' && (scanned + 1 >= text.Length || text[scanned + 1] != '\n'))) number++;
            var logicalStart = start == 0 || (start <= text.Length && start > 0 && (text[start - 1] == '\n' || text[start - 1] == '\r'));
            if (logicalStart)
            {
                var top = presenter.TranslatePoint(new Point(0, y), gutter)?.Y;
                if (top is { } t && t + line.Height >= 0 && t <= gutter.Bounds.Height) result.Add((number, t));
            }
            y += line.Height;
        }
        return result;
    }

    static void ForceRender(Session session, Action Pump)
    {
        session.Gutter.InvalidateVisual();
        session.Editor.InvalidateVisual();
        Dispatcher.UIThread.RunJobs(); Pump();
        var w = Math.Max(1, (int)Math.Ceiling(session.Gutter.Bounds.Width));
        var h = Math.Max(1, (int)Math.Ceiling(session.Gutter.Bounds.Height));
        using var bmp = new RenderTargetBitmap(new PixelSize(w, h));
        bmp.Render(session.Gutter);
    }

    static int Drawn(LineNumberGutter gutter) =>
        (int)typeof(LineNumberGutter).GetProperty("LastDrawnCount", Flags)!.GetValue(gutter)!;

    static int ViewportCap(Session session)
    {
        var height = Math.Max(session.Gutter.Bounds.Height, 1);
        var presenter = session.Editor.GetVisualDescendants().OfType<TextPresenter>().First();
        var lineHeight = presenter.TextLayout.TextLines.Count > 0
            ? Math.Max(1.0, presenter.TextLayout.TextLines.Min(l => l.Height))
            : Math.Max(1.0, session.Editor.FontSize * 1.2);
        // Visible logical lines plus one-line margin on each side, plus a couple of partial lines.
        return (int)Math.Ceiling(height / lineHeight) + 4;
    }

    static void ScrollTo(Session session, Action Pump, double fraction)
    {
        var scroll = session.Editor.GetVisualDescendants().OfType<ScrollViewer>().First();
        var max = Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height);
        scroll.Offset = new Vector(scroll.Offset.X, max * Math.Clamp(fraction, 0, 1));
        Dispatcher.UIThread.RunJobs(); Pump(); Pump();
    }

    static (int Start, int End) LineRange(string text, int caret)
    {
        var start = caret;
        while (start > 0 && text[start - 1] is not ('\n' or '\r')) start--;
        var end = caret;
        while (end < text.Length && text[end] is not ('\n' or '\r')) end++;
        if (end < text.Length && text[end] == '\r') end++;
        if (end < text.Length && text[end] == '\n') end++;
        return (start, end);
    }

    static Session Open(string markdown, Action Pump, Action Settle, Action<Task> Wait, bool previewVisible, bool wrap)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-126gv-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        File.WriteAllText(Path.Combine(root, "note.md"), markdown);
        var w = new MainWindow { Width = 1280, Height = 800 };
        w.Show(); Pump(); Pump();
        var v = w.GetVisualDescendants().OfType<MainView>().Single();
        Wait((Task)typeof(MainView).GetMethod("LoadRootAsync", Flags)!.Invoke(v, [root])!);
        Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [Path.Combine(root, "note.md")])!);
        Settle();
        var wrapOn = typeof(MainView).GetField("_wrap", Flags)?.GetValue(v) is true;
        if (wrapOn != wrap)
        {
            Request(v, WorkspaceAction.WordWrap);
            Pump();
        }
        Request(v, previewVisible ? WorkspaceAction.SplitView : WorkspaceAction.EditorOnly);
        var until = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < until) Pump();
        var box = ActiveEditor(v) ?? throw new Exception("no editor");
        var gutter = v.GetVisualDescendants().OfType<LineNumberGutter>().FirstOrDefault()
            ?? throw new Exception("no LineNumberGutter");
        return new Session(w, v, box, gutter, root);
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

    static void Request(MainView v, WorkspaceAction action)
    {
        typeof(MainView).GetMethod("RequestAction", Flags)!.Invoke(v, [action]);
        Dispatcher.UIThread.RunJobs();
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
            sb.Append("| Col A | Col B | Col C |\n| --- | --- |\n| ").Append(n).Append(" | value | more |\n\n");
        }
        return sb.ToString();
    }

    sealed class Session(Window window, MainView view, TextBox editor, LineNumberGutter gutter, string root) : IDisposable
    {
        public Window Window { get; } = window;
        public MainView View { get; } = view;
        public TextBox Editor { get; } = editor;
        public LineNumberGutter Gutter { get; } = gutter;
        public void Dispose()
        {
            Window.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}