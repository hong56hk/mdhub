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
