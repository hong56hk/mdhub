using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Bug 95: a tab alone in its split never moves out of it (menu hidden, shortcut a no-op), whichever split it is in
// and however the split was made (Ctrl+\, a move, a restored session); a move that empties its split closes it.
internal static class MoveTabSingleTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SessionManager.Enabled = false;
        Shortcuts.ResetAll(); Shortcuts.ReleaseAll();
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-movesingle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string Make(string name, string text) { var p = Path.Combine(root, name); File.WriteAllText(p, text); return p; }
        var a = Make("a.md", "alpha"); var b = Make("b.md", "bravo"); var c = Make("c.md", "charlie");
        var window = new MainWindow { Width = 1600, Height = 800 };
        void Pump() { using var timeout = new CancellationTokenSource(35); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 3; i++) Pump(); }
        void Wait(Task task) { var until = DateTime.UtcNow.AddSeconds(12); while (!task.IsCompleted && DateTime.UtcNow < until) Pump(); if (!task.IsCompleted) throw new Exception("Operation timed out"); task.GetAwaiter().GetResult(); Pump(); }
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
        var ctrl = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        var move = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta | RawInputModifiers.Control : RawInputModifiers.Control | RawInputModifiers.Alt;
        void Press(Key key, RawInputModifiers mods) { window.KeyPress(key, mods, PhysicalKey.None, null); Settle(); window.KeyRelease(key, mods, PhysicalKey.None, null); Settle(); }
        try
        {
            window.Show(); Settle();
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            EditorPane ActivePane() => (EditorPane)typeof(MainView).GetProperty("ActivePane", Flags)!.GetValue(view)!;
            List<EditorPane> Panes() => ((IReadOnlyList<EditorPane>)typeof(MainView).GetProperty("EditorPanes", Flags)!.GetValue(view)!).ToList();
            Task Invoke(string method, params object?[] args) => (Task)typeof(MainView).GetMethod(method, Flags)!.Invoke(view, args)!;
            string Layout() => string.Join(" | ", Panes().Select(p => string.Join(",", p.Documents.Select(d => Path.GetFileName(d.Path) ?? "new"))));
            // A real right-click on the tab (pointer events, not a synthetic ContextRequested).
            (bool Right, bool Left) MenuFor(EditorPane pane, NoteDocument d)
            {
                var list = pane.FindControl<FileTabs>("FileTabs")!.FindControl<ListBox>("Tabs")!;
                var item = list.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.DataContext == d);
                var at = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
                window.MouseDown(at, MouseButton.Right); window.MouseUp(at, MouseButton.Right); Settle();
                var menu = list.ContextMenu!;
                MenuItem Item(string name) => menu.Items.OfType<MenuItem>().Single(i => i.Name == name);
                var result = (Item("TabMoveRightMenuItem").IsVisible, Item("TabMoveLeftMenuItem").IsVisible);
                Check(menu.IsOpen, $"tab menu opened by right-click ({Layout()})");
                menu.Close(); Settle();
                return result;
            }
            void Focus(EditorPane pane, NoteDocument d)
            {
                window.MouseDown(d.Editor.TranslatePoint(new Point(20, 10), window)!.Value, MouseButton.Left);
                window.MouseUp(d.Editor.TranslatePoint(new Point(20, 10), window)!.Value, MouseButton.Left); Settle();
                Check(ActivePane() == pane && pane.Active == d, $"clicked editor is active ({Layout()})");
            }
            Wait(view.OpenFolderPathAsync(root));
            Wait(Invoke("OpenPathAsync", a));
            var left = ActivePane();
            Wait(Invoke("CloseOtherDocumentsAsync", left.Documents.First(d => d.Path == a)));
            Wait(Invoke("OpenPathAsync", b));
            var docB = left.Active!;
            // Ctrl+\ : split editor right, the right split has one tab (a second view of b).
            Press(Key.OemPipe, ctrl);
            Check(Panes().Count == 2 && Panes()[1].Documents.Count == 1, $"Ctrl+\\ made a one-tab right split ({Layout()})");
            var right = Panes()[1];
            var single = right.Documents[0];
            Focus(right, single);
            var menu = MenuFor(right, single);
            Check(!menu.Right && !menu.Left, $"1. single-tab rightmost split: no Move items in its tab menu ({menu})");
            Press(Key.Right, move);
            Check(Panes().Count == 2 && right.Documents.Single() == single, $"1. single-tab rightmost split: Ctrl+Alt+Right does nothing ({Layout()})");
            Press(Key.Left, move);
            Check(Panes().Count == 2 && right.Documents.Single() == single && left.Documents.Count == 2, $"single tab does not move left either ({Layout()})");

            // Alt+Right pane focus, then the move keys act on the newly focused split.
            Focus(left, docB);
            Press(Key.Right, RawInputModifiers.Alt);
            Check(ActivePane() == right, "Alt+Right focused the right split");
            Press(Key.Right, move);
            Check(Panes().Count == 2 && right.Documents.Single() == single && left.Documents.Count == 2, $"after Alt+Right, Ctrl+Alt+Right on the single tab does nothing ({Layout()})");
            // Leftmost split with one tab.
            Focus(left, docB);
            Wait(Invoke("CloseOtherDocumentsAsync", docB));
            Check(left.Documents.Count == 1, $"left split down to one tab ({Layout()})");
            Focus(left, docB);
            menu = MenuFor(left, docB);
            Check(!menu.Right && !menu.Left, $"2. single-tab leftmost split: no Move items ({menu})");
            Press(Key.Left, move);
            Check(Panes().Count == 2 && left.Documents.Single() == docB, $"2. single-tab leftmost split: Ctrl+Alt+Left does nothing ({Layout()})");
            Press(Key.Right, move);
            Check(Panes().Count == 2 && left.Documents.Single() == docB && right.Documents.Single() == single, $"single tab does not move right into the other split ({Layout()})");

            // Multi-tab split moves.
            Wait(Invoke("OpenPathAsync", c));
            var docC = left.Active!;
            Focus(left, docC);
            menu = MenuFor(left, docC);
            Check(menu.Right && menu.Left, "3. two-tab split shows both Move items");
            Press(Key.Right, move);
            Check(Panes().Count == 2 && right.Documents.Contains(docC) && left.Documents.Single() == docB, $"3. a tab in a multi-tab split moves ({Layout()})");

            // A move that empties its split closes that split (defensive path: call the move on the last tab directly).
            Focus(left, docB);
            var bypass = typeof(MainView).GetMethod("MoveTabToSide", Flags)!;
            Check(left.Documents.Count == 1, "left has one tab");
            var three = Panes().Count;
            bypass.Invoke(view, [left, docB, 1, false]); Settle();
            Check(Panes().Count == three && left.Documents.Single() == docB, "the last tab stays without allowLast");
            bypass.Invoke(view, [left, docB, 1, true]); Settle();
            Check(!Panes().Contains(left) && Panes().Count == three - 1 && Panes()[0].Documents.Any(d => d.Path == b) && ActivePane() == Panes()[0], $"4. the emptied source split closes ({Layout()})");
            Console.WriteLine("ALL MOVETAB SINGLE TESTS PASSED");
        }
        finally
        {
            Shortcuts.ResetAll();
            foreach (var v in window.GetVisualDescendants().OfType<MainView>())
                foreach (var d in (IEnumerable<NoteDocument>)typeof(MainView).GetProperty("AllDocuments", Flags)!.GetValue(v)!) d.SavedText = d.Text;
            window.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}