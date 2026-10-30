using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;
using IOPath = System.IO.Path;

// Item 64: each folder's tabs are saved when its window closes or the app exits, and restored (with the active
// tab) when the folder is opened again from Open Recent; missing files are skipped silently.
internal static class FolderTabsTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Settle(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var root = IOPath.Combine(IOPath.GetTempPath(), $"mdhub-foldertabs-{Guid.NewGuid():N}");
        var sessionPath = IOPath.Combine(root, "appdata", "session.json");
        var store = FolderTabsStore.PathFor(sessionPath);
        SessionManager.PathOverride = sessionPath; SessionManager.Enabled = true; SessionManager.Reset();
        var folder = Directory.CreateDirectory(IOPath.Combine(root, "Notes")).FullName;
        string F(string name) { var p = IOPath.Combine(folder, name); File.WriteAllText(p, "# " + name); return p; }
        var a = F("a.md"); var b = F("b.md"); var c = F("c.md");
        var windows = new List<MainWindow>();
        (MainWindow, MainView, ListBox) NewWindow()
        {
            var w = new MainWindow { Width = 1000, Height = 700 }; windows.Add(w);
            SessionManager.Track(w); w.Show(); Settle();
            return (w, w.GetVisualDescendants().OfType<MainView>().Single(), w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!);
        }
        List<string?> Paths(ListBox tabs) => tabs.Items.OfType<NoteDocument>().Select(d => d.Path).ToList();
        void Recent(MainView view, string path) => Wait((Task)typeof(MainView).GetMethod("OpenRecentAsync", flags)!.Invoke(view, [path, true])!);
        try
        {
            var (w1, v1, tabs1) = NewWindow();
            Recent(v1, folder);
            Check(Paths(tabs1).All(p => p is null), "A folder with no history opens no tabs");
            foreach (var p in new[] { a, b, c }) Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", flags)!.Invoke(v1, [p])!);
            tabs1.SelectedItem = tabs1.Items.OfType<NoteDocument>().First(d => d.Path == b); Settle();
            w1.Close(); Settle();
            Check(File.Exists(store), "Closing the window saves the folder's tabs in app data");
            Check(Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Length == 3, "Nothing is written inside the folder");

            var (w2, v2, tabs2) = NewWindow();
            Recent(v2, folder + IOPath.DirectorySeparatorChar);
            Check(Paths(tabs2).SequenceEqual([a, b, c]), $"Open Recent restores the tabs in order ({string.Join(", ", Paths(tabs2))})");
            Check((tabs2.SelectedItem as NoteDocument)?.Path == b, "The previously active tab is selected");
            tabs2.SelectedItem = tabs2.Items.OfType<NoteDocument>().First(d => d.Path == c); Settle();
            SessionManager.BeginShutdown(); w2.Close(); Settle();
            Check(FolderTabsStore.Recall(FolderTabsStore.Load(store), folder, File.Exists) is { ActiveTab: 2 }, "App exit saves the folder's tabs");

            File.Delete(c);
            var (w3, v3, tabs3) = NewWindow();
            Recent(v3, folder);
            Check(Paths(tabs3).SequenceEqual([a, b]) && tabs3.SelectedItem is NoteDocument { Path: { } }, "A deleted file is skipped silently and another tab is selected");
            w3.Close(); Settle();
        }
        finally
        {
            foreach (var w in windows) try { w.Close(); } catch { }
            SessionManager.Enabled = false; SessionManager.PathOverride = null; SessionManager.Reset();
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"Folder tabs tests: {passed} passed");
    }
}
