using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 45: context menus (Undo/Redo, first-open padding, hand cursor) and the explorer rows' cursor.
internal static class MenuTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        bool Until(Func<bool> ok) { var end = DateTime.UtcNow.AddSeconds(8); while (!ok() && DateTime.UtcNow < end) Pump(); return ok(); }
        string CursorName(Control c) { var cur = c.Cursor; if (cur is null) return "none"; var f = typeof(Cursor).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(x => x.FieldType == typeof(string)); return f?.GetValue(cur) as string ?? cur.ToString() ?? "?"; }
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-menu-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        var file = Path.Combine(root, "sub", "note.md"); File.WriteAllText(file, "hello");
        var w = new MainWindow { Width = 1100, Height = 700 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var explorer = w.GetVisualDescendants().OfType<FolderExplorer>().Single();
            var tree = explorer.FindControl<TreeView>("FolderTree")!;
            NoteDocument? Active() => (NoteDocument?)typeof(MainView).GetField("_active", Flags)!.GetValue(v);
            Task OpenFile(string p) => (Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [p])!;
            MenuItem ItemOf(ContextMenu m, string name) => m.Items.OfType<MenuItem>().Single(i => i.Name == name);
            var hand = new Cursor(StandardCursorType.Hand);
            var handName = CursorName(new Border { Cursor = hand });
            var ibeamName = CursorName(new Border { Cursor = new Cursor(StandardCursorType.Ibeam) });
            Check(handName != ibeamName, $"Cursors can be told apart ({handName} vs {ibeamName})");

            Wait(v.OpenFolderPathAsync(root));
            Wait(OpenFile(file));
            var editor = Active()!.Editor;
            var menu = editor.ContextMenu!;
            // Padding of the first open, measured from the menu's edge to its first and last items.
            (double l, double t, double r, double b) Measure()
            {
                var items = menu.Items.OfType<Control>().Where(c => c.IsVisible).ToList();
                var first = items.First().TranslatePoint(new Point(0, 0), menu)!.Value;
                var lastC = items.Last();
                var last = lastC.TranslatePoint(new Point(lastC.Bounds.Width, lastC.Bounds.Height), menu)!.Value;
                return (first.X, first.Y, menu.Bounds.Width - last.X, menu.Bounds.Height - last.Y);
            }
            menu.Open(editor); Pump(); Pump();
            var border = menu.GetVisualDescendants().OfType<Border>().First();
            var edge = border.BorderThickness;
            var firstOpen = Measure();
            Console.WriteLine($"first open padding {firstOpen} border {edge}");
            var pad = LocResources.MenuPadding;
            Check(pad == new Thickness(2) && Math.Abs(firstOpen.l - pad.Left - edge.Left) < 0.6 && Math.Abs(firstOpen.t - pad.Top - edge.Top) < 0.6 && Math.Abs(firstOpen.r - pad.Right - edge.Right) < 0.6 && Math.Abs(firstOpen.b - pad.Bottom - edge.Bottom) < 0.6,
                "The first open of the editor menu already has its 2px padding on all four sides");
            var names = menu.Items.OfType<Control>().Select(c => c is MenuItem m ? m.Name : "-").ToList();
            Check(names.Take(3).SequenceEqual(new[] { "EditorUndoMenuItem", "EditorRedoMenuItem", "-" }), "Undo and Redo are at the top, followed by a separator");
            var undo = ItemOf(menu, "EditorUndoMenuItem"); var redo = ItemOf(menu, "EditorRedoMenuItem");
            Check(undo.InputGesture?.ToString() == "Ctrl+Z" && redo.InputGesture?.ToString() == "Ctrl+Y", $"Undo and Redo show their shortcuts ({undo.InputGesture}, {redo.InputGesture})");
            Check((undo.Header as string) == Loc.T("MenuUndo") && (redo.Header as string) == Loc.T("MenuRedo"), "Undo and Redo use the localized menu labels");
            Check(!undo.IsEnabled && !redo.IsEnabled, "Undo and Redo are disabled before any edit");
            Check(menu.Items.OfType<MenuItem>().All(i => CursorName(i) == handName), "Editor menu items show the hand cursor");
            menu.Close(); Pump();
            menu.Open(editor); Pump(); Pump();
            var second = Measure();
            Check(second == firstOpen, "A later open has exactly the same padding as the first");
            menu.Close(); Pump();

            editor.Focus(); editor.CaretIndex = editor.Text!.Length; Pump();
            w.KeyTextInput("!"); Pump();
            Check(editor.Text == "hello!", "Typing records an edit");
            menu.Open(editor); Pump();
            Check(undo.IsEnabled && !redo.IsEnabled, "After an edit Undo is enabled and Redo is not");
            menu.Close(); Pump();
            undo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Check(editor.Text == "hello", "Undo from the menu reverts the edit");
            menu.Open(editor); Pump();
            Check(redo.IsEnabled, "After Undo, Redo is enabled");
            menu.Close(); Pump();
            redo.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Check(editor.Text == "hello!", "Redo from the menu reapplies the edit");

            var preview = (Control)typeof(MainView).GetField("MdEditor", Flags)!.GetValue(v)!;
            preview.ContextMenu!.Open(preview); Pump();
            Check(preview.ContextMenu.Padding == LocResources.MenuPadding && preview.ContextMenu.Items.OfType<MenuItem>().All(i => CursorName(i) == handName), "The preview menu has the shared padding and hand-cursor items");
            preview.ContextMenu.Close(); Pump();

            var row = tree.GetVisualDescendants().OfType<TreeViewItem>().First(t => t.Tag is FolderEntry { IsDirectory: true });
            row.ContextMenu!.Open(row); Pump();
            Check(row.ContextMenu.Padding == LocResources.MenuPadding && row.ContextMenu.Items.OfType<MenuItem>().All(i => CursorName(i) == handName), "The explorer item menu has the shared padding and hand-cursor items");
            row.ContextMenu.Close(); Pump();
            tree.ContextMenu!.Open(tree); Pump();
            Check(tree.ContextMenu.Padding == LocResources.MenuPadding && tree.ContextMenu.Items.OfType<MenuItem>().All(i => CursorName(i) == handName), "The explorer background menu has the shared padding and hand-cursor items");
            tree.ContextMenu.Close(); Pump();

            var top = w.GetVisualDescendants().OfType<TopMenu>().Single();
            var topItems = top.GetLogicalDescendants().OfType<MenuItem>().ToList();
            Check(topItems.Count > 10 && topItems.All(i => CursorName(i) == handName), "Top menu items show the hand cursor");

            row.IsExpanded = true; Pump();
            var fileRow = Until(() => tree.GetVisualDescendants().OfType<TreeViewItem>().Any(t => t.Tag is FolderEntry { IsDirectory: false })) ? tree.GetVisualDescendants().OfType<TreeViewItem>().First(t => t.Tag is FolderEntry { IsDirectory: false }) : null;
            Check(CursorName(row) == handName && fileRow is not null && CursorName(fileRow) == handName, "Folder and file rows show the hand cursor");
            var chevron = row.GetVisualDescendants().OfType<ToggleButton>().FirstOrDefault();
            Check(chevron is not null && CursorName(chevron) == handName, "The folder chevron shows the hand cursor");
            tree.SelectedItem = fileRow; Pump();
            explorer.BeginRename(); Pump();
            var box = Until(() => explorer.IsRenaming) ? fileRow!.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsVisible) : null;
            Check(box is not null && CursorName(box) == ibeamName, "The inline rename box keeps the I-beam cursor");
            w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Pump();
        }
        finally
        {
            foreach (var o in w.OwnedWindows.ToList()) o.Close();
            try { w.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"Menu tests: {passed} passed");
    }
}