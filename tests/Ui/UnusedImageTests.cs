using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;
using Mdhub.Views;

internal static class UnusedImageTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-unused-" + Guid.NewGuid().ToString("N"))).FullName;
        var window = new MainWindow();
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
        void Pump() { using var timeout = new CancellationTokenSource(20); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Until(Func<bool> condition) { var end = DateTime.UtcNow.AddSeconds(15); while (!condition() && DateTime.UtcNow < end) Pump(); Check(condition(), "operation completed"); }
        void Wait(Task task) { Until(() => task.IsCompleted); task.GetAwaiter().GetResult(); }
        string Write(string relative, string text = "") { var path = Path.GetFullPath(Path.Combine(root, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); return path; }
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            var unused = Write(".assets/unused.png");
            Write(".assets/space image.PNG"); Write(".assets/ref.jpg"); Write(".assets/html.svg"); Write(".assets/download.webp");
            Write(".assets/absolute.png"); Write(".assets/hash#file.png"); Write(".assets/unsaved.png"); Write(".assets/keep.txt");
            var absolute = new Uri(Path.Combine(root, ".assets", "absolute.png")).AbsoluteUri;
            Write("note.MD", $"![x](.assets/space%20image.PNG)\n\n![r][pic]\n\n[pic]: .assets/ref.jpg\n\n<img src='.assets/html.svg'>\n\n[download](.assets/download.webp)\n\n![abs]({absolute})\n\n![hash](.assets/hash%23file.png)");
            var childUnused = Write("sub/.assets/unused.png");
            Write("sub/.assets/kept.png"); Write("sub/note.md", "![x](.assets/kept.png)");
            Write(".git/.assets/hidden.png"); Write(".md-trash/.assets/hidden.png");
            var open = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [Path.Combine(root, "note.MD")] = "![x](.assets/unsaved.png)" };
            Check(UnusedImageScanner.Scan(root, false, open).SequenceEqual([unused]), "same-level scan recognizes encoded, reference, HTML, ordinary, absolute and unsaved links; ignores non-images");
            Check(UnusedImageScanner.Scan(root, true, open).ToHashSet().SetEquals([unused, childUnused]), "recursive scan checks each level and excludes internal folders");
            Check(UnusedImageScanner.Scan(Path.Combine(root, "sub"), false).SequenceEqual([childUnused]), "folder scan stays inside selected folder");
            window.Show(); Pump();
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
            explorer.WatchEnabled = false;
            Wait(explorer.LoadRootAsync(root));
            var requests = new List<ExplorerRequest>();
            // Check menu request routing separately from MainView's handler.
            var isolated = new FolderExplorer { WatchEnabled = false };
            Wait(isolated.LoadRootAsync(root));
            isolated.ExplorerRequested += requests.Add;
            var isolatedTree = isolated.FindControl<TreeView>("FolderTree")!;
            void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            foreach (var action in new[] { ExplorerAction.RemoveUnusedImages, ExplorerAction.RemoveUnusedImagesRecursively })
            {
                var name = action.ToString() + "MenuItem";
                Click(isolatedTree.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Name == "Background" + name));
                Check(requests.Last() == new ExplorerRequest(action, root), "background cleanup targets root");
                foreach (var row in isolatedTree.Items.OfType<TreeViewItem>().Where(i => i.Tag is FolderEntry e && (e.Path == Path.Combine(root, "sub") || e.Path == Path.Combine(root, "note.MD"))))
                {
                    Click(row.ContextMenu!.Items.OfType<MenuItem>().Single(i => i.Name == name));
                    var entry = (FolderEntry)row.Tag!;
                    Check(requests.Last() == new ExplorerRequest(action, entry.IsDirectory ? entry.Path : root), "row cleanup targets folder or file parent");
                }
            }
            var recycled = new List<string>();
            typeof(MainView).GetProperty("RecycleOverride", flags)!.SetValue(view, (Action<string>)(p => { recycled.Add(p); File.Delete(p); }));
            Task Start() => (Task)typeof(MainView).GetMethod("HandleExplorerRequestAsync", flags)!.Invoke(view, [new ExplorerRequest(ExplorerAction.RemoveUnusedImagesRecursively, root)])!;
            Window Dialog() { Until(() => window.OwnedWindows.Any(w => w.Name == "UnusedImagesDialog")); return window.OwnedWindows.Single(w => w.Name == "UnusedImagesDialog"); }
            T Find<T>(Window dialog, string name) where T : Control => dialog.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            void ClickButton(Window dialog, string name) => Find<Button>(dialog, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var task = Start(); var dialog = Dialog();
            var text = ((TextBlock)Find<ScrollViewer>(dialog, "UnusedImagePaths").Content!).Text!;
            Check(text.Contains(Path.GetRelativePath(root, childUnused)) && text.Contains(Path.GetRelativePath(root, unused)) && !text.Contains(root), "dialog lists paths relative to scan root");
            Check(Find<TextBlock>(dialog, "UnusedImagesCount").Text!.Contains('3'), "dialog reports total unused images");
            ClickButton(dialog, "CancelUnusedImages"); Wait(task);
            Check(recycled.Count == 0 && File.Exists(unused), "cancel leaves files untouched");
            task = Start(); dialog = Dialog();
            File.AppendAllText(Path.Combine(root, "note.MD"), "\n\n![new](.assets/unused.png)");
            ClickButton(dialog, "RecycleUnusedImages"); Wait(task);
            Check(recycled.ToHashSet().SetEquals([childUnused, Path.Combine(root, ".assets", "unsaved.png")]) && File.Exists(unused), "confirmation recycles only listed images still unused after rechecking");
            explorer.StopWatcher(); isolated.StopWatcher();
        }
        finally
        {
            window.Close();
            Directory.Delete(root, true);
        }
    }
}
