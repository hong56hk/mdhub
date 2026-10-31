using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;
using Mdhub.Views;

// Bug: clicking a global search result (file header or match line) ran the search again.
internal static class SearchClickTests
{
    public static void Run()
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow { Width = 1200, Height = 800 }; window.Show();
        void Pump() { using var timeout = new CancellationTokenSource(30); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(int ms) { using var timeout = new CancellationTokenSource(ms); Dispatcher.UIThread.MainLoop(timeout.Token); }
        Pump();
        var view = window.FindControl<MainView>("MainView")!;
        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        var search = Find<GlobalSearch>("GlobalSearch");
        int Runs() => (int)typeof(GlobalSearch).GetProperty("SearchRuns", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(search)!;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-searchclick-" + Guid.NewGuid().ToString("N"))).FullName;
        void Await(Task task) { var deadline = DateTime.UtcNow.AddSeconds(15); while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump(); Check(task.IsCompleted, "operation completes"); task.GetAwaiter().GetResult(); }
        try
        {
            File.WriteAllText(Path.Combine(root, "c.md"), "delta\nfoo delta\n");
            File.WriteAllText(Path.Combine(root, "d.txt"), "delta\r\nmore delta\r\n");
            for (var i = 0; i < 30; i++) File.WriteAllText(Path.Combine(root, $"z{i:00}.md"), "delta " + i);
            Await((Task)typeof(MainView).GetMethod("LoadRootAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [root])!);
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift }); Pump();
            Find<TextBox>("GlobalQuery").Text = "delta"; Pump();
            Await(search.SearchAsync()); Wait(400);
            var tree = Find<TreeView>("GlobalResults");
            var result = search.Result; var runs = Runs();
            Check(result.Files.Count == 32, $"search finds every file ({result.Files.Count})");
            var scroll = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
            void Click(TreeViewItem item, string what)
            {
                item.BringIntoView(); window.UpdateLayout(); Pump();
                var header = item.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_Header");
                var p = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), window)!.Value;
                var offset = scroll.Offset;
                window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left);
                Wait(700); // longer than the 250 ms typing debounce
                Check(Runs() == runs && ReferenceEquals(search.Result, result) && tree.Items.Count == result.Files.Count,
                    $"clicking {what} does not re-run the search (runs {runs} -> {Runs()}, files {tree.Items.Count})");
                // Selecting may nudge an edge item fully into view by a pixel; a re-run would clear the list and jump to the top.
                Check(Math.Abs(scroll.Offset.Y - offset.Y) <= 2, $"clicking {what} keeps the results scroll position ({offset} -> {scroll.Offset})");
            }
            TreeViewItem Group(string name) => tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is SearchFile f && Path.GetFileName(f.Path) == name);
            Click(Group("c.md"), "a file header");
            var c = Group("c.md"); c.IsExpanded = true; Pump();
            Click((TreeViewItem)c.Items[1]!, "a match line in a closed LF file");
            var d = Group("d.txt"); d.IsExpanded = true; Pump();
            Click((TreeViewItem)d.Items[0]!, "a match line in a CRLF file");
            Click((TreeViewItem)c.Items[0]!, "a match line in an already open file");
            var z = Group("z25.md"); z.IsExpanded = true; Pump();
            Click((TreeViewItem)z.Items[0]!, "a match line further down the list");
            var tabs = Find<ListBox>("Tabs");
            Check(tabs.Items.Count >= 3, "clicked results opened their files");
        }
        finally { window.Close(); try { Directory.Delete(root, true); } catch (Exception) { } }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
