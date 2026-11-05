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
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 89: tab context menu Move to Right/Left Split.
internal static class MoveTabTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SessionManager.Enabled = false;
        Shortcuts.ResetAll();
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-movetab-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string Make(string name, string text) { var p = Path.Combine(root, name); File.WriteAllText(p, text); return p; }
        var a = Make("a.md", "alpha one\nalpha two"); var b = Make("b.md", "bravo"); var c = Make("c.md", "charlie");
        var window = new MainWindow { Width = 1600, Height = 800 };
        MainWindow? restored = null;
        void Pump() { using var timeout = new CancellationTokenSource(35); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task task) { var until = DateTime.UtcNow.AddSeconds(12); while (!task.IsCompleted && DateTime.UtcNow < until) Pump(); if (!task.IsCompleted) throw new Exception("Operation timed out"); task.GetAwaiter().GetResult(); Pump(); }
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
        try
        {
            window.Show(); Pump();
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            EditorPane ActivePane() => (EditorPane)typeof(MainView).GetProperty("ActivePane", Flags)!.GetValue(view)!;
            IReadOnlyList<EditorPane> Panes() => ((IReadOnlyList<EditorPane>)typeof(MainView).GetProperty("EditorPanes", Flags)!.GetValue(view)!).ToList();
            Task Invoke(string method, params object?[] args) => (Task)typeof(MainView).GetMethod(method, Flags)!.Invoke(view, args)!;
            void Move(EditorPane pane, NoteDocument d, int dir) { typeof(MainView).GetMethod("MoveTabToSide", Flags)!.Invoke(view, [pane, d, dir]); Pump(); Pump(); }
            NoteDocument Doc(EditorPane pane, string path) => pane.Documents.First(d => d.Path is { } p && Path.GetFullPath(p) == Path.GetFullPath(path));
            Wait(view.OpenFolderPathAsync(root));
            Wait(Invoke("OpenPathAsync", a));
            var first = ActivePane();
            var docA = Doc(first, a);
            Wait(Invoke("CloseOtherDocumentsAsync", docA));

            // Visibility rule.
            var list = first.FindControl<FileTabs>("FileTabs")!.FindControl<ListBox>("Tabs")!;
            var menu = list.ContextMenu!;
            MenuItem Item(string name) => menu.Items.OfType<MenuItem>().Single(i => i.Name == name);
            void OpenMenu(NoteDocument d)
            {
                list.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.DataContext == d).RaiseEvent(new ContextRequestedEventArgs()); Pump();
            }
            Check(first.Documents.Count == 1, "Single tab in the split");
            OpenMenu(docA);
            Check(!Item("TabMoveRightMenuItem").IsVisible && !Item("TabMoveLeftMenuItem").IsVisible, "Move items hidden when the only split has one tab (bug 95: nothing to move to)");
            menu.Close(); Pump();
            Wait(Invoke("OpenPathAsync", b));
            Wait(Invoke("OpenPathAsync", c));
            OpenMenu(docA);
            Check(Item("TabMoveRightMenuItem").IsVisible && Item("TabMoveLeftMenuItem").IsVisible, "Move items visible with two or more tabs");
            Check(Item("TabMoveRightMenuItem").Header as string == Loc.T("MoveToRightSplit") && Loc.T("MoveToRightSplit") == "Move to Right Split"
                && Item("TabMoveLeftMenuItem").Header as string == "Move to Left Split", "Localized headers");
            Check(Item("TabMoveRightMenuItem").Padding == LocResources.MenuItemPadding, "Standard menu padding");

            // Dirty state, undo, caret and selection travel with the tab; no split on the right -> a new one.
            docA.Editor.CaretIndex = docA.Editor.Text!.Length;
            docA.Editor.SelectedText = " EDIT"; Pump();
            docA.Editor.SelectionStart = 2; docA.Editor.SelectionEnd = 5; Pump();
            var editorControl = docA.Editor;
            Item("TabMoveRightMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Close(); Pump(); Pump();
            Check(Panes().Count == 2 && Panes()[0] == first, "Moving right with no right split creates one");
            var right = Panes()[1];
            Check(right.Documents.Count == 1 && right.Documents[0] == docA && !first.Documents.Contains(docA), "The tab moved (removed from its split, only tab in the new one)");
            Check(ActivePane() == right && right.Active == docA && right.FindControl<FileTabs>("FileTabs")!.SelectedDocument == docA, "Moved tab is active in the target split");
            Check(docA.Editor == editorControl && docA.IsDirty && docA.Text.EndsWith(" EDIT"), "Same document model and editor: unsaved edit kept, no reload");
            Check(docA.Editor.SelectionStart == 2 && docA.Editor.SelectionEnd == 5, "Selection kept");
            Check(docA.Editor.IsFocused || docA.Editor.IsKeyboardFocusWithin, "Editor focused in the target split");
            docA.Undo(); Pump();
            Check(docA.Text == "alpha one\nalpha two", "Undo history kept after the move");
            docA.Redo(); Pump();
            Check(docA.Text.EndsWith(" EDIT"), "Redo kept");

            // Move to an existing split (left neighbour).
            var docB = Doc(first, b);
            Move(first, docB, 1);
            Check(Panes().Count == 2 && right.Documents.Count == 2 && right.Active == docB && first.Documents.Count == 1, "Move right into the existing split");
            Move(right, docB, -1);
            Check(Panes().Count == 2 && first.Documents.Contains(docB) && first.Active == docB && ActivePane() == first, "Move left into the existing split");
            // No split on the left -> a new one at the far left.
            Move(first, docB, -1);
            Check(Panes().Count == 3 && Panes()[0].Documents.Single() == docB && Panes()[1] == first && Panes()[2] == right, "Moving left with no left split creates one on the left");
            var left = Panes()[0];

            // Duplicate in target: b is open in the left split and again in the middle one (opening a path that is
            // already open shares its NoteBuffer). The middle view is dirty when it moves left.
            Check(left.Documents.Single() == docB && first.Documents.Count == 1 && right.Documents.Single() == docA, "Layout b | c | a");
            ActivatePaneOf(view, first);
            Wait(Invoke("OpenPathAsync", b));
            var dup = first.Active!;
            Check(dup != docB && dup.Buffer == docB.Buffer && first.Documents.Count == 2, "Second view of b in the middle split");
            dup.Editor.CaretIndex = 0; dup.Editor.SelectedText = "DIRTY "; Pump();
            Check(dup.IsDirty && docB.IsDirty && docB.Text.StartsWith("DIRTY "), "Dirty edit in the moving view is shared with the target view");
            int CountB() => ((IEnumerable<NoteDocument>)typeof(MainView).GetProperty("AllDocuments", Flags)!.GetValue(view)!).Count(d => d.Buffer == docB.Buffer);
            var countB = CountB();
            var dupSource = first;
            Move(first, dup, -1);
            Check(left.Documents.Count(d => d.Buffer == docB.Buffer) == 1 && CountB() == countB - 1 && !dupSource.Documents.Contains(dup), "Duplicate in target: moving tab closes, no second tab of the same file");
            Check(ActivePane() == left && left.Active == docB && docB.IsDirty && docB.Text.StartsWith("DIRTY "), "Existing tab focused and still holds the dirty edits");
            docB.Undo(); Pump();
            Check(docB.Text == "bravo", "Undo of the edit made in the closed view still works in the existing tab");
            docB.Redo(); Pump();

            // Session restore remembers the layout.
            var session = view.CaptureSession();
            Check(session.Panes is { Count: > 1 } && session.Panes.Count == Panes().Count, $"Session captures {Panes().Count} splits");
            var layout = Panes().Select(p => string.Join(",", p.Documents.Select(d => Path.GetFileName(d.Path)))).ToList();
            var activeIndex = Panes().ToList().IndexOf(ActivePane());
            restored = new MainWindow { Width = 1600, Height = 800 };
            restored.Show(); Pump();
            var view2 = restored.GetVisualDescendants().OfType<MainView>().Single();
            var t = view2.RestoreSessionAsync(session, null);
            var until = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < until) Pump(); t.GetAwaiter().GetResult(); Pump();
            var panes2 = ((IReadOnlyList<EditorPane>)typeof(MainView).GetProperty("EditorPanes", Flags)!.GetValue(view2)!).ToList();
            var layout2 = panes2.Select(p => string.Join(",", p.Documents.Select(d => Path.GetFileName(d.Path)))).ToList();
            Check(layout2.SequenceEqual(layout), $"Restored layout matches [{string.Join(" | ", layout)}] got [{string.Join(" | ", layout2)}]");
            Check(panes2.IndexOf((EditorPane)typeof(MainView).GetProperty("ActivePane", Flags)!.GetValue(view2)!) == activeIndex, "Restored active split");
            var old = System.Text.Json.JsonSerializer.Deserialize<WindowSession>("{\"Tabs\":[\"x.md\"],\"ActiveTab\":0}")!;
            Check(old.Panes is null && old.ActivePane == 0, "Old session files without pane layout still load");
            Console.WriteLine("ALL MOVETAB TESTS PASSED");
        }
        finally
        {
            foreach (var d in window.GetVisualDescendants().OfType<MainView>().SelectMany(v => ((IEnumerable<NoteDocument>)typeof(MainView).GetProperty("AllDocuments", Flags)!.GetValue(v)!))) d.SavedText = d.Text;
            restored?.Close();
            window.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static void ActivatePaneOf(MainView view, EditorPane pane) => typeof(MainView).GetMethod("ActivatePane", Flags)!.Invoke(view, [pane]);
}
