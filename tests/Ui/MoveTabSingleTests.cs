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

// Bug 95 (revised): the move is blocked only when it changes nothing (one split with one tab, or the outermost
// split's only tab moving outward); any other move is allowed, and a split left with no tabs closes.
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
                if (pane.Active != d) { pane.FindControl<FileTabs>("FileTabs")!.SelectedDocument = d; Settle(); }
                window.MouseDown(d.Editor.TranslatePoint(new Point(20, 10), window)!.Value, MouseButton.Left);
                window.MouseUp(d.Editor.TranslatePoint(new Point(20, 10), window)!.Value, MouseButton.Left); Settle();
                Check(ActivePane() == pane && pane.Active == d, $"clicked editor is active ({Layout()})");
            }
            void ClickMenu(EditorPane pane, NoteDocument d, string name)
            {
                var list = pane.FindControl<FileTabs>("FileTabs")!.FindControl<ListBox>("Tabs")!;
                var item = list.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.DataContext == d);
                var at = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
                window.MouseDown(at, MouseButton.Right); window.MouseUp(at, MouseButton.Right); Settle();
                var menu = list.ContextMenu!;
                menu.Items.OfType<MenuItem>().Single(i => i.Name == name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                menu.Close(); Settle();
            }
            bool Has(EditorPane pane, string path) => pane.Documents.Any(d => d.Path == path);
            Wait(view.OpenFolderPathAsync(root));
            Wait(Invoke("OpenPathAsync", a));
            var left = ActivePane();
            var docA = left.Documents.First(d => d.Path == a);
            Wait(Invoke("CloseOtherDocumentsAsync", docA));

            // 1. One split with one tab: nothing to move to, both directions hidden and no-ops.
            Focus(left, docA);
            var menu = MenuFor(left, docA);
            Check(!menu.Right && !menu.Left, $"1. single split, single tab: both Move items hidden ({menu})");
            Press(Key.Right, move); Press(Key.Left, move);
            Check(Panes().Count == 1 && left.Documents.Single() == docA, $"1. single split, single tab: Ctrl+Alt+Right/Left do nothing ({Layout()})");

            // Two splits via Ctrl+\ : a,b | b'
            Wait(Invoke("OpenPathAsync", b));
            Press(Key.OemPipe, ctrl);
            Check(Panes().Count == 2 && Panes()[1].Documents.Count == 1, $"Ctrl+\\ made a one-tab right split ({Layout()})");
            var right = Panes()[1];
            var single = right.Documents[0];
            Focus(right, single);
            menu = MenuFor(right, single);
            Check(!menu.Right && menu.Left, $"2. rightmost split's only tab: Move to Right hidden (outward = no change), Move to Left shown ({menu})");
            Press(Key.Right, move);
            Check(Panes().Count == 2 && right.Documents.Single() == single, $"2. outward Ctrl+Alt+Right is a no-op ({Layout()})");
            Press(Key.Left, move);
            Check(Panes().Count == 1 && Panes()[0] == left && left.Documents.Count == 2 && Has(left, b) && ActivePane() == left && left.Active?.Path == b,
                $"3. last tab of the right split moves left (same file: joins its open tab) and the emptied split closes ({Layout()})");

            // Leftmost split's only tab: a | b,c  (built with the menu)
            Wait(Invoke("OpenPathAsync", c));
            var docB = left.Documents.First(d => d.Path == b);
            ClickMenu(left, docB, "TabMoveRightMenuItem");
            right = Panes()[1];
            ClickMenu(left, left.Documents.First(d => d.Path == c), "TabMoveRightMenuItem");
            Check(Panes().Count == 2 && left.Documents.Single() == docA && right.Documents.Count == 2, $"two-tab split moves (menu) ({Layout()})");
            Focus(left, docA);
            menu = MenuFor(left, docA);
            Check(menu.Right && !menu.Left, $"4. leftmost split's only tab: Move to Left hidden, Move to Right shown ({menu})");
            Press(Key.Left, move);
            Check(Panes().Count == 2 && left.Documents.Single() == docA, $"4. outward Ctrl+Alt+Left is a no-op ({Layout()})");
            ClickMenu(left, docA, "TabMoveRightMenuItem");
            Check(Panes().Count == 1 && Panes()[0] == right && right.Documents.Contains(docA) && right.Active == docA && ActivePane() == right && !Panes().Contains(left),
                $"5. menu: last tab of the left split moves right and the emptied split closes ({Layout()})");

            // Middle split of three: its only tab moves either way.
            var docC = right.Documents.First(d => d.Path == c);
            Focus(right, docC);
            Press(Key.Right, move);                       // b,a | c
            var third = Panes()[1];
            Focus(right, right.Documents.First(d => d.Path == b));
            Press(Key.Left, move);                        // b | a | c
            var mid = Panes()[1];
            Check(Panes().Count == 3 && mid.Documents.Single() == docA, $"three splits b | a | c ({Layout()})");
            menu = MenuFor(mid, docA);
            Check(menu.Right && menu.Left, "6. middle split's only tab: both Move items shown");
            Focus(mid, docA);
            Press(Key.Right, move);
            Check(Panes().Count == 2 && !Panes().Contains(mid) && Panes()[1] == third && third.Documents.Contains(docA) && ActivePane() == third, $"6. middle split's only tab moves right and its split closes ({Layout()})");
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