using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;

// Item 22: explorer indentation and same-level alignment of files, folders and the inline rename box.
internal static class ExplorerLayoutTests
{
    public static int Passed;
    public static void Run(bool setup = true)
    {
        if (setup) AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow { Width = 1300, Height = 800 }; window.Show();
        void Pump() { using var timeout = new CancellationTokenSource(100); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Func<bool> done) { var deadline = DateTime.UtcNow.AddSeconds(10); while (!done() && DateTime.UtcNow < deadline) Pump(); }
        Pump();
        var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
        explorer.WatchEnabled = false;
        var tree = explorer.FindControl<TreeView>("FolderTree")!;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-layout-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(root, "Alpha")).FullName;
            Directory.CreateDirectory(Path.Combine(sub, "Nested"));
            File.WriteAllText(Path.Combine(sub, "inner.md"), "x");
            File.WriteAllText(Path.Combine(sub, "Nested", "deep.md"), "x");
            Directory.CreateDirectory(Path.Combine(root, "Empty"));
            File.WriteAllText(Path.Combine(root, "top.md"), "x");
            var load = explorer.LoadRootAsync(root); Wait(() => load.IsCompleted); Pump();
            IEnumerable<TreeViewItem> Nodes(IEnumerable<TreeViewItem> items) { foreach (var i in items) { yield return i; foreach (var c in Nodes(i.Items.OfType<TreeViewItem>())) yield return c; } }
            TreeViewItem Node(string path) => Nodes(tree.Items.OfType<TreeViewItem>()).Single(i => i.Tag is FolderEntry e && e.Path == path);
            Node(sub).IsExpanded = true; Wait(() => Nodes(tree.Items.OfType<TreeViewItem>()).Any(i => i.Tag is FolderEntry e && e.Path == Path.Combine(sub, "Nested"))); Pump();
            Node(Path.Combine(sub, "Nested")).IsExpanded = true; Wait(() => Nodes(tree.Items.OfType<TreeViewItem>()).Any(i => i.Tag is FolderEntry e && e.Path.EndsWith("deep.md"))); Pump(); Pump();
            window.UpdateLayout();
            if (Environment.GetEnvironmentVariable("MDHUB_LAYOUT_DUMP") == "1")
                foreach (var n in new[] { Node(sub), Node(Path.Combine(root, "top.md")), Node(Path.Combine(sub, "inner.md")) })
                    foreach (var v in n.GetVisualDescendants().OfType<Control>().TakeWhile(v => v is not TreeViewItem).Take(25))
                        Console.WriteLine($"DUMP {n.Header} {v.GetType().Name}#{v.Name} x={v.TranslatePoint(default, tree)?.X:0.0} w={v.Bounds.Width:0.0} vis={v.IsVisible} op={v.Opacity}");
            double X(Visual v) => v.TranslatePoint(default, tree)!.Value.X;
            Control Part(TreeViewItem i, string name) => i.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);
            double TextX(TreeViewItem i) => X(Part(i, "PART_HeaderPresenter").GetVisualDescendants().OfType<TextBlock>().First());
            double HeaderX(TreeViewItem i) => X(Part(i, "PART_HeaderPresenter"));
            bool Same(double a, double b) => Math.Abs(a - b) < 0.5;
            var alpha = Node(sub); var empty = Node(Path.Combine(root, "Empty")); var top = Node(Path.Combine(root, "top.md"));
            var nested = Node(Path.Combine(sub, "Nested")); var inner = Node(Path.Combine(sub, "inner.md")); var deep = Node(Path.Combine(sub, "Nested", "deep.md"));
            Console.WriteLine($"INFO x: level0 {HeaderX(alpha)}/{HeaderX(top)}, level1 {HeaderX(nested)}/{HeaderX(inner)}, level2 {HeaderX(deep)}");
            Check(Same(HeaderX(alpha), HeaderX(top)) && Same(HeaderX(empty), HeaderX(top)) && Same(TextX(alpha), TextX(top)), "level 0: folders and files start at the same x");
            Check(Same(HeaderX(nested), HeaderX(inner)) && Same(TextX(nested), TextX(inner)), "level 1: folder and file start at the same x");
            var step1 = HeaderX(inner) - HeaderX(top); var step2 = HeaderX(deep) - HeaderX(inner);
            Check(step1 >= 8 && step1 <= 12 && Same(step1, step2), $"indent per level is 8-12px and constant (got {step1:0.#}, {step2:0.#})");
            var fileSlot = Part(top, "PART_ExpandCollapseChevronContainer"); var folderSlot = Part(alpha, "PART_ExpandCollapseChevronContainer");
            Check(fileSlot.IsVisible && Same(fileSlot.Bounds.Width, folderSlot.Bounds.Width) && fileSlot.Bounds.Width > 0 && Same(X(fileSlot), X(folderSlot)),
                "files reserve the chevron slot");
            foreach (var (row, sibling, level) in new[] { (top, alpha, 0), (inner, nested, 1), (deep, deep, 2), (nested, inner, 1) })
            {
                var before = TextX(row);
                tree.SelectedItem = row; for (var k = 0; k < 6; k++) Pump();
                explorer.BeginRename(((FolderEntry)row.Tag!).Path); Pump(); Pump(); window.UpdateLayout();
                var box = row.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "ExplorerRenameBox");
                var boxText = X(box.GetVisualDescendants().OfType<TextPresenter>().First());
                Console.WriteLine($"INFO rename level {level}: text {before} box text {boxText}");
                Check(Same(boxText, before) && Same(boxText, TextX(sibling)), $"inline rename text lines up with the row and its level (level {level}, {((FolderEntry)row.Tag!).IsDirectory switch { true => "folder", _ => "file" }})");
                box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Pump();
            }
            foreach (var d in window.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<Mdhub.Models.NoteDocument>()) d.SavedText = d.Text;
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Passed++;
        Console.WriteLine("PASS: " + message);
    }
}