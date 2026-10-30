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

internal static class RenameTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow(); window.Show();
        void Pump() { using var timeout = new CancellationTokenSource(100); Dispatcher.UIThread.MainLoop(timeout.Token); }
        Pump();
        var view = window.FindControl<MainView>("MainView")!;
        var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
        explorer.WatchEnabled = false;
        var tree = explorer.FindControl<TreeView>("FolderTree")!;
        var tabs = window.GetVisualDescendants().OfType<ListBox>().Single(c => c.Name == "Tabs");
        TextBox Box() => explorer.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ExplorerRenameBox");
        void Press(Control target, Key key) { target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key }); Pump(); }
        void Wait(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!done() && DateTime.UtcNow < deadline) Pump();
            Check(done(), "operation completes");
        }
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-rename-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var source = Path.Combine(root, "sample.test.md");
            File.WriteAllText(source, "original");
            File.WriteAllText(Path.Combine(root, "existing.md"), "keep");
            var load = explorer.LoadRootAsync(root); Wait(() => load.IsCompleted); load.GetAwaiter().GetResult(); Pump();
            tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is FolderEntry e && e.Path == source);
            Wait(() => tabs.SelectedItem is NoteDocument { Path: var p } && p == source);
            Pump();
            var document = (NoteDocument)tabs.SelectedItem!;
            document.Editor.Text = "unsaved edits";
            document.Editor.Focus(); Pump();
            Press(view, Key.F2);
            Check(!explorer.IsRenaming, "F2 does nothing while the editor has focus");
            explorer.FocusTree(); Pump();
            Press(view, Key.F2);
            Check(Box().Text == "sample.test.md" && Box().SelectedText == "sample.test" && window.OwnedWindows.Count == 0,
                "F2 starts inline rename and selects filename without extension");
            Box().Text = "cancelled.md";
            Press(Box(), Key.Escape);
            Check(!explorer.IsRenaming && File.Exists(source), "Escape cancels rename");
            var item = (TreeViewItem)tree.SelectedItem!;
            item.ContextMenu!.Items.OfType<MenuItem>().Single(m => m.Name == "RenameMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Check(explorer.IsRenaming && Box().IsFocused, "context menu starts inline rename");
            Box().Text = "existing.md";
            Press(Box(), Key.Enter);
            Wait(() => !Box().IsReadOnly);
            Check(explorer.IsRenaming && File.ReadAllText(Path.Combine(root, "existing.md")) == "keep" && window.OwnedWindows.Count == 0,
                "duplicate name stays editable and does not overwrite");
            Box().Text = "bad/name.md";
            Press(Box(), Key.Enter);
            Check(explorer.IsRenaming && File.Exists(source), "invalid name remains editable");
            Box().Text = "renamed.md";
            Press(Box(), Key.Enter);
            Wait(() => !explorer.IsRenaming);
            var target = Path.Combine(root, "renamed.md");
            Check(!File.Exists(source) && File.Exists(target) && document.Path == target && document.Text == "unsaved edits" && document.IsDirty,
                "Enter renames and preserves open tab with unsaved edits");
            Check(((TreeViewItem)tree.SelectedItem!).Tag is FolderEntry { Path: var sel } && sel == target, "renamed item stays selected");
            Check(view.CaptureSession().Tabs.Contains(target) && !view.CaptureSession().Tabs.Contains(source), "session points at the new path");

            TreeViewItem Node(string path) => TreeNodesOf(tree).Single(i => i.Tag is FolderEntry e && e.Path == path);
            void Start(string path) { tree.SelectedItem = Node(path); for (var k = 0; k < 6; k++) Pump(); explorer.FocusTree(); Pump(); Press(view, Key.F2); Pump(); }

            // Selection rules
            Check(FolderExplorer.RenameSelectionLength("notes.md", false) == 5 && FolderExplorer.RenameSelectionLength(".gitignore", false) == 10
                && FolderExplorer.RenameSelectionLength("README", false) == 6 && FolderExplorer.RenameSelectionLength("my.folder", true) == 9
                && FolderExplorer.RenameSelectionLength("a.tar.gz", false) == 5, "selection length rules (stem, dotfile, no extension, folder)");
            var dot = Path.Combine(root, ".gitignore"); File.WriteAllText(dot, "bin");
            var folder = Directory.CreateDirectory(Path.Combine(root, "my.folder")).FullName;
            var reload = explorer.RefreshAsync(); Wait(() => reload.IsCompleted); Pump();
            Start(dot);
            Check(explorer.IsRenaming && Box().SelectedText == ".gitignore", "dotfile selects the whole name");
            Press(Box(), Key.Escape);
            Start(folder);
            Check(explorer.IsRenaming && Box().SelectedText == "my.folder", "folder selects the whole name");
            Press(Box(), Key.Escape);

            // Empty name: error inline and in the status bar, box stays open
            string? message = null; explorer.MessageChanged += m => message = m;
            Start(target);
            Box().Text = "   ";
            Press(Box(), Key.Enter);
            Check(explorer.IsRenaming && ToolTip.GetTip(Box()) is string tip && tip.Length > 0 && message == tip && Box().Classes.Contains("renameError"),
                "empty name shows an inline error and keeps the box open");
            Box().Text = "x"; Pump();
            Check(ToolTip.GetTip(Box()) is null, "editing clears the error");
            Box().Text = "existing.md";
            Press(Box(), Key.Enter);
            Check(explorer.IsRenaming && message!.Contains("already exists"), "duplicate name reports the clash before touching the disk");
            Box().Text = "bad:name?.md";
            Press(Box(), Key.Enter);
            Check(explorer.IsRenaming && File.Exists(target), "invalid characters keep the box open");

            // Unchanged name: Enter closes, nothing happens
            Box().Text = "renamed.md"; message = null;
            Press(Box(), Key.Enter); Pump();
            Check(!explorer.IsRenaming && File.Exists(target) && document.Path == target && message is null, "unchanged name does nothing");

            // Losing focus commits
            Start(target);
            Box().Text = "blurred.md";
            document.Editor.Focus(); Pump();
            Wait(() => !explorer.IsRenaming);
            var blurred = Path.Combine(root, "blurred.md");
            Check(File.Exists(blurred) && !File.Exists(target) && document.Path == blurred && document.Editor.IsFocused, "losing focus commits and leaves focus where it went");
            Check(((TreeViewItem)tree.SelectedItem!).Tag is FolderEntry { Path: var sel2 } && sel2 == blurred && view.CaptureSession().Tabs.Contains(blurred),
                "blur rename keeps the item selected and updates the session");

            // Losing focus with an invalid name keeps the box open
            Start(blurred);
            Box().Text = "";
            document.Editor.Focus(); Pump(); Pump();
            Check(explorer.IsRenaming && File.Exists(blurred), "losing focus with an invalid name keeps the box open");
            Box().Focus(); Pump();
            Press(Box(), Key.Escape);
            Check(!explorer.IsRenaming && File.Exists(blurred), "Escape after an error cancels");

            // Case-only rename
            Start(blurred);
            Box().Text = "Blurred.md";
            Press(Box(), Key.Enter);
            Wait(() => !explorer.IsRenaming);
            var cased = Path.Combine(root, "Blurred.md");
            Check(Directory.GetFiles(root).Any(f => Path.GetFileName(f) == "Blurred.md") && document.Path == cased, "case-only rename is allowed");

            // Folder rename moves open tabs inside it
            var inner = Path.Combine(folder, "inner.md"); File.WriteAllText(inner, "in");
            var openInner = view.GetType().GetMethod("OpenPathAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [inner]) as Task;
            Wait(() => openInner!.IsCompleted); Pump();
            var innerDoc = tabs.Items.OfType<NoteDocument>().Single(d => d.Path == inner);
            reload = explorer.RefreshAsync(); Wait(() => reload.IsCompleted); Pump();
            Start(folder);
            Box().Text = "renamed folder";
            Press(Box(), Key.Enter);
            Wait(() => !explorer.IsRenaming);
            var newInner = Path.Combine(root, "renamed folder", "inner.md");
            Check(innerDoc.Path == newInner && view.CaptureSession().Tabs.Contains(newInner), "folder rename updates tabs inside it and the session");

            Check(Shortcuts.All.Any(s => s.Id == "explorer.rename" && s.Format(false) == "F2"), "F2 is listed in the shortcut list");
            Check(typeof(DialogService).GetMethod("PromptNameAsync")!.GetParameters().Length == 4, "rename popup mode removed from the name dialog");
            foreach (var d in tabs.Items.OfType<NoteDocument>()) d.SavedText = d.Text;
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
    private static IEnumerable<TreeViewItem> TreeNodesOf(TreeView tree)
    {
        IEnumerable<TreeViewItem> Walk(IEnumerable<TreeViewItem> items) { foreach (var i in items) { yield return i; foreach (var c in Walk(i.Items.OfType<TreeViewItem>())) yield return c; } }
        return Walk(tree.Items.OfType<TreeViewItem>());
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
