using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 101: a right click below the text (bug 94b: the text presenter reports the event, the menu is retargeted onto
// the editor) runs the editor menu's normal opening route, so it ends up exactly like a right click on the text:
// Undo/Redo state, spelling suggestions for the word under the pointer, and the clicked split becoming active.
internal static class MenuOpenPathTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = false }).SetupWithoutStarting();
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        bool Until(Func<bool> ok) { var end = DateTime.UtcNow.AddSeconds(8); while (!ok() && DateTime.UtcNow < end) Pump(); return ok(); }
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-menuopen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var a = Path.Combine(root, "a.md"); File.WriteAllText(a, "Helo world");
        var b = Path.Combine(root, "b.md"); File.WriteAllText(b, "other");
        var spell = EditorBehavior.Current.SpellCheck;
        var w = new MainWindow { Width = 1100, Height = 700 };
        try
        {
            EditorBehavior.Update(d => d with { SpellCheck = true });
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Task Invoke(string m, params object?[] args) => (Task)typeof(MainView).GetMethod(m, Flags)!.Invoke(v, args)!;
            EditorPane ActivePane() => (EditorPane)typeof(MainView).GetProperty("ActivePane", Flags)!.GetValue(v)!;
            List<EditorPane> Panes() => ((IReadOnlyList<EditorPane>)typeof(MainView).GetProperty("EditorPanes", Flags)!.GetValue(v)!).ToList();
            Wait(v.OpenFolderPathAsync(root));
            Wait(Invoke("OpenPathAsync", a));
            var left = ActivePane();
            var docA = left.Documents.Single(d => d.Path == a);
            // Something to undo (and nothing to redo), so the two items differ.
            docA.Editor.Focus(); Pump();
            docA.Editor.CaretIndex = docA.Editor.Text!.Length; docA.Editor.SelectedText = "!"; Pump();
            Wait(Invoke("OpenPathAsync", b));
            Wait(Invoke("OpenToSideAsync", [null]));
            Check(Panes().Count == 2, "two splits");
            var right = ActivePane();
            Check(right != left, "the right split is active");
            typeof(MainView).GetMethod("ActivatePane", Flags)!.Invoke(v, [left]); Pump();
            Wait(Invoke("OpenPathAsync", a));
            Check(left.Active == docA && ActivePane() == left, "the left split shows a.md");
            var editor = docA.Editor;
            var menu = editor.ContextMenu!;
            object? source = null, sourceSeen = null;
            editor.AddHandler(Control.ContextRequestedEvent, (_, e) => { source = e.Source; sourceSeen ??= e.Source; }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            Check(Until(() => left.GetVisualDescendants().OfType<TextEditor>().First().Spelling.Count is > 0), "the misspelling 'Helo' is found");

            // Opens the menu with a real right click at a point in the editor, from the right split; returns what it shows.
            string Open(string label, Point inEditor)
            {
                var other = right.Documents.First(); other.Editor.Focus(); Pump();
                Check(ActivePane() == right, $"{label}: the other split is focused before the click");
                var at = editor.TranslatePoint(inEditor, w)!.Value;
                w.MouseMove(at); w.MouseDown(at, MouseButton.Right); w.MouseUp(at, MouseButton.Right);
                for (var k = 0; k < 6; k++) Pump();
                Check(menu.IsOpen, $"{label}: the editor menu opens");
                MenuItem? Named(string n) => menu.Items.OfType<MenuItem>().FirstOrDefault(i => i.Name == n);
                var fixes = menu.Items.OfType<MenuItem>().Where(i => i.Name == "SpellSuggestionMenuItem").Select(i => i.Header as string).ToList();
                var state = $"undo={Named("EditorUndoMenuItem")?.IsEnabled} redo={Named("EditorRedoMenuItem")?.IsEnabled} paste={Named("EditorPasteMenuItem")?.IsEnabled} "
                    + $"fixes=[{string.Join(",", fixes)}] add={Named("SpellAddMenuItem") is not null} active={(ActivePane() == left ? "left" : "right")} "
                    + $"items={string.Join("|", menu.Items.OfType<Control>().Select(i => i is MenuItem m ? m.Name : "-"))}";
                Console.WriteLine($"{label}: {state}");
                menu.Close(); Pump();
                return state;
            }
            var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().First();
            var topLeft = presenter.TranslatePoint(new Point(0, 0), editor)!.Value;
            var onWord = topLeft + new Point(8, presenter.Bounds.Height / 2);
            var below = new Point(onWord.X, Math.Min(editor.Bounds.Height - 10, topLeft.Y + presenter.Bounds.Height + 120));
            Check(below.Y > topLeft.Y + presenter.Bounds.Height + 20 && below.Y < editor.Bounds.Height, $"the second click is below the text, still in the editor ({below}, text {presenter.Bounds})");

            // Below the text first, on a menu never opened before: nothing left over from an earlier open can stand in
            // for its own opening handlers (before item 101 it had no suggestions here).
            var low = Open("right click below the text (first open)", below);
            Check(sourceSeen is TextPresenter, $"the below-text click is reported by the text presenter (the bug 94b route; {sourceSeen?.GetType().Name})");
            var normal = Open("right click on the text", onWord);
            Check(normal.Contains("undo=True redo=False") && normal.Contains("fixes=[") && !normal.Contains("fixes=[]") && normal.Contains("active=left"),
                $"normal right click: Undo on, Redo off, suggestions for Helo, the clicked split active ({normal})");
            Check(low == normal, $"a right click below the text shows exactly the same menu ({low} vs {normal})");
            // State changed since the last open: Undo the edit, then below the text again must show the new state.
            editor.Focus(); Pump(); docA.Undo(); Pump();
            var low2 = Open("below the text after Undo", below);
            var normal2 = Open("on the text after Undo", onWord);
            Check(low2 == normal2 && low2.Contains("redo=True"), $"after Undo both paths show Redo on and the same items ({low2} vs {normal2})");
        }
        finally
        {
            EditorBehavior.Update(d => d with { SpellCheck = spell });
            try { w.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}