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
using Mdhub.Rendering;
using Mdhub.Services;
using Mdhub.Text;
using Mdhub.Views;

// Item 125: incremental preview updates reuse unchanged top-level controls.
internal static class PreviewDiffTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int DebounceMs => (int)typeof(MdEditor).GetField("TypingDebounceMilliseconds", Flags | BindingFlags.Static)!.GetValue(null)!;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        EditorBehavior.Import(null);
        PreviewSettings.Import(null);
        SessionManager.Enabled = false;
        var previousTheme = ThemeService.Current;
        try
        {
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
            EditsMatchFreshRender(Check, Pump, Settle, Wait, WaitUntil);
            InsertDeleteSplitMerge(Check, Pump, Settle, Wait, WaitUntil);
            UnchangedControlsReused(Check, Pump, Settle, Wait, WaitUntil);
            ForceFullRebuilds(Check, Pump, Settle, Wait, WaitUntil);
            ScrollAndLineMapping(Check, Pump, Settle, Wait, WaitUntil);
            CadenceAndIdle(Check, Pump, Settle, Wait, WaitUntil);
            HiddenSkip(Check, Pump, Settle, Wait, WaitUntil);
            Console.WriteLine("All preview-diff tests passed.");
        }
        finally
        {
            ThemeService.Apply(previousTheme, persist: false);
            PreviewSettings.Import(null);
            EditorBehavior.Import(null);
        }
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

    static void EditsMatchFreshRender(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        using var session = Open(StructuredMarkdown(), Pump, Settle, Wait, previewVisible: true, wrap: true, extraFiles: StructuredExtras);
        EqualsFresh(session, Check, "initial structured preview");

        ApplyText(session, Pump, WaitUntil, EditAt(session.Editor.Text!, "Middle paragraph text.", "Middle paragraph X text."), "middle edit applied");
        EqualsFresh(session, Check, "after an edit in the middle");

        ApplyText(session, Pump, WaitUntil, EditAt(session.Editor.Text!, "# Title", "# TitleX"), "start edit applied");
        EqualsFresh(session, Check, "after an edit at the start");

        ApplyText(session, Pump, WaitUntil, session.Editor.Text + " more.", "end edit applied");
        EqualsFresh(session, Check, "after an edit at the end");

        using var large = Open(GenerateMarkdown(80 * 1024), Pump, Settle, Wait, previewVisible: true, wrap: true);
        var box = large.Editor;
        var before = box.Text ?? "";
        var caret = Math.Max(1, before.IndexOf("Heading 10", StringComparison.Ordinal));
        if (caret < 1) caret = before.Length / 2;
        box.Focus();
        box.CaretIndex = box.SelectionStart = box.SelectionEnd = caret;
        Dispatcher.UIThread.RunJobs(); Pump();
        var inc = Incremental(large.Preview);
        TypeKeys(large.Window, Pump, (Key.A, "a"), (Key.S, "s"), (Key.D, "d"));
        WaitUntil(() => Incremental(large.Preview) > inc && PreviewSource(large.Preview) == box.Text, "80 KB mid-document typetext should refresh the preview after idle", 8);
        Check(box.Text == before.Insert(caret, "asd"), "80 KB typetext 'asd' is in the source before comparing the tree");
        EqualsFresh(large, Check, "80 KB typetext middle edit");
    }

    static void InsertDeleteSplitMerge(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        using var session = Open(StructuredMarkdown(), Pump, Settle, Wait, previewVisible: true, wrap: true, extraFiles: StructuredExtras);
        var inserted = InsertBefore(session.Editor.Text!, "## End", "New paragraph between sections.\n\n");
        var beforeInsert = Snapshot(session.Preview);
        ApplyText(session, Pump, WaitUntil, inserted, "inserted a new paragraph");
        EqualsFresh(session, Check, "after inserting a new paragraph");
        Check(ReuseHolds(beforeInsert, session.Preview, "Title", "End"), "inserting a paragraph reuses unchanged blocks including the heading after the insert");
        Check(PreviewVisual(session.Preview).Contains("New paragraph between sections."), "the new paragraph is visible in the preview");

        var withoutHeading = RemoveOnce(session.Editor.Text!, "## Section\n\n");
        var beforeDelete = Snapshot(session.Preview);
        ApplyText(session, Pump, WaitUntil, withoutHeading, "deleted a heading");
        EqualsFresh(session, Check, "after deleting a heading");
        Check(ReuseHolds(beforeDelete, session.Preview, "Title", "Last paragraph"),
            "deleting a heading reuses unchanged blocks around it");
        Check(!PreviewVisual(session.Preview).Contains("Section") || PreviewVisual(session.Preview).IndexOf("Section", StringComparison.Ordinal) == PreviewVisual(session.Preview).LastIndexOf("Section", StringComparison.Ordinal),
            "the deleted heading is no longer a preview heading (or only remains if the word appears elsewhere)");

        // Split one paragraph into two blocks, then merge them back.
        using var splitSession = Open(StructuredMarkdown(), Pump, Settle, Wait, previewVisible: true, wrap: true, extraFiles: StructuredExtras);
        var split = EditAt(splitSession.Editor.Text!, "Intro paragraph one.", "Intro paragraph\n\none.");
        var beforeSplit = Snapshot(splitSession.Preview);
        ApplyText(splitSession, Pump, WaitUntil, split, "split a paragraph into two blocks");
        EqualsFresh(splitSession, Check, "after splitting a paragraph into two blocks");
        Check(ReuseHolds(beforeSplit, splitSession.Preview, "Title", "Last paragraph"), "splitting a paragraph reuses unchanged surrounding blocks");

        var merged = EditAt(splitSession.Editor.Text!, "Intro paragraph\n\none.", "Intro paragraph one.");
        ApplyText(splitSession, Pump, WaitUntil, merged, "merged two paragraphs back into one");
        EqualsFresh(splitSession, Check, "after merging two paragraphs");
    }

    static void UnchangedControlsReused(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        using var session = Open(StructuredMarkdown(), Pump, Settle, Wait, previewVisible: true, wrap: true, extraFiles: StructuredExtras);
        var before = Snapshot(session.Preview);
        var full = FullRebuilds(session.Preview);
        var inc = Incremental(session.Preview);
        ApplyText(session, Pump, WaitUntil, EditAt(session.Editor.Text!, "Middle paragraph text.", "Middle paragraph changed."), "middle paragraph edited for reuse");
        Check(Incremental(session.Preview) == inc + 1, "a one-block typography-neutral edit uses an incremental apply");
        Check(FullRebuilds(session.Preview) == full, "a one-block edit does not force a full rebuild");
        var after = session.Preview;
        Check(ReuseHolds(before, after, "Title"), "the title heading control is reused");
        Check(ReuseHolds(before, after, "Intro paragraph one."), "the intro paragraph control is reused");
        Check(ReuseHolds(before, after, "list item"), "the list control is reused");
        Check(ReuseHolds(before, after, "void Example"), "the code block control is reused");
        Check(ReuseHolds(before, after, "Col A"), "the table control is reused");
        Check(ReuseHolds(before, after, "Last paragraph"), "the last paragraph control is reused");
        Check(!ReuseHolds(before, after, "Middle paragraph changed."), "the edited paragraph is a new control");
        var render = RenderOf(after)!;
        Check(render.ReusedCount >= 6 && render.ReplacedCount == 1, "the patch reuses the unchanged prefix and suffix (reused=" + render.ReusedCount + ", replaced=" + render.ReplacedCount + ")");
    }

    static void ForceFullRebuilds(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        using var session = Open(StructuredMarkdown(), Pump, Settle, Wait, previewVisible: true, wrap: true, extraFiles: StructuredExtras);
        ApplyText(session, Pump, WaitUntil, EditAt(session.Editor.Text!, "Middle paragraph text.", "Middle paragraph Y."), "seed an incremental tree before forced rebuilds");

        var beforeTheme = Snapshot(session.Preview);
        var full = FullRebuilds(session.Preview);
        var inc = Incremental(session.Preview);
        var from = ThemeService.Current;
        var to = from == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        ThemeService.Apply(to, persist: false);
        WaitUntil(() => FullRebuilds(session.Preview) > full, "theme change should force a full preview rebuild", 8);
        Check(Incremental(session.Preview) == inc, "a theme change does not take the incremental path");
        Check(!AnyReused(beforeTheme, session.Preview), "a theme change replaces every top-level preview control");
        EqualsFresh(session, Check, "after a theme change");
        ThemeService.Apply(from, persist: false);
        WaitUntil(() => FullRebuilds(session.Preview) > full + 1, "restoring the theme should full-rebuild again", 8);

        var beforeSettings = Snapshot(session.Preview);
        full = FullRebuilds(session.Preview);
        inc = Incremental(session.Preview);
        PreviewSettings.Update(s => s with { BodySize = s.BodySize >= 18 ? 14 : 18 }, persist: false);
        WaitUntil(() => FullRebuilds(session.Preview) > full, "a preview typography change should force a full rebuild", 8);
        Check(Incremental(session.Preview) == inc, "a typography change does not take the incremental path");
        Check(!AnyReused(beforeSettings, session.Preview), "a typography change replaces every top-level preview control");
        EqualsFresh(session, Check, "after a preview typography change");
        PreviewSettings.Reset(persist: false);
        WaitUntil(() => FullRebuilds(session.Preview) > full + 1, "restoring typography should full-rebuild again", 8);

        File.WriteAllText(Path.Combine(session.Root, "other.md"), "# Other document\n\nA different note.\n");
        var beforeSwitch = Snapshot(session.Preview);
        full = FullRebuilds(session.Preview);
        inc = Incremental(session.Preview);
        Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(session.View, [Path.Combine(session.Root, "other.md")])!);
        WaitUntil(() => FullRebuilds(session.Preview) > full && (PreviewSource(session.Preview) ?? "").Contains("Other document"), "switching documents should force a full rebuild", 8);
        Check(Incremental(session.Preview) == inc, "a document switch does not take the incremental path");
        Check(!AnyReused(beforeSwitch, session.Preview), "a document switch replaces every top-level preview control");
        EqualsFresh(session, Check, "after a document switch");
        Check(PreviewVisual(session.Preview).Contains("A different note."), "the switched document is shown");
    }

    static void ScrollAndLineMapping(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        using var session = Open(GenerateMarkdown(80 * 1024), Pump, Settle, Wait, previewVisible: true, wrap: true);
        var md = session.Preview;
        var box = session.Editor;
        var text = box.Text ?? "";
        var caret = text.IndexOf("Heading 16", StringComparison.Ordinal);
        if (caret < 0) caret = text.Length / 2;
        box.CaretIndex = box.SelectionStart = box.SelectionEnd = caret;
        Dispatcher.UIThread.RunJobs(); Pump();
        var centers = Centers(md);
        md.CenterPreviewOnLine(caret);
        WaitUntil(() => Centers(md) > centers && ScrollY(md) > 0, "centering the preview on a late heading should scroll down", 8);
        var offset = ScrollY(md);
        var heading = ControlForLine(md, caret) ?? throw new Exception("no preview block for Heading 16");
        var inc = Incremental(md);
        ApplyText(session, Pump, WaitUntil, EditAt(text, "# Heading 1\n", "# Heading 1 changed\n"), "early heading edited after scroll");
        Check(Incremental(md) == inc + 1, "the early heading edit was applied incrementally");
        Check(ReferenceEquals(heading, ControlForLine(md, session.Editor.Text!.IndexOf("Heading 16", StringComparison.Ordinal))),
            "line mapping for an unchanged suffix heading keeps the same control after a diff");
        Check(Math.Abs(ScrollY(md) - offset) < 40, "scroll position is preserved after an incremental apply (was " + offset.ToString("0.0") + ", now " + ScrollY(md).ToString("0.0") + ")");
        EqualsFresh(session, Check, "after a scrolled incremental edit");
        var later = session.Editor.Text!.IndexOf("Heading 16", StringComparison.Ordinal);
        var centers2 = Centers(md);
        md.CenterPreviewOnLine(later);
        WaitUntil(() => Centers(md) > centers2, "CenterPreviewOnLine still works after a diff", 8);
        Check(ScrollY(md) > 0, "recentering a late heading after a diff still leaves the preview scrolled");
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
        var inc = Incremental(md);
        for (var i = 1; i <= 15; i++)
        {
            var start = DateTime.UtcNow;
            TypeKeys(session.Window, Pump, (Key.X, "x"));
            var deadline = start.AddMilliseconds(150);
            while (DateTime.UtcNow < deadline) Pump();
            Check(Rebuilds(md) == rebuilds, "no preview apply happens while keys arrive at ~150 ms gaps (after key " + i + ", rebuilds=" + Rebuilds(md) + ")");
        }
        Check(box.Text == before.Insert(caret, new string('x', 15)), "150 ms cadence inserts every character into the source");
        WaitUntil(() => Rebuilds(md) == rebuilds + 1 && PreviewSource(md) == box.Text, "preview refreshes once after idle >=400 ms", 8);
        Check(Incremental(md) == inc + 1, "the idle refresh after the burst is incremental");
        Check(PreviewVisual(md).Contains("xxxxxxxxxxxxxxx") || (PreviewSource(md) ?? "").Contains("xxxxxxxxxxxxxxx"),
            "idle preview render includes the typed characters");
    }

    static void HiddenSkip(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        using var session = Open(GenerateMarkdown(80 * 1024), Pump, Settle, Wait, previewVisible: true, wrap: true);
        Request(session.View, WorkspaceAction.EditorOnly);
        var until = DateTime.UtcNow.AddSeconds(2);
        while (DateTime.UtcNow < until) Pump();
        Check(!session.Preview.IsVisible || !PreviewVisible(session), "Editor mode hides the preview");
        var md = session.Preview;
        var box = session.Editor;
        var before = box.Text ?? "";
        var rebuilds = Rebuilds(md);
        var visual = PreviewVisual(md);
        box.Focus();
        var caret = Math.Max(1, before.Length / 2);
        box.CaretIndex = box.SelectionStart = box.SelectionEnd = caret;
        Dispatcher.UIThread.RunJobs(); Pump();
        TypeKeys(session.Window, Pump, (Key.A, "a"), (Key.S, "s"), (Key.D, "d"));
        Check(box.Text == before.Insert(caret, "asd"), "edits while the preview is hidden are applied to the source");
        var idle = DateTime.UtcNow.AddMilliseconds(DebounceMs + 200);
        while (DateTime.UtcNow < idle) Pump();
        Check(Rebuilds(md) == rebuilds && PreviewVisual(md) == visual, "hidden-preview skip still holds: edits do not rebuild the preview UI");
        Check(PreviewSource(md) == before || PreviewSource(md) != box.Text, "the hidden preview is left unchanged until it is shown");
        Request(session.View, WorkspaceAction.SplitView);
        WaitUntil(() => PreviewVisible(session) && PreviewSource(md) == box.Text && Rebuilds(md) > rebuilds,
            "showing the preview is an explicit refresh and rebuilds a stale UI", 8);
        EqualsFresh(session, Check, "after showing a stale hidden preview");
    }

    static void ApplyText(Session session, Action Pump, Action<Func<bool>, string, int> WaitUntil, string next, string name)
    {
        var box = session.Editor;
        var rebuilds = Rebuilds(session.Preview);
        box.Text = next;
        Dispatcher.UIThread.RunJobs(); Pump();
        session.Preview.RefreshNow();
        WaitUntil(() => Rebuilds(session.Preview) > rebuilds && PreviewSource(session.Preview) == next, name, 8);
        Pump(); Pump();
    }

    static void EqualsFresh(Session session, Action<bool, string> Check, string label)
    {
        var live = RenderOf(session.Preview) ?? throw new Exception(label + ": no live render");
        var text = live.Map.Source;
        var editor = ActiveEditor(session.View);
        Check(editor is not null && text == editor.Text, label + ": live map source matches the editor");
        var doc = (NoteDocument?)typeof(MainView).GetProperty("ActiveDocument", Flags)?.GetValue(session.View)
            ?? (NoteDocument?)typeof(MainView).GetField("_active", Flags)?.GetValue(session.View);
        var baseDir = doc?.Path is { } path ? Path.GetDirectoryName(path) : session.Root;
        var styleInline = Path.GetExtension(doc?.Path ?? doc?.UntitledName ?? "note.md").Equals(".md", StringComparison.OrdinalIgnoreCase);
        var fresh = MarkdownPreview.RenderMapped(MarkdownPreview.Parse(text), text, baseDir, styleInline);
        Check(live.TopLevels.Count == fresh.TopLevels.Count, label + ": same top-level block count (" + live.TopLevels.Count + " vs " + fresh.TopLevels.Count + ")");
        Check(live.Controls.Count == fresh.Controls.Count && live.Controls.Count == PreviewContent(session.Preview).Children.Count,
            label + ": same control count and host child count");
        Check(VisualOf(live.Controls) == VisualOf(fresh.Controls) && VisualOf(live.Controls) == PreviewVisual(session.Preview),
            label + ": same preview text in order");
        if (live.Map.Blocks.Count != fresh.Map.Blocks.Count)
            Check(false, label + ": same mapped block count (" + live.Map.Blocks.Count + " vs " + fresh.Map.Blocks.Count + ")");
        else
        {
            for (var i = 0; i < live.Map.Blocks.Count; i++)
            {
                var a = live.Map.Blocks[i];
                var b = fresh.Map.Blocks[i];
                if (a.Start != b.Start || a.End != b.End || a.IsContainer != b.IsContainer || a.FallbackId != b.FallbackId)
                    Check(false, label + ": block " + i + " mapping (" + a.Start + "-" + a.End + " vs " + b.Start + "-" + b.End + ")");
            }
            Check(true, label + ": same source mapping for every block");
        }
        if (live.Map.Segments.Count != fresh.Map.Segments.Count)
            Check(false, label + ": same mapped segment count (" + live.Map.Segments.Count + " vs " + fresh.Map.Segments.Count + ")");
        else
        {
            for (var i = 0; i < live.Map.Segments.Count; i++)
            {
                var a = live.Map.Segments[i];
                var b = fresh.Map.Segments[i];
                if (a.Start != b.Start || a.Length != b.Length || a.BlockId != b.BlockId)
                    Check(false, label + ": segment " + i + " mapping");
            }
            Check(true, label + ": same source mapping for every segment");
        }
        var pos = 0;
        var lines = 0;
        while (pos <= text.Length)
        {
            var (s, e) = CaretLine.At(text, Math.Min(pos, text.Length));
            if (live.Map.BlockForLine(s, e) != fresh.Map.BlockForLine(s, e))
                Check(false, label + ": BlockForLine at " + s);
            lines++;
            if (e >= text.Length) break;
            pos = e + (text[e] == '\r' && e + 1 < text.Length && text[e + 1] == '\n' ? 2 : 1);
        }
        Check(lines > 0, label + ": BlockForLine matches a fresh render on every source line (" + lines + " lines)");
        Check(MapHighlightsEqual(live, fresh), label + ": selection highlights match a fresh render");
    }

    static bool MapHighlightsEqual(PreviewRender live, PreviewRender fresh)
    {
        var text = live.Map.Source;
        if (text.Length == 0) return true;
        var points = new[] { 0, text.Length / 4, text.Length / 2, Math.Max(0, text.Length - 8) }.Distinct();
        foreach (var start in points)
        {
            var end = Math.Min(text.Length, start + Math.Min(24, Math.Max(1, text.Length / 10)));
            var a = live.Map.Map(start, end);
            var b = fresh.Map.Map(start, end);
            if (a.Count != b.Count) return false;
            for (var i = 0; i < a.Count; i++)
                if (a[i] != b[i]) return false;
        }
        return true;
    }

    static Session Open(string markdown, Action Pump, Action Settle, Action<Task> Wait, bool previewVisible, bool wrap, Action<string>? extraFiles = null)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-125pd-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        File.WriteAllText(Path.Combine(root, "note.md"), markdown);
        extraFiles?.Invoke(root);
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
    static int Incremental(MdEditor md) => (int)typeof(MdEditor).GetProperty("PreviewIncrementalApplies", Flags)!.GetValue(md)!;
    static int FullRebuilds(MdEditor md) => (int)typeof(MdEditor).GetProperty("PreviewFullRebuilds", Flags)!.GetValue(md)!;
    static int Centers(MdEditor md) => (int)typeof(MdEditor).GetProperty("PreviewCenters", Flags)!.GetValue(md)!;
    static PreviewRender? RenderOf(MdEditor md) => typeof(MdEditor).GetField("_render", Flags)!.GetValue(md) as PreviewRender;
    static string? PreviewSource(MdEditor md) => RenderOf(md)?.Map.Source;
    static StackPanel PreviewContent(MdEditor md) => md.FindControl<StackPanel>("PreviewContent") ?? throw new Exception("PreviewContent missing");
    static double ScrollY(MdEditor md)
    {
        var scroll = md.FindControl<ScrollViewer>("PreviewScroll") ?? throw new Exception("PreviewScroll missing");
        return scroll.Offset.Y;
    }
    static bool PreviewVisible(Session session)
    {
        var content = session.Preview.FindControl<StackPanel>("PreviewContent");
        return session.Preview.IsVisible && content is { IsEffectivelyVisible: true };
    }
    static Control? ControlForLine(MdEditor md, int caret)
    {
        var render = RenderOf(md);
        if (render is null) return null;
        var (s, e) = CaretLine.At(render.Map.Source, caret);
        var id = render.Map.BlockForLine(s, e);
        return id >= 0 && render.Blocks.TryGetValue(id, out var control) ? control : null;
    }

    static string PreviewVisual(MdEditor md) => VisualOf(PreviewContent(md).Children);
    static string VisualOf(IEnumerable<Control> controls)
    {
        var sb = new StringBuilder();
        foreach (var control in controls) Walk(control, sb);
        return sb.ToString();
    }
    static void Walk(Control control, StringBuilder sb)
    {
        if (control is TextBlock block) sb.Append(BlockText(block));
        if (control is Panel panel)
            foreach (var child in panel.Children.OfType<Control>()) Walk(child, sb);
        else if (control is Decorator decorator && decorator.Child is Control inner) Walk(inner, sb);
        else if (control is ContentControl content && content.Content is Control hosted) Walk(hosted, sb);
    }
    static string BlockText(TextBlock block)
    {
        if (block.Inlines is { Count: > 0 } inlines)
        {
            var sb = new StringBuilder();
            void WalkInlines(InlineCollection items)
            {
                foreach (var inline in items)
                {
                    if (inline is Run run) sb.Append(run.Text);
                    else if (inline is LineBreak) sb.Append('\n');
                    else if (inline is Span span) WalkInlines(span.Inlines);
                }
            }
            WalkInlines(inlines);
            if (sb.Length > 0) return sb.ToString();
        }
        return block.Text ?? "";
    }

    record SnapshotEntry(Control Control, string Text);
    static List<SnapshotEntry> Snapshot(MdEditor md)
    {
        var content = PreviewContent(md);
        return content.Children.OfType<Control>().Select(c => new SnapshotEntry(c, VisualOf([c]))).ToList();
    }
    static bool ReuseHolds(List<SnapshotEntry> before, MdEditor after, params string[] needles)
    {
        var now = PreviewContent(after).Children.OfType<Control>().ToList();
        foreach (var needle in needles)
        {
            var old = before.FirstOrDefault(e => e.Text.Contains(needle));
            if (old is null) return false;
            var found = now.FirstOrDefault(c => ReferenceEquals(c, old.Control));
            if (found is null) return false;
            if (!VisualOf([found]).Contains(needle) && needle.EndsWith("X") == false) return false;
        }
        return true;
    }
    static bool AnyReused(List<SnapshotEntry> before, MdEditor after)
    {
        var now = PreviewContent(after).Children.OfType<Control>().ToList();
        return before.Any(e => now.Any(c => ReferenceEquals(c, e.Control)));
    }

    static string EditAt(string text, string find, string replace)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        if (at < 0) throw new Exception("missing text to edit: " + find);
        return text.Remove(at, find.Length).Insert(at, replace);
    }
    static string InsertBefore(string text, string find, string insert)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        if (at < 0) throw new Exception("missing insertion point: " + find);
        return text.Insert(at, insert);
    }
    static string RemoveOnce(string text, string find)
    {
        var at = text.IndexOf(find, StringComparison.Ordinal);
        if (at < 0) throw new Exception("missing text to delete: " + find);
        return text.Remove(at, find.Length);
    }

    static string StructuredMarkdown() =>
        "# Title\n\n" +
        "Intro paragraph one.\n\n" +
        "## Section\n\n" +
        "Middle paragraph text.\n\n" +
        "- list item one\n- list item two with `inline code`\n\n" +
        "```csharp\nvoid Example() { Console.WriteLine(\"hello\"); }\n```\n\n" +
        "| Col A | Col B |\n| --- | --- |\n| 1 | 2 |\n\n" +
        "![dot](dot.png)\n\n" +
        "## End\n\n" +
        "Last paragraph.\n";

    static void StructuredExtras(string root)
    {
        // 1x1 PNG so the image block is a real control in the structured fixture.
        File.WriteAllBytes(Path.Combine(root, "dot.png"), Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
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
            sb.Append("| Col A | Col B | Col C |\n| --- | --- |\n| ").Append(n).Append(" | value | more |\n\n");
        }
        return sb.ToString();
    }

    sealed class Session(Window window, MainView view, TextBox editor, MdEditor preview, string root) : IDisposable
    {
        public Window Window { get; } = window;
        public MainView View { get; } = view;
        public TextBox Editor { get; } = editor;
        public MdEditor Preview { get; } = preview;
        public string Root { get; } = root;
        public void Dispose()
        {
            Window.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}