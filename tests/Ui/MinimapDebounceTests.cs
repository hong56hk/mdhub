using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 120: debounce minimap content redraws after text edits.
internal static class MinimapDebounceTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        EditorBehavior.Import(null);
        SessionManager.Enabled = false;
        EditorBehavior.Update(c => c with { Minimap = true }, persist: false);
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Settle() { for (var i = 0; i < 12; i++) Pump(); }
        void WaitDebounce()
        {
            using var t = new CancellationTokenSource(DebounceMs + 80);
            Dispatcher.UIThread.MainLoop(t.Token);
            Dispatcher.UIThread.RunJobs();
        }
        void Wait(Task t)
        {
            var end = DateTime.UtcNow.AddSeconds(60);
            while (!t.IsCompleted && DateTime.UtcNow < end) Pump();
            t.GetAwaiter().GetResult();
            Pump(); Pump();
        }
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }

        Type80KbAndDebounce(Check, Pump, Settle, Wait, WaitDebounce);
        ViewportScrollClickAndRefresh(Check, Pump, WaitDebounce);
        Console.WriteLine("All minimap-debounce tests passed.");
    }

    static void Type80KbAndDebounce(Action<bool, string> Check, Action Pump, Action Settle, Action<Task> Wait, Action WaitDebounce)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-120md-" + Guid.NewGuid().ToString("N")[..8])).FullName;
        var w = new MainWindow { Width = 1280, Height = 800 };
        try
        {
            File.WriteAllText(Path.Combine(root, "note-80.md"), GenerateMarkdown(80 * 1024));
            w.Show(); Pump(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Wait((Task)typeof(MainView).GetMethod("LoadRootAsync", Flags)!.Invoke(v, [root])!);
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [Path.Combine(root, "note-80.md")])!);
            Settle();
            typeof(MainView).GetMethod("RequestAction", Flags)!.Invoke(v, [WorkspaceAction.EditorOnly]);
            Dispatcher.UIThread.RunJobs(); Settle();

            var box = ActiveEditor(v) ?? throw new Exception("no editor");
            var map = box.GetVisualDescendants().OfType<EditorMinimap>().Single();
            Check(map.IsVisible, "minimap is visible");
            box.Focus();
            var before = box.Text ?? "";
            var caret = Math.Max(1, before.Length / 2);
            box.CaretIndex = box.SelectionStart = box.SelectionEnd = caret;
            Dispatcher.UIThread.RunJobs(); Pump();

            var redraws = Redraws(map);
            foreach (var (key, ch) in new[] { (Key.A, "a"), (Key.S, "s"), (Key.D, "d") })
            {
                w.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, ch);
                w.KeyTextInput(ch);
                w.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, ch);
                Dispatcher.UIThread.RunJobs();
                Pump();
            }
            Check(box.Text == before.Insert(caret, "asd"), "real keystrokes type 'asd' mid-document in an ~80 KB file");
            Check(Redraws(map) == redraws,
                "no full minimap content redraw per key while typing (redraws " + redraws + " -> " + Redraws(map) + ")");
            WaitDebounce();
            Check(Redraws(map) == redraws + 1
                  && SnapText(map) == box.Text,
                "the minimap reflects the latest text after the debounce (redraws=" + Redraws(map) + ")");
        }
        finally
        {
            w.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static void ViewportScrollClickAndRefresh(Action<bool, string> Check, Action Pump, Action WaitDebounce)
    {
        var editor = new TextEditor();
        var document = new NoteDocument { Path = "map.md" };
        document.Editor.Text = string.Join('\n', Enumerable.Range(1, 250).Select(i => $"## Heading {i}\nText with **emphasis** and `code`."));
        editor.ShowDocument(document);
        var window = new Window { Content = editor, Width = 800, Height = 600 };
        try
        {
            window.Show(); Pump(); Pump();
            var map = document.Editor.GetVisualDescendants().OfType<EditorMinimap>().Single();
            var scroll = document.Editor.GetVisualDescendants().OfType<ScrollViewer>().First();
            Check(map.IsVisible, "minimap is visible in the editor host");

            // Viewport indicator follows scroll immediately, with no content debounce.
            scroll.Offset = new Vector(scroll.Offset.X, 0);
            Dispatcher.UIThread.RunJobs(); Pump();
            var redraws = Redraws(map);
            var y0 = Viewport(map).Y;
            var target = Math.Max(80, scroll.Extent.Height * 0.55);
            scroll.Offset = new Vector(scroll.Offset.X, target);
            Dispatcher.UIThread.RunJobs();
            Check(Viewport(map).Y > y0 + 10,
                "viewport indicator follows scroll immediately (y " + y0.ToString("F1") + " -> " + Viewport(map).Y.ToString("F1") + ")");
            Check(Redraws(map) == redraws,
                "scrolling does not rebuild minimap content (redraws " + redraws + " -> " + Redraws(map) + ")");

            // Click and drag navigation.
            Point At(double y) => map.TranslatePoint(new Point(50, y), window)!.Value;
            scroll.Offset = new Vector(scroll.Offset.X, 0);
            Dispatcher.UIThread.RunJobs(); Pump();
            window.MouseDown(At(map.Bounds.Height * 0.7), MouseButton.Left);
            window.MouseUp(At(map.Bounds.Height * 0.7), MouseButton.Left); Pump();
            Check(scroll.Offset.Y > scroll.Extent.Height / 2, "clicking the map scrolls to a distant section");
            var offset = scroll.Offset.Y;
            window.MouseDown(At(map.Bounds.Height * 0.7), MouseButton.Left);
            window.MouseMove(At(map.Bounds.Height * 0.3), RawInputModifiers.LeftMouseButton);
            window.MouseUp(At(map.Bounds.Height * 0.3), MouseButton.Left); Pump();
            Check(scroll.Offset.Y < offset, "dragging the viewport scrolls upward");

            // Immediate refresh on theme change, resize, wrap toggle, and tab switch.
            redraws = Redraws(map);
            var previousTheme = ThemeService.Current;
            ThemeService.Apply(previousTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark, persist: false);
            Dispatcher.UIThread.RunJobs(); Pump();
            Check(Redraws(map) > redraws, "theme change refreshes the minimap snapshot immediately");
            ThemeService.Apply(previousTheme, persist: false);
            Dispatcher.UIThread.RunJobs(); Pump();

            redraws = Redraws(map);
            window.Height += 140;
            Dispatcher.UIThread.RunJobs(); Pump(); Pump();
            Check(Redraws(map) > redraws, "resize refreshes the minimap snapshot immediately");

            redraws = Redraws(map);
            document.Editor.TextWrapping = document.Editor.TextWrapping == TextWrapping.Wrap ? TextWrapping.NoWrap : TextWrapping.Wrap;
            Dispatcher.UIThread.RunJobs(); Pump();
            Check(Redraws(map) > redraws, "wrap toggle refreshes the minimap snapshot immediately");

            var other = new NoteDocument { Path = "other.md" };
            other.Editor.Text = "the other tab's unique text " + Guid.NewGuid().ToString("N");
            editor.ShowDocument(other);
            Dispatcher.UIThread.RunJobs(); Pump(); Pump();
            var otherMap = other.Editor.GetVisualDescendants().OfType<EditorMinimap>().Single();
            Check(otherMap.IsVisible && SnapText(otherMap) == other.Editor.Text,
                "tab switch refreshes the minimap snapshot immediately");
            // And the previous tab was flushed so it is current if shown again.
            editor.ShowDocument(document);
            Dispatcher.UIThread.RunJobs(); Pump();
            var back = document.Editor.GetVisualDescendants().OfType<EditorMinimap>().Single();
            Check(SnapText(back) == document.Editor.Text, "switching back shows the previous tab's current snapshot");
        }
        finally
        {
            window.Close();
            ThemeService.Apply(AppTheme.Dark, persist: false);
            EditorBehavior.Import(null);
        }
    }

    static int DebounceMs => (int)typeof(EditorMinimap).GetField("ContentDebounceMilliseconds", Flags | BindingFlags.Static)!.GetValue(null)!;
    static int Redraws(EditorMinimap map) => (int)typeof(EditorMinimap).GetProperty("ContentRedraws", Flags)!.GetValue(map)!;
    static string? SnapText(EditorMinimap map) => (string?)typeof(EditorMinimap).GetProperty("SnapshotText", Flags)!.GetValue(map);
    static Rect Viewport(EditorMinimap map) => (Rect)typeof(EditorMinimap).GetProperty("ViewportRect", Flags)!.GetValue(map)!;
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