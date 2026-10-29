using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

internal static class ExplorerDragTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<MainView>("MainView")!;
        var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
        explorer.WatchEnabled = false;
        var tree = explorer.FindControl<TreeView>("FolderTree")!;
        var rootButton = explorer.FindControl<Button>("FolderNameButton")!;
        var tabs = window.GetVisualDescendants().OfType<ListBox>().Single(x => x.Name == "Tabs");
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void RunOperation(string method, params object[] arguments)
        {
            var task = (Task)typeof(MainView).GetMethod(method, flags)!.Invoke(view, arguments)!;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                using var timeout = new CancellationTokenSource(20);
                Dispatcher.UIThread.MainLoop(timeout.Token);
            }
            Check(task.IsCompleted, "operation completes");
            task.GetAwaiter().GetResult();
        }
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-drag-{Guid.NewGuid():N}");
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, "Nested")).FullName;
            var source = Path.Combine(root, "drag.md");
            var target = Path.Combine(folder, "drag.md");
            File.WriteAllText(source, "saved content");
            File.WriteAllText(Path.Combine(folder, "child.txt"), "child");
            RunOperation("LoadRootAsync", root);
            Dispatcher.UIThread.RunJobs();
            var folderItem = tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is FolderEntry { IsDirectory: true });
            var label = folderItem.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Nested");
            void Click(Control control)
            {
                var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
            }
            Click(label);
            Check(folderItem.IsExpanded && ReferenceEquals(tree.SelectedItem, folderItem), "single click selects and expands folder");
            window.UpdateLayout();
            label = folderItem.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Nested");
            Click(label);
            Check(!folderItem.IsExpanded, "second click collapses folder without extra double-click toggle");
            var arrow = folderItem.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First();
            Click(arrow);
            Check(folderItem.IsExpanded, "arrow click expands exactly once");
            Click(arrow);
            Check(!folderItem.IsExpanded, "arrow click collapses exactly once");
            Check(DragDrop.GetAllowDrop(tree) && DragDrop.GetAllowDrop(rootButton), "tree and folder name accept drops");
            RunOperation("OpenPathAsync", source);
            var document = (NoteDocument)tabs.SelectedItem!;
            document.Editor.Text = "unsaved drag edits";
            RunOperation("HandleExplorerRequestAsync", new ExplorerRequest(ExplorerAction.Copy, source));
            Check(ExplorerMove.Destination(root, source, root) is null, "same-directory drop is rejected");
            Check(ExplorerMove.Destination(root, source, Path.GetTempPath()) is null, "out-of-root drop is rejected");
            File.WriteAllText(target, "keep destination");
            Check(ExplorerMove.Destination(root, source, folder) is null, "collision is rejected");
            try
            {
                RunOperation("MoveExplorerFileAsync", new ExplorerMoveRequest(source, folder));
                throw new Exception("Colliding move should fail");
            }
            catch (IOException) { }
            Check(File.ReadAllText(target) == "keep destination" && File.Exists(source), "rejected move preserves both files");
            File.Delete(target);
            RunOperation("MoveExplorerFileAsync", new ExplorerMoveRequest(source, folder));
            Check(!File.Exists(source) && File.ReadAllText(target) == "saved content"
                && document.Path == target && document.Text == "unsaved drag edits" && document.IsDirty,
                "move into folder preserves unsaved edits and retargets the open tab");
            Check(tree.SelectedItem is TreeViewItem { Tag: FolderEntry selected } && selected.Path == target,
                "moved file is revealed and selected");
            Check(typeof(MainView).GetProperty("ExplorerClipboard", flags)!.GetValue(view) is ValueTuple<string, bool> clipboard
                && clipboard.Item1 == target, "clipboard follows moved file");
            RunOperation("SaveAsync", document, false);
            Check(File.ReadAllText(target) == "unsaved drag edits" && !File.Exists(source), "save writes to the new path");
            RunOperation("MoveExplorerFileAsync", new ExplorerMoveRequest(target, root));
            Check(File.Exists(source) && !File.Exists(target) && document.Path == source,
                "move out of folder returns file and tab to root");
        }
        finally { window.Close(); Directory.Delete(root, recursive: true); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
