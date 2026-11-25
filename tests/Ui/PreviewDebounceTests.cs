using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 124: typing-driven preview refreshes wait for idle and must not apply mid-burst.
internal static class PreviewDebounceTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int DebounceMs => (int)typeof(MdEditor).GetField("TypingDebounceMilliseconds", Flags | BindingFlags.Static)!.GetValue(null)!;

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
        void WaitUntil(Func<bool> ready, string name, int seconds = 8)
        {
            var end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                Pump();
                if (ready()) return;
            }
            throw new Exception(name);
        }

        Type80Kb(Check, Pump, Settle, Wait, previewVisible: true);
        Type80Kb(Check, Pump, Settle, Wait, previewVisible: false);
        CadenceAndIdle(Check, Pump, Settle, Wait, WaitUntil);
        StaleParseDiscarded(Check, Pump, Settle, Wait, WaitUntil);
        HiddenSkip(Check, Pump, Settle, Wait, WaitUntil);
        ExplicitRefreshIsPrompt(Check, Pump, Settle, Wait, WaitUntil);
        Console.WriteLine("All preview-debounce tests passed.");
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

    static void CadenceAndIdle(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        using var session = Open(GenerateMarkdown(80 * 1024), Pump, Settle, Wait, previewVisible: true, wrap: true);
        var box = session.Editor;
        var md = session.Preview;
        box.Focus();
        var before = box.Text ?? "";
        var caret = Math.Max(1, before.IndexOf("Heading 10", StringComparison.Ordinal));
        if (caret < 1) caret = before.Length / 2;
        box.CaretIndex = box.SelectionStart = box.SelectionEnd = caret;
        Dispatcher.UIThread.RunJobs(); Pump();
        WaitUntil(() => PreviewSource(md) == before, "initial preview should be current before the cadence", 8);
        var rebuilds = Rebuilds(md);

        const int n = 15, gap = 150;
        for (var i = 0; i < n; i++)
        {
            TypeKeys(session.Window, Pump, (Key.X, "x"));
            var end = DateTime.UtcNow.AddMilliseconds(gap);
            while (DateTime.UtcNow < end) Pump();
            Check(Rebuilds(md) == rebuilds, "no preview apply happens while keys arrive at ~150 ms gaps (after key " + (i + 1) + ", rebuilds=" + Rebuilds(md) + ")");
        }
        var typed = box.Text ?? "";
        Check(typed == before.Insert(caret, new string('x', n)), "150 ms cadence inserts every character into the source");

        WaitUntil(() => Rebuilds(md) > rebuilds && PreviewSource(md) == typed,
            "preview refreshes once after idle >=400 ms with the typed text", 8);
        Check(Rebuilds(md) == rebuilds + 1, "preview refreshes once after idle (rebuilds " + rebuilds + " -> " + Rebuilds(md) + ")");
        Check(PreviewVisual(md).Contains(new string('x', n)), "idle preview render includes the typed characters");
    }

    static void StaleParseDiscarded(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        using var session = Open(GenerateMarkdown(80 * 1024), Pump, Settle, Wait, previewVisible: true, wrap: true);
        var box = session.Editor;
        var md = session.Preview;
        var before = box.Text ?? "";
        var caret = Math.Max(1, before.IndexOf("Heading 8", StringComparison.Ordinal));
        if (caret < 1) caret = before.Length / 2;
        box.Focus();
        box.CaretIndex = box.SelectionStart = box.SelectionEnd = caret;
        Dispatcher.UIThread.RunJobs(); Pump();
        WaitUntil(() => PreviewSource(md) == before, "initial preview should be current before the stale-parse test", 8);
        var rebuilds = Rebuilds(md);

        var armed = DateTime.UtcNow;
        TypeKeys(session.Window, Pump, (Key.Q, "Q"));
        var first = before.Insert(caret, "Q");
        // Wait just past the debounce (measured from the key, which arms the timer) so a parse of
        // the Q-only text can start, then type again before the UI apply can run.
        var untilParse = armed.AddMilliseconds(DebounceMs + 40);
        while (DateTime.UtcNow < untilParse) Pump();
        TypeKeys(session.Window, Pump, (Key.Z, "Z"));
        var final = first.Insert(caret + 1, "Z");
        var discardUntil = DateTime.UtcNow.AddMilliseconds(250);
        while (DateTime.UtcNow < discardUntil) Pump();
        Check(Rebuilds(md) == rebuilds && PreviewSource(md) == before,
            "stale parse result is discarded when a key arrives (rebuilds=" + Rebuilds(md) + ", source still previous=" + (PreviewSource(md) == before) + ")");
        Check(box.Text == final, "both characters are in the source after the interrupting key");

        WaitUntil(() => Rebuilds(md) == rebuilds + 1 && PreviewSource(md) == final,
            "after a later idle the preview applies the latest text, not the discarded parse", 8);
    }

    static void HiddenSkip(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        using var session = Open("# Hidden\n\nuniquepara body.\n", Pump, Settle, Wait, previewVisible: true, wrap: false);
        Request(session.View, WorkspaceAction.EditorOnly);
        Settle();
        var md = session.Preview;
        var box = session.Editor;
        Check(!md.IsVisible, "Editor mode hides the preview");
        var rebuilds = Rebuilds(md);
        var before = box.Text ?? "";
        var at = before.IndexOf("uniquepara", StringComparison.Ordinal);
        box.Focus();
        box.CaretIndex = box.SelectionStart = box.SelectionEnd = at;
        Dispatcher.UIThread.RunJobs(); Pump();
        TypeKeys(session.Window, Pump, (Key.H, "H"), (Key.I, "i"), (Key.D, "d"));
        var edited = before.Insert(at, "Hid");
        Check(box.Text == edited, "edits while the preview is hidden are applied to the source");
        var wait = DateTime.UtcNow.AddMilliseconds(DebounceMs + 800);
        while (DateTime.UtcNow < wait) Pump();
        Check(Rebuilds(md) == rebuilds, "hidden-preview skip still holds: edits do not rebuild the preview UI");
        Check(PreviewSource(md) != edited, "the hidden preview is left unchanged until it is shown");

        Request(session.View, WorkspaceAction.SplitView);
        Pump();
        Check(md.IsVisible, "Split shows the preview");
        WaitUntil(() => PreviewSource(md) == edited && PreviewVisual(md).Contains("Hiduniquepara"),
            "showing the hidden preview refreshes it immediately with the latest text", 8);
        Check(Rebuilds(md) > rebuilds, "showing the preview is an explicit refresh and rebuilds a stale UI");
    }

    static void ExplicitRefreshIsPrompt(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        using var session = Open("# Prompt\n\nuniquepara body.\n", Pump, Settle, Wait, previewVisible: true, wrap: false);
        var box = session.Editor;
        var md = session.Preview;
        var before = box.Text ?? "";
        var at = before.IndexOf("uniquepara", StringComparison.Ordinal);
        box.Focus();
        box.CaretIndex = box.SelectionStart = box.SelectionEnd = at;
        Dispatcher.UIThread.RunJobs(); Pump();
        WaitUntil(() => PreviewSource(md) == before, "initial preview should be current before the explicit refresh", 8);
        TypeKeys(session.Window, Pump, (Key.E, "E"));
        var edited = before.Insert(at, "E");
        // Well under the typing debounce: an explicit refresh must not wait for idle.
        md.RefreshNow();
        var deadline = DateTime.UtcNow.AddMilliseconds(Math.Max(200, DebounceMs - 200));
        while (DateTime.UtcNow < deadline && PreviewSource(md) != edited) Pump();
        Check(PreviewSource(md) == edited && PreviewVisual(md).Contains("Euniquepara"),
            "explicit refreshes still happen immediately, without waiting for the typing debounce (source=" + (PreviewSource(md) ?? "null") + ")");
    }

    static Session Open(string markdown, Action Pump, Action Settle, Action<Task> Wait, bool previewVisible, bool wrap)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-124pd-" + Guid.NewGuid().ToString("N")[..8])).FullName;
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
        var md = Md(v);
        return new Session(w, v, box, md, root);
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

    static MdEditor Md(MainView v) => v.GetVisualDescendants().OfType<MdEditor>().First();
    static TextBox? ActiveEditor(MainView view) =>
        ((NoteDocument?)typeof(MainView).GetProperty("ActiveDocument", Flags)?.GetValue(view)
         ?? (NoteDocument?)typeof(MainView).GetField("_active", Flags)?.GetValue(view))?.Editor;
    static int Rebuilds(MdEditor md) => (int)typeof(MdEditor).GetProperty("PreviewUiRebuilds", Flags)!.GetValue(md)!;
    static string? PreviewSource(MdEditor md)
    {
        var render = typeof(MdEditor).GetField("_render", Flags)!.GetValue(md);
        if (render is null) return null;
        var map = render.GetType().GetProperty("Map")!.GetValue(render);
        return map?.GetType().GetProperty("Source")!.GetValue(map) as string;
    }
    static string PreviewVisual(MdEditor md)
    {
        var content = md.FindControl<StackPanel>("PreviewContent");
        if (content is null) return "";
        return string.Concat(content.GetVisualDescendants().OfType<TextBlock>().Select(BlockText));
    }
    static string BlockText(TextBlock block)
    {
        if (block.Inlines is { Count: > 0 } inlines)
        {
            var sb = new StringBuilder();
            void Walk(InlineCollection items)
            {
                foreach (var inline in items)
                {
                    if (inline is Run run) sb.Append(run.Text);
                    else if (inline is LineBreak) sb.Append('\n');
                    else if (inline is Span span) Walk(span.Inlines);
                }
            }
            Walk(inlines);
            if (sb.Length > 0) return sb.ToString();
        }
        return block.Text ?? "";
    }

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

    sealed class Session(Window window, MainView view, TextBox editor, MdEditor preview, string root) : IDisposable
    {
        public Window Window { get; } = window;
        public MainView View { get; } = view;
        public TextBox Editor { get; } = editor;
        public MdEditor Preview { get; } = preview;
        public void Dispose()
        {
            Window.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}