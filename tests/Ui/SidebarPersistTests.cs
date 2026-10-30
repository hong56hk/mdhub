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
using Mdhub.Services;
using Mdhub.Views;

// Item 61: global search results survive a trip to the explorer and back (query, results, expanded groups, scroll,
// no re-run); the explorer toolbar's Collapse All sits right of Reload and collapses every expanded folder.
internal static class SidebarPersistTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    public static void Run()
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow { Width = 1200, Height = 800 }; window.Show();
        void Pump() { using var timeout = new CancellationTokenSource(30); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(int ms) { using var timeout = new CancellationTokenSource(ms); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Await(Task task) { var deadline = DateTime.UtcNow.AddSeconds(15); while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump(); Check(task.IsCompleted, "operation completes"); task.GetAwaiter().GetResult(); }
        Pump();
        var view = window.FindControl<MainView>("MainView")!;
        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
        var search = Find<GlobalSearch>("GlobalSearch");
        int Runs() => (int)typeof(GlobalSearch).GetProperty("SearchRuns", Flags)!.GetValue(search)!;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-sidebar-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            for (var i = 0; i < 40; i++) File.WriteAllText(Path.Combine(root, $"n{i:00}.md"), "gamma\ngamma two\n");
            var deep = Directory.CreateDirectory(Path.Combine(root, "a", "b", "c")).FullName;
            File.WriteAllText(Path.Combine(deep, "deep.md"), "x");
            Await((Task)typeof(MainView).GetMethod("LoadRootAsync", Flags)!.Invoke(view, [root])!);
            void Tab(string name) { Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Wait(400); }
            Tab("SearchTab");
            var query = Find<TextBox>("GlobalQuery");
            query.Text = "gamma"; Pump();
            Await(search.SearchAsync()); Wait(400);
            var tree = Find<TreeView>("GlobalResults");
            var result = search.Result; var runs = Runs();
            Check(result.Files.Count == 40, $"search finds every file ({result.Files.Count})");
            var groups = tree.Items.OfType<TreeViewItem>().ToList();
            groups[0].IsExpanded = false; groups[3].IsExpanded = false; Pump();
            var scroll = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Offset = new Vector(0, 300); window.UpdateLayout(); Pump();
            var offset = scroll.Offset;
            Check(offset.Y > 100, $"results scroll ({offset})");
            for (var trip = 1; trip <= 2; trip++)
            {
                Tab("ExplorerTab");
                Check(!search.IsVisible && Find<FolderExplorer>("FolderExplorer").IsVisible, $"trip {trip}: the explorer is shown");
                Tab("SearchTab"); window.UpdateLayout(); Pump();
                var same = tree.Items.OfType<TreeViewItem>().ToList();
                Check(query.Text == "gamma" && Runs() == runs && ReferenceEquals(search.Result, result) && same.SequenceEqual(groups),
                    $"trip {trip}: query and results are kept and the search does not re-run (runs {runs} -> {Runs()})");
                Check(!same[0].IsExpanded && !same[3].IsExpanded && same.Where((_, i) => i is not 0 and not 3).All(g => g.IsExpanded), $"trip {trip}: expanded/collapsed groups are kept");
                Check(Math.Abs(scroll.Offset.Y - offset.Y) <= 1, $"trip {trip}: the scroll position is kept ({offset} -> {scroll.Offset})");
            }
            query.Text = "two"; Wait(700);
            Check(Runs() == runs + 1 && search.Result.Count == 40 && !ReferenceEquals(search.Result, result), "a new query replaces the results");
            var runs2 = Runs();
            Tab("ExplorerTab"); Tab("SearchTab");
            Check(Runs() == runs2, "the new results are kept too");

            // Collapse All: right of Reload, same icon style, themed, hand cursor, localized tooltip.
            Tab("ExplorerTab");
            var reload = Find<Button>("ReloadFolderButton"); var collapse = Find<Button>("CollapseAllButton");
            Check(collapse.Parent == reload.Parent && collapse.Parent is StackPanel p && p.Children.IndexOf(collapse) == p.Children.IndexOf(reload) + 1
                  && collapse.TranslatePoint(default, window)!.Value.X > reload.TranslatePoint(default, window)!.Value.X, "Collapse All sits right of Reload");
            var icon = collapse.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            var reloadIcon = reload.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single();
            Check(icon.Classes.Contains("explorerIcon") && icon.StrokeThickness == reloadIcon.StrokeThickness && icon.StrokeThickness <= 1.5 && icon.Fill is null
                  && (icon.Stroke as Avalonia.Media.ISolidColorBrush)?.Color == ThemeService.Lookup("IconForeground"), "Collapse All uses the thin-stroke icon style and the theme icon colour");
            Check(collapse.Cursor is not null && reload.Cursor is not null, "explorer toolbar buttons show the hand cursor");
            foreach (var (code, text) in new[] { ("en", "Collapse All"), ("zh-Hant", Loc.ZhHant["CollapseAll"]), ("ja", Loc.Ja["CollapseAll"]), ("ko", Loc.Ko["CollapseAll"]) })
            {
                Loc.Set(code); Pump();
                Check(ToolTip.GetTip(collapse) as string == text && text.Length > 0 && (code == "en" || text != "Collapse All"), $"{code}: Collapse All tooltip is '{text}'");
            }
            Loc.Set("en"); Pump();
            var explorerTree = Find<FolderExplorer>("FolderExplorer").FindControl<TreeView>("FolderTree")!;
            IEnumerable<TreeViewItem> Nodes(IEnumerable<TreeViewItem> items) => items.SelectMany(i => new[] { i }.Concat(Nodes(i.Items.OfType<TreeViewItem>())));
            for (var level = 0; level < 3; level++)
            {
                foreach (var folder in Nodes(explorerTree.Items.OfType<TreeViewItem>()).Where(i => i.Tag is FolderEntry { IsDirectory: true })) folder.IsExpanded = true;
                Wait(300);
            }
            var expanded = Nodes(explorerTree.Items.OfType<TreeViewItem>()).Where(i => i.IsExpanded).ToList();
            Check(expanded.Count >= 3, $"nested folders are expanded ({expanded.Count})");
            collapse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(Nodes(explorerTree.Items.OfType<TreeViewItem>()).All(i => !i.IsExpanded) && expanded.All(i => !i.IsExpanded), "Collapse All collapses every expanded folder");
        }
        finally { Loc.Set("en"); window.Close(); try { Directory.Delete(root, true); } catch (Exception) { } }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
