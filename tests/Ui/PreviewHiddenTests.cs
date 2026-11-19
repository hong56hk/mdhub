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
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Text;
using Mdhub.Views;

// Item 117: skip the preview UI rebuild while the preview is hidden; refresh when it is shown again.
internal static class PreviewHiddenTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
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

        Type80KbKeys(Check, Pump, Settle, Wait);
        HiddenThenShown(Check, Pump, Settle, Wait, WaitUntil);
        Console.WriteLine("All preview-hidden tests passed.");
    }

    static void Type80KbKeys(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-117type-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        var w = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            File.WriteAllText(Path.Combine(root, "note-80.md"), GenerateMarkdown(80 * 1024));
            w.Show(); Pump(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Wait((Task)typeof(MainView).GetMethod("LoadRootAsync", Flags)!.Invoke(v, [root])!);
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [Path.Combine(root, "note-80.md")])!);
            Settle();
            Request(v, WorkspaceAction.EditorOnly); Settle();

            var box = ActiveEditor(v) ?? throw new Exception("no editor for 80 KB typetext");
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
            Check(box.Text == before.Insert(caret, "asd"), "real keystrokes type 'asd' mid-document in an ~80 KB Markdown file");
        }
        finally
        {
            w.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static void HiddenThenShown(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action<Func<bool>, string, int> WaitUntil)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-117hide-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        var w = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            File.WriteAllText(Path.Combine(root, "home.md"), "# Home\n\nHello uniquepara.\n\n[other](other.md)\n");
            File.WriteAllText(Path.Combine(root, "other.md"), "# Other\n\nOther file body.\n");
            w.Show(); Pump(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Wait((Task)typeof(MainView).GetMethod("LoadRootAsync", Flags)!.Invoke(v, [root])!);
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [Path.Combine(root, "home.md")])!);
            Request(v, WorkspaceAction.SplitView);
            Settle();
            WaitUntil(() => PreviewSource(Md(v)) == (ActiveEditor(v)?.Text ?? ""), "initial preview failed to render", 8);

            var md = Md(v);
            var box = ActiveEditor(v) ?? throw new Exception("no editor");
            var initialRebuilds = Rebuilds(md);
            Request(v, WorkspaceAction.EditorOnly);
            Settle();
            Check(!md.IsVisible, "Editor mode hides the preview");

            box.Focus();
            var before = box.Text ?? "";
            var insertAt = before.IndexOf("uniquepara", StringComparison.Ordinal);
            Check(insertAt >= 0, "fixture contains uniquepara");
            box.CaretIndex = box.SelectionStart = box.SelectionEnd = insertAt;
            Dispatcher.UIThread.RunJobs(); Pump();
            foreach (var (key, ch) in new[] { (Key.H, "H"), (Key.I, "i"), (Key.D, "d") })
            {
                w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, ch);
                w.KeyTextInput(ch);
                w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, ch);
                Dispatcher.UIThread.RunJobs();
                Pump();
            }
            var edited = box.Text ?? "";
            Check(edited == before.Insert(insertAt, "Hid"), "edits while the preview is hidden are applied to the source");

            // Past the 180 ms preview debounce, plus the off-thread count.
            WaitUntil(() =>
            {
                var stats = TextStatistics.Count(edited);
                var counts = w.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "Counts");
                return counts?.Text == Loc.F("Statistics", stats.Words, stats.Characters, stats.Lines);
            }, "status bar counts did not update while the preview was hidden", 8);
            Check(Rebuilds(md) == initialRebuilds, "edits while the preview is hidden do not rebuild the preview UI");
            Check(PreviewSource(md) != edited, "the hidden preview is left unchanged until it is shown");

            void AssertShown(WorkspaceAction mode, string label)
            {
                Request(v, mode);
                Pump();
                Check(md.IsVisible, label + ": preview is visible");
                WaitUntil(() => PreviewSource(md) == edited && PreviewVisual(md).Contains("Hiduniquepara"),
                    label + $": preview does not show the latest content (source-match={PreviewSource(md) == edited}, visual='{PreviewVisual(md)}')", 8);
                Check(Rebuilds(md) > initialRebuilds, label + ": showing the preview refreshes a stale UI");
            }

            AssertShown(WorkspaceAction.SplitView, "Split");
            AssertShown(WorkspaceAction.ViewerOnly, "Preview");
            // Source did not change after Split, so Viewer can reuse the current render (no extra rebuild required).
            Request(v, WorkspaceAction.EditorOnly);
            Settle();
            Request(v, WorkspaceAction.SplitView);
            Pump();
            WaitUntil(() => PreviewSource(md) == edited && PreviewVisual(md).Contains("Hiduniquepara"),
                "returning to Split still shows the latest content", 8);

            // Double-click-to-edit after the hidden-then-shown refresh.
            using (var settle = new CancellationTokenSource(700)) Dispatcher.UIThread.MainLoop(settle.Token);
            var content = md.FindControl<StackPanel>("PreviewContent") ?? throw new Exception("PreviewContent missing");
            var paragraph = content.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => BlockText(t).Contains("Hiduniquepara"))
                ?? throw new Exception("refreshed paragraph not found (visual='" + PreviewVisual(md) + "')");
            var target = paragraph.TranslatePoint(new Point(20, Math.Max(2, paragraph.Bounds.Height / 2)), w)
                ?? throw new Exception("paragraph has no window point");
            for (var i = 0; i < 2; i++) { w.MouseDown(target, MouseButton.Left); w.MouseUp(target, MouseButton.Left); }
            Pump();
            Check((bool)typeof(MdEditor).GetProperty("IsEditingPreview", Flags)!.GetValue(md)!,
                "double-click-to-edit still works after a hidden-then-shown refresh");
            typeof(MdEditor).GetMethod("CancelPreviewEdit", Flags)!.Invoke(md, []);
            Settle();

            // Link following after the hidden-then-shown refresh: hide, edit, show, then click [other](other.md).
            Request(v, WorkspaceAction.EditorOnly);
            Settle();
            var rebuildsBeforeLinkShow = Rebuilds(md);
            box.Focus();
            var zAt = (box.Text ?? "").IndexOf("Hiduniquepara", StringComparison.Ordinal);
            box.CaretIndex = box.SelectionStart = box.SelectionEnd = zAt;
            Dispatcher.UIThread.RunJobs(); Pump();
            w.KeyPress(Key.Z, RawInputModifiers.None, PhysicalKey.None, "z");
            w.KeyTextInput("z");
            w.KeyRelease(Key.Z, RawInputModifiers.None, PhysicalKey.None, "z");
            Dispatcher.UIThread.RunJobs(); Pump();
            edited = box.Text ?? "";
            var hiddenUntil = DateTime.UtcNow.AddMilliseconds(800);
            while (DateTime.UtcNow < hiddenUntil) Pump();
            Check(Rebuilds(md) == rebuildsBeforeLinkShow, "a hidden trailing edit must not rebuild the preview UI");
            Request(v, WorkspaceAction.SplitView);
            Pump();
            WaitUntil(() => md.IsVisible && PreviewSource(md) == edited, "preview was stale before the link click", 8);
            using (var settle = new CancellationTokenSource(700)) Dispatcher.UIThread.MainLoop(settle.Token);
            content = md.FindControl<StackPanel>("PreviewContent")!;
            var linkBlock = content.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(t => BlockText(t).Trim() == "other")
                ?? throw new Exception("rendered link 'other' not found (visual='" + PreviewVisual(md) + "')");
            var linkPoint = linkBlock.TranslatePoint(new Point(16, Math.Max(2, linkBlock.Bounds.Height / 2)), w)
                ?? throw new Exception("link has no window point");
            var hrefSeen = (string?)null;
            void OnLink(string href) => hrefSeen = href;
            md.LinkClicked += OnLink;
            try
            {
                w.MouseDown(linkPoint, MouseButton.Left); w.MouseUp(linkPoint, MouseButton.Left);
                Pump(); Pump();
                WaitUntil(() =>
                {
                    var selected = w.GetVisualDescendants().OfType<ListBox>()
                        .Where(c => c.Name == "Tabs" && c.IsEffectivelyVisible)
                        .Select(c => c.SelectedItem).OfType<NoteDocument>().FirstOrDefault();
                    var active = (NoteDocument?)typeof(MainView).GetField("_active", Flags)?.GetValue(v);
                    return (selected ?? active)?.Path is { } path
                        && string.Equals(Path.GetFileName(path), "other.md", StringComparison.OrdinalIgnoreCase);
                }, "link following from the preview did not open other.md after a hidden-then-shown refresh"
                    + $" (href={hrefSeen}, status={w.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "Status")?.Text}, bounds={linkBlock.Bounds})", 8);
            }
            finally { md.LinkClicked -= OnLink; }
            Check(true, "link following from the preview still works after a hidden-then-shown refresh");
        }
        finally
        {
            w.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static void Request(MainView v, WorkspaceAction action)
    {
        typeof(MainView).GetMethod("RequestAction", Flags)!.Invoke(v, [action]);
        Dispatcher.UIThread.RunJobs();
    }

    static MdEditor Md(MainView v) => v.GetVisualDescendants().OfType<MdEditor>().First(c => c.Name == "MdEditor" || c.IsVisible || true);

    static TextBox? ActiveEditor(MainView view)
    {
        var active = (NoteDocument?)typeof(MainView).GetProperty("ActiveDocument", Flags)?.GetValue(view)
            ?? (NoteDocument?)typeof(MainView).GetField("_active", Flags)?.GetValue(view);
        return active?.Editor;
    }

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
}