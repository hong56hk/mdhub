using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;

// Bug 54: refreshing the explorer (add/rename/delete/paste, file watcher) updates rows in place:
// expanded folders, the selection and the scroll offset survive and kept rows keep their TreeViewItem.
internal static class ExplorerRefreshTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var timeout = new CancellationTokenSource(30); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Await(Task task) { var end = DateTime.UtcNow.AddSeconds(10); while (!task.IsCompleted && DateTime.UtcNow < end) Pump(); task.GetAwaiter().GetResult(); }
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-explorer-refresh-" + Guid.NewGuid().ToString("N"))).FullName;
        var explorer = new FolderExplorer { WatchEnabled = false };
        var window = new Window { Width = 400, Height = 300, Content = explorer };
        try
        {
            var a = Directory.CreateDirectory(Path.Combine(root, "a")).FullName;
            var b = Directory.CreateDirectory(Path.Combine(a, "b")).FullName;
            var note = Path.Combine(b, "note.md"); File.WriteAllText(note, "x");
            for (var i = 0; i < 40; i++) File.WriteAllText(Path.Combine(b, $"f{i:00}.md"), "");
            File.WriteAllText(Path.Combine(root, "z.md"), "");
            window.Show(); Pump();
            Await(explorer.LoadRootAsync(root));
            Await(explorer.RevealAsync(note, focus: true)); Pump(); Pump();
            var tree = explorer.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "FolderTree");
            IEnumerable<TreeViewItem> Nodes(IEnumerable<object?> items) => items.OfType<TreeViewItem>().SelectMany(i => Nodes(i.Items).Prepend(i));
            TreeViewItem? Row(string path) => Nodes(tree.Items).FirstOrDefault(i => i.Tag is FolderEntry e && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            var rowA = Row(a)!; var rowB = Row(b)!; var rowNote = Row(note)!;
            Check(rowA.IsExpanded && rowB.IsExpanded && tree.SelectedItem == rowNote, "setup: a/b expanded and note.md selected");
            var scroller = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroller.Offset = new Vector(0, 120); Pump(); Pump();
            var offset = scroller.Offset.Y;
            var before = Nodes(tree.Items).ToList();

            File.WriteAllText(Path.Combine(b, "added.md"), "");
            File.WriteAllText(Path.Combine(root, "new.md"), "");
            Directory.CreateDirectory(Path.Combine(a, "c"));
            Await(explorer.RefreshAsync()); Pump(); Pump();
            Check(rowA.IsExpanded && rowB.IsExpanded, "expanded folders stay expanded after an add");
            Check(Row(a) == rowA && Row(b) == rowB && Row(note) == rowNote && before.All(r => Nodes(tree.Items).Contains(r)), "existing rows keep their TreeViewItem (no rebuild)");
            Check(tree.SelectedItem == rowNote, "selection survives an add");
            Check(Math.Abs(scroller.Offset.Y - offset) < 1, $"scroll offset survives an add ({offset} -> {scroller.Offset.Y})");
            Check(Row(Path.Combine(b, "added.md")) is not null && Row(Path.Combine(root, "new.md")) is not null && Row(Path.Combine(a, "c")) is not null, "added items appear");
            var names = rowB.Items.OfType<TreeViewItem>().Select(i => Path.GetFileName(((FolderEntry)i.Tag!).Path)).ToList();
            Check(names.IndexOf("added.md") == 0 && names.IndexOf("note.md") == names.IndexOf("f39.md") + 1, $"new file is inserted in sorted position ({string.Join(",", names.Take(3))})");
            var rootNames = tree.Items.OfType<TreeViewItem>().Select(i => Path.GetFileName(((FolderEntry)i.Tag!).Path)).ToList();
            Check(rootNames.SequenceEqual(["a", "new.md", "z.md"]), $"root order is folders then files ({string.Join(",", rootNames)})");

            File.Delete(Path.Combine(root, "z.md"));
            File.Move(Path.Combine(b, "f00.md"), Path.Combine(b, "renamed.md"));
            Await(explorer.RefreshAsync()); Pump(); Pump();
            Check(Row(Path.Combine(root, "z.md")) is null && Row(Path.Combine(b, "f00.md")) is null && Row(Path.Combine(b, "renamed.md")) is not null, "deleted and renamed rows update in place");
            Check(rowA.IsExpanded && rowB.IsExpanded && Row(a) == rowA && tree.SelectedItem == rowNote, "expansion and selection survive delete and rename");
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}