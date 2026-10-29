using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Text;
using Mdhub.Views;

internal static class WorkspaceSearchTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow(); window.Show();
        void Pump() { using var timeout = new CancellationTokenSource(30); Dispatcher.UIThread.MainLoop(timeout.Token); }
        Pump();
        var view = window.FindControl<MainView>("MainView")!;
        T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
        var search = Find<GlobalSearch>("GlobalSearch");
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-search-" + Guid.NewGuid().ToString("N"))).FullName;
        void Await(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump();
            Check(task.IsCompleted, "operation completes"); task.GetAwaiter().GetResult();
        }
        void Invoke(string name, params object[] values) => Await((Task)typeof(MainView).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, values)!);
        void Shortcut(Key key) { view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift }); Pump(); }
        void Query(string value) { Find<TextBox>("GlobalQuery").Text = value; Pump(); Await(search.SearchAsync()); }
        try
        {
            Shortcut(Key.F);
            Check(search.IsVisible && Find<TextBox>("GlobalQuery").IsFocused && search.Result.Count == 0, "search shortcut opens and focuses empty-folder search");
            var a = Path.Combine(root, "a.md");
            var sub = Directory.CreateDirectory(Path.Combine(root, "sub")).FullName;
            var b = Path.Combine(sub, "b.txt");
            File.WriteAllText(a, "alpha\nAlpha alphabet\n");
            File.WriteAllText(b, "alpha\r\nother alpha", System.Text.Encoding.Unicode);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, ".git", "ignored"), "alpha");
            File.WriteAllBytes(Path.Combine(root, "binary.bin"), [0, 1, 2]);
            Invoke("LoadRootAsync", root);
            Query("alpha");
            Check(search.Result.Count == 5 && search.Result.Files.Count == 2, $"global search groups recursive text results and excludes metadata and binary files (count={search.Result.Count}, error={search.Result.Error})");
            Find<ToggleButton>("GlobalWholeWord").IsChecked = true;
            Await(search.SearchAsync());
            Check(search.Result.Count == 4, "whole-word option");
            Find<ToggleButton>("GlobalMatchCase").IsChecked = true;
            Await(search.SearchAsync());
            Check(search.Result.Count == 3, "match-case option");
            Find<ToggleButton>("GlobalRegex").IsChecked = true;
            Query("(");
            Check(search.Result.Error is not null && !Find<Button>("GlobalReplaceAll").IsEnabled, "invalid regex displays error and disables replace");
            Find<ToggleButton>("GlobalRegex").IsChecked = false;
            Find<ToggleButton>("GlobalWholeWord").IsChecked = false;
            Find<ToggleButton>("GlobalMatchCase").IsChecked = false;
            Invoke("OpenPathAsync", a);
            var tabs = Find<ListBox>("Tabs");
            var document = (NoteDocument)tabs.SelectedItem!;
            document.Editor.Text = "unsaved alpha";
            Query("alpha");
            Check(search.Result.Count == 3 && search.Result.Files.Single(f => f.Path == a).Source == "unsaved alpha", "search includes unsaved open buffers");
            var resultTree = Find<TreeView>("GlobalResults");
            var aGroup = resultTree.Items.OfType<TreeViewItem>().Single(i => i.Tag is SearchFile f && f.Path == a);
            resultTree.SelectedItem = aGroup.Items[0];
            Pump();
            Check(document.Editor.SelectedText == "alpha", $"clicking result selects matching source text (selected='{document.Editor.SelectedText}')");
            Shortcut(Key.E);
            Check(Find<FolderExplorer>("FolderExplorer").IsVisible && !search.IsVisible, "Ctrl+Shift+E returns to explorer");
            Shortcut(Key.H);
            Check(search.IsVisible && Find<StackPanel>("GlobalReplaceRow").IsVisible, "Ctrl+Shift+H opens search with replace expanded");
            Query("alpha");
            var request = new WorkspaceReplaceRequest(search.Result.Files, "alpha", "beta", new());
            window.UpdateLayout();
            using (var screenshot = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height)))
            {
                screenshot.Render(window);
                Directory.CreateDirectory("artifacts");
                screenshot.Save("artifacts/global-search.png");
            }
            var replace = (Task<string>)typeof(MainView).GetMethod("ReplaceSearchResultsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [request])!;
            Pump();
            var dialog = window.OwnedWindows.Single();
            dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Replace")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Await(replace);
            Check(document.Text == "unsaved beta" && document.IsDirty && File.ReadAllText(a).StartsWith("alpha"), "replace preserves open buffers as unsaved edits");
            Check(File.ReadAllText(b) == "beta\r\nother beta" && File.ReadAllBytes(b).Take(2).SequenceEqual(new byte[] { 255, 254 }), "closed-file replacement preserves Unicode encoding and CRLF");
            document.Editor.Undo();
            Check(document.Text == "unsaved alpha", "open-buffer replacement can be undone");
            Query("beta");
            var stale = new WorkspaceReplaceRequest(search.Result.Files, "beta", "wrong", new());
            File.WriteAllText(b, "external edit");
            var staleTask = (Task<string>)typeof(MainView).GetMethod("ReplaceSearchResultsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [stale])!;
            Pump();
            window.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Replace")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Await(staleTask);
            Check(File.ReadAllText(b) == "external edit" && staleTask.Result.Contains("skipped"), "stale search results never overwrite changed files");
            // UI alignment: global search uses the find bar's shared styles (Styles/FindStyles.axaml).
            var findBar = window.GetVisualDescendants().OfType<FindBar>().First();
            T FindIn<T>(Control host, string name) where T : Control => host.FindControl<T>(name) ?? throw new Exception("missing " + name);
            var findBarWasVisible = findBar.IsVisible;
            findBar.IsVisible = true; FindIn<ToggleButton>(findBar, "ExpandReplaceToggle").IsChecked = true;
            Find<ToggleButton>("GlobalExpandReplace").IsChecked = true; Pump(); window.UpdateLayout();
            string Look(Control c)
            {
                var presenter = c.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().FirstOrDefault(p => p.Name == "PART_ContentPresenter");
                return c switch
                {
                    TemplatedControl t => $"{t.Classes.Count(x => !x.StartsWith(':'))}|{string.Join(",", t.Classes.Where(x => !x.StartsWith(':')).Order())}|{t.Height}|{t.Padding}|{t.CornerRadius}|{t.FontSize}|{t.FontWeight}|{t.Background}|{t.Foreground}|{t.BorderBrush}|{t.BorderThickness}|{presenter?.Background}|{presenter?.CornerRadius}",
                    _ => ""
                } + (c is Avalonia.Layout.Layoutable l && c is not TextBox ? $"|{l.Width}" : "");
            }
            foreach (var (mine, theirs) in new[] { ("GlobalMatchCase", "MatchCaseToggle"), ("GlobalWholeWord", "WholeWordToggle"), ("GlobalRegex", "RegexToggle"), ("GlobalPreserveCase", "PreserveCaseToggle"), ("GlobalReplaceSelected", "ReplaceButton"), ("GlobalReplaceAll", "ReplaceAllButton") })
            {
                FindIn<Control>(findBar, theirs).IsEnabled = FindIn<Control>(search, mine).IsEnabled; Pump();
                Check(Look(FindIn<Control>(search, mine)) == Look(FindIn<Control>(findBar, theirs)), $"{mine} looks like find bar {theirs} ({Look(FindIn<Control>(search, mine))} vs {Look(FindIn<Control>(findBar, theirs))})");
            }
            var refreshButton = FindIn<Button>(search, "GlobalRefresh");
            Check(Look(refreshButton) == Look(FindIn<Control>(findBar, "FindCloseButton")) && refreshButton.Width == 24, "refresh uses the find bar icon button style");
            var expandMine = FindIn<ToggleButton>(search, "GlobalExpandReplace"); var expandTheirs = FindIn<ToggleButton>(findBar, "ExpandReplaceToggle");
            Check(expandMine.Classes.Contains("findAction") && expandMine.Width == expandTheirs.Width && expandMine.CornerRadius == expandTheirs.CornerRadius && expandMine.Foreground?.ToString() == expandTheirs.Foreground?.ToString(), "expand toggle matches the find bar chevron toggle");
            Check(FindIn<Avalonia.Controls.Shapes.Path>(search, "GlobalExpandIcon").Data?.ToString() != null && search.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().All(path => path.Classes.Contains("findIcon") && path.Width == 16 && path.StrokeThickness == 1.25), "global search icons use the find bar 16px icon style");
            var queryBox = Find<TextBox>("GlobalQuery"); var findInput = findBar.GetVisualDescendants().OfType<TextBox>().First(b => b.Classes.Contains("findField"));
            Check(queryBox.Classes.Contains("findField") && Find<TextBox>("GlobalReplacement").Classes.Contains("findField") && queryBox.Height == findInput.Height && queryBox.Background?.ToString() == findInput.Background?.ToString() && queryBox.BorderBrush?.ToString() == findInput.BorderBrush?.ToString() && queryBox.CornerRadius == findInput.CornerRadius, "search inputs use the find bar dark field style");
            Check(queryBox.InnerRightContent is StackPanel inner && inner.Children.Contains(Find<ToggleButton>("GlobalRegex")) && Find<TextBox>("GlobalReplacement").InnerRightContent == Find<ToggleButton>("GlobalPreserveCase"), "option toggles sit inside the inputs like the find bar");
            foreach (var (mine, theirs) in new[] { ("GlobalMatchCase", "MatchCaseToggle"), ("GlobalPreserveCase", "PreserveCaseToggle") })
            {
                FindIn<ToggleButton>(search, mine).IsChecked = true; FindIn<ToggleButton>(findBar, theirs).IsChecked = true; Pump();
                Check(Look(FindIn<Control>(search, mine)) == Look(FindIn<Control>(findBar, theirs)), $"checked {mine} matches checked {theirs}");
                FindIn<ToggleButton>(search, mine).IsChecked = false; FindIn<ToggleButton>(findBar, theirs).IsChecked = false; Pump();
            }
            Check(!Find<Avalonia.Controls.Shapes.Path>("GlobalExpandIcon").Data!.Bounds.Equals(Avalonia.Media.Geometry.Parse("M 6,4 L 10,8 6,12").Bounds), "expanded chevron points down");
            Find<ToggleButton>("GlobalExpandReplace").IsChecked = false; Pump();
            Check(!Find<StackPanel>("GlobalReplaceRow").IsVisible && Find<Avalonia.Controls.Shapes.Path>("GlobalExpandIcon").Data!.Bounds.Equals(Avalonia.Media.Geometry.Parse("M 6,4 L 10,8 6,12").Bounds), "collapsing hides replace and restores the chevron");

            FindIn<ToggleButton>(findBar, "ExpandReplaceToggle").IsChecked = false; findBar.IsVisible = findBarWasVisible; Pump();

            // Encoding resilience: invalid bytes never abort a search, and superseded searches are quiet.
            var noisy = new List<string>();
            EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> watch = (_, e) => { if (e.Exception is OperationCanceledException or System.Text.DecoderFallbackException) lock (noisy) noisy.Add(e.Exception.GetType().Name + " @ " + Environment.StackTrace.Replace("\r\n", " / ")); };
            AppDomain.CurrentDomain.FirstChanceException += watch;
            try
            {
                var bad = Path.Combine(root, "cp1252.md");
                var badBytes = System.Text.Encoding.ASCII.GetBytes("gamma here, it's a smart quote. gamma").ToArray();
                badBytes[31] = 0x92; // 'Unable to translate bytes [92] at index 31' (replaces the '.')
                File.WriteAllBytes(bad, badBytes);
                File.WriteAllText(Path.Combine(root, "plain.md"), "gamma plain");
                File.WriteAllBytes(Path.Combine(root, "bom.md"), [0xEF, 0xBB, 0xBF, .. System.Text.Encoding.UTF8.GetBytes("gamma bom")]);
                Query("gamma");
                var badFile = search.Result.Files.SingleOrDefault(f => f.Path == bad);
                Check(search.Result.Error is null && search.Result.Files.Count == 3 && badFile is { Lossy: true, Hits.Count: 2 } && search.Result.Lossy == 1, $"file with invalid bytes is searched with replacement characters (files={search.Result.Files.Count})");
                Check(Find<TextBlock>("GlobalSummary").Text!.Contains("undecodable"), "summary reports files with undecodable bytes");
                Check(search.Result.Files.Single(f => f.Path.EndsWith("bom.md")).Encoding.GetPreamble().Length == 3, "BOM file keeps its UTF-8 BOM encoding");
                using (var locked = new FileStream(Path.Combine(root, "locked.md"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    locked.Write(System.Text.Encoding.UTF8.GetBytes("gamma locked")); locked.Flush();
                    Query("gamma");
                    Check(search.Result.Files.Count == 3 && search.Result.Skipped >= 1, "a locked file is skipped without aborting the search");
                }
                var lossyReplace = (Task<string>)typeof(MainView).GetMethod("ReplaceSearchResultsAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(view, [new WorkspaceReplaceRequest([badFile!], "gamma", "delta", new())])!;
                Pump();
                if (window.OwnedWindows.SingleOrDefault() is { } confirm) confirm.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Replace")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Await(lossyReplace);
                Check(File.ReadAllBytes(bad).SequenceEqual(badBytes) && lossyReplace.Result.Contains("skipped"), "replace never rewrites a file with undecodable bytes");
                using (var cancelledToken = new CancellationTokenSource())
                {
                    cancelledToken.Cancel();
                    var cancelledSearch = WorkspaceSearch.SearchAsync(root, "gamma", new(), new Dictionary<string, string>(), cancelledToken.Token);
                    Await(cancelledSearch);
                    Check(cancelledSearch.Status == TaskStatus.RanToCompletion && cancelledSearch.Result.Cancelled, "a cancelled search completes quietly with a Cancelled result");
                }
                Find<TextBox>("GlobalQuery").Text = "gamma";
                var first = search.SearchAsync(); var second = search.SearchAsync(); var third = search.SearchAsync();
                Await(Task.WhenAll(first, second, third));
                Check(search.Result.Files.Count == 4 && Find<TextBlock>("GlobalSummary").Text!.Contains("4 files"), "superseded searches are cancelled and the last one wins");
                for (var typing = 0; typing < 5; typing++) { Find<TextBox>("GlobalQuery").Text = "gamm" + new string('a', typing % 2); search.Schedule(); }
                Find<TextBox>("GlobalQuery").Text = "gamma"; Pump(); Pump();
                Await(search.SearchAsync());
                Check(noisy.Count == 0, $"search raises no OperationCanceledException or DecoderFallbackException ({string.Join(",", noisy)})");
            }
            finally { AppDomain.CurrentDomain.FirstChanceException -= watch; }
            document.SavedText = document.Text;
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
