using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;
using Mdhub.Views;

// Item 43: global-search match rows start at the same x as their file name (just right of the file row's chevron).
internal static class SearchIndentTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Await(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        var window = new MainWindow { Width = 1100, Height = 700 }; window.Show(); Pump();
        var view = window.GetVisualDescendants().OfType<MainView>().Single();
        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        var search = Find<GlobalSearch>("GlobalSearch");
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-indent-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "a.md"), "alpha\nbeta alpha\n");
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllText(Path.Combine(root, "sub", "b.md"), "x\nalpha\n");
            Await((Task)typeof(MainView).GetMethod("LoadRootAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [root])!);
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift }); Pump();
            Find<TextBox>("GlobalQuery").Text = "alpha"; Pump(); Await(search.SearchAsync()); Pump();
            var results = Find<TreeView>("GlobalResults");
            // x of the first glyph of a row's header text, relative to the results list.
            double TextX(TreeViewItem item)
            {
                var header = item.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_HeaderPresenter");
                var text = header.GetVisualDescendants().OfType<TextBlock>().First();
                return text.TranslatePoint(new Point(text.Padding.Left, 0), results)!.Value.X;
            }
            double ChevronRight(TreeViewItem item)
            {
                var chevron = item.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_ExpandCollapseChevron");
                return chevron.TranslatePoint(new Point(chevron.Bounds.Width, 0), results)!.Value.X;
            }
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme, persist: false); Pump();
                var groups = results.Items.OfType<TreeViewItem>().ToList();
                Check(groups.Count == 2 && groups.All(g => g.Items.Count > 0), $"{theme}: two file groups with matches");
                // Selecting a match opens the file (and the list refreshes), so check the shared selection look on a file row;
                // match rows use the same unmodified item template and theme brushes.
                var firstGroup = groups[0];
                var plain = firstGroup.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot").Background;
                results.SelectedItem = firstGroup; Pump();
                var selected = firstGroup.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot").Background;
                Check(firstGroup.IsSelected && selected is not null && !Equals(selected, plain), $"{theme}: the theme selection colour still applies to result rows");
                results.SelectedItem = null; Pump();
                foreach (var g in groups)
                {
                    var fileX = TextX(g);
                    Check(fileX >= ChevronRight(g) - 0.5, $"{theme}: the file name starts right of its chevron ({fileX:0.##} >= {ChevronRight(g):0.##})");
                    foreach (var hit in g.Items.OfType<TreeViewItem>())
                    {
                        var x = TextX(hit);
                        Check(Math.Abs(x - fileX) < 0.5, $"{theme}: match line starts at the file name's x ({x:0.##} == {fileX:0.##})");
                        Border Root(TreeViewItem t) => t.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");
                        var rootX = Root(hit).TranslatePoint(new Point(0, 0), results)!.Value.X;
                        Check(Math.Abs(rootX - Root(g).TranslatePoint(new Point(0, 0), results)!.Value.X) < 0.5 && Math.Abs(Root(hit).Bounds.Width - Root(g).Bounds.Width) < 0.5,
                            $"{theme}: the match row's hover/selection background still spans the full row");
                    }
                }
            }
        }
        finally
        {
            ThemeService.Apply(AppTheme.Dark, persist: false);
            try { window.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"Search indent tests: {passed} passed");
    }
}