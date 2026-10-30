using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 25: File > Open Recent.
internal static class RecentTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var timeout = new CancellationTokenSource(100); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 5; i++) Pump(); }
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-recent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string Make(string name, string text = "# hi") { var p = Path.Combine(root, name); File.WriteAllText(p, text); return p; }
        string Dir(string name) { var p = Path.Combine(root, name); Directory.CreateDirectory(p); return p; }
        var a = Make("a.md"); var b = Make("b.txt", "plain"); var f1 = Dir("f1"); var f2 = Dir("f2");
        try
        {
            RecentService.Clear();
            var w1 = new MainWindow { Width = 1200, Height = 800 }; var w2 = new MainWindow { Width = 1100, Height = 700 };
            w1.Show(); w2.Show(); Pump();
            MainView V(Window w) => w.GetVisualDescendants().OfType<MainView>().Single();
            MenuItem Recent(Window w) => w.GetVisualDescendants().OfType<TopMenu>().Single().FindControl<MenuItem>("OpenRecentMenu")!;
            List<MenuItem> Entries(Window w) => Recent(w).Items.OfType<MenuItem>().Where(m => m.Tag is string).ToList();
            IList<NoteDocument> Docs(Window w) => (IList<NoteDocument>)typeof(MainView).GetField("_documents", Flags)!.GetValue(V(w))!;
            Task OpenFile(Window w, string p) => (Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(V(w), [p])!;
            void Wait(Task t) { while (!t.IsCompleted) Pump(); t.GetAwaiter().GetResult(); }

            var fileMenu = w1.GetVisualDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu").Items.OfType<MenuItem>().First();
            var headers = fileMenu.Items.OfType<MenuItem>().Select(m => m.Header as string).ToList();
            Check(headers.IndexOf("Open _Recent") == headers.IndexOf("Open _Folder…") + 1, "Open Recent sits after Open Folder in the File menu");
            Check(!Recent(w1).IsEnabled && !Recent(w2).IsEnabled && Recent(w1).Items.Count == 0, "Open Recent is disabled while the list is empty");

            Wait(OpenFile(w1, a)); Wait(OpenFile(w1, b)); Pump();
            Check(RecentService.Files.SequenceEqual([b, a]), "Opening files records them newest first");
            Wait(V(w1).OpenFolderPathAsync(f1)); Pump();
            Check(RecentService.Folders.SequenceEqual([f1]), "Opening a folder records it");
            Check(Recent(w1).IsEnabled && Recent(w2).IsEnabled, "Open Recent is enabled in every window once the list has entries");
            var items = Recent(w2).Items.Cast<object>().ToList();
            Check(items.Count == 6 && items[0] is MenuItem { Tag: string t0 } && t0 == b && items[1] is MenuItem { Tag: string t1 } && t1 == a && items[2] is Separator
                && items[3] is MenuItem { Tag: string t3 } && t3 == f1 && items[4] is Separator && items[5] is MenuItem { Header: "Clear Recently Opened" },
                "The other window shows files, separator, folders, separator, Clear Recently Opened");
            var first = Entries(w2)[0];
            var texts = ((StackPanel)first.Header!).Children.OfType<TextBlock>().Select(x => x.Text).ToList();
            Check(texts.SequenceEqual(["b.txt", RecentList.ShortParent(b)]) && Equals(ToolTip.GetTip(first), b), "An entry shows the name and short folder with the full path as its tooltip");
            Check(((StackPanel)Entries(w2)[2].Header!).Children.OfType<TextBlock>().First().Text == "f1", "A folder entry shows the folder name");

            Wait(OpenFile(w1, a)); Pump();
            Check(RecentService.Files.SequenceEqual([a, b]), "Switching to an already open file moves it to the front");

            // Click an entry in the second window: it opens there.
            Entries(w2).Single(m => (string)m.Tag! == b).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            Check(Docs(w2).Any(d => d.Path == b) && RecentService.Files[0] == b, "Clicking a recent file opens it and moves it to the front");
            Entries(w2).Single(m => (string)m.Tag! == f1).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            Check(V(w2).FindControl<FolderExplorer>("FolderExplorer")?.OpenedFolder is { } of && RecentList.Same(of, f1), "Clicking a recent folder opens it");

            // Missing entries: status note, then keep or remove.
            File.Delete(b);
            var asked = new List<string>();
            var confirm = typeof(MainView).GetProperty("ConfirmRemoveRecentOverride", Flags)!;
            confirm.SetValue(V(w2), (Func<string, Task<bool>>)(p => { asked.Add(p); return Task.FromResult(false); }));
            Entries(w2).Single(m => (string)m.Tag! == b).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            var note = (string?)typeof(MainView).GetProperty("LastRecentNote", Flags)!.GetValue(V(w2));
            Check(asked.SequenceEqual([b]) && note is not null && note.Contains("no longer exists") && note.Contains(b), "A missing file shows a status note and asks to remove it");
            Check(RecentService.Files.Contains(b), "Keeping a missing entry leaves it in the list");
            confirm.SetValue(V(w2), (Func<string, Task<bool>>)(p => { asked.Add(p); return Task.FromResult(true); }));
            Entries(w2).Single(m => (string)m.Tag! == b).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            Check(!RecentService.Files.Contains(b) && !Entries(w1).Any(m => (string)m.Tag! == b), "Removing a missing entry updates every window");
            Wait(V(w1).OpenFolderPathAsync(f2)); Directory.Delete(f2);
            Entries(w2).Single(m => (string)m.Tag! == f2).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            Check(!RecentService.Folders.Contains(f2) && asked[^1] == f2, "A missing folder can be removed too");

            // Cap.
            for (var i = 0; i < 12; i++) Wait(OpenFile(w1, Make($"n{i}.md")));
            Pump();
            Check(Entries(w1).Count(m => File.Exists((string)m.Tag!)) == 10 && RecentService.Files.Count == 10 && Path.GetFileName(RecentService.Files[0]) == "n11.md", "The menu lists the 10 newest files");

            // Session restore does not reorder.
            var before = RecentService.Files.ToList();
            Wait(V(w2).RestoreSessionAsync(new WindowSession { Tabs = [a] }, null));
            Check(RecentService.Files.SequenceEqual(before), "Restoring a session does not reorder Open Recent");

            // Persistence.
            var sessionFile = Path.Combine(root, "session.json");
            SessionManager.PathOverride = sessionFile; SessionManager.Enabled = true;
            try
            {
                Wait(OpenFile(w1, a));
                var saved = SessionStore.Load(sessionFile);
                Check(saved is not null && saved.RecentFiles.SequenceEqual(RecentService.Files) && saved.RecentFolders.SequenceEqual(RecentService.Folders), "Every open saves the list to the session file");
                RecentService.Clear(); RecentService.Load(saved);
                Check(RecentService.Files.SequenceEqual(saved!.RecentFiles) && Entries(w1).Count == saved.RecentFiles.Count + saved.RecentFolders.Count, "Loading the session file restores the list and menus");
                Recent(w1).Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Clear Recently Opened")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
                Check(RecentService.IsEmpty && !Recent(w1).IsEnabled && !Recent(w2).IsEnabled && Recent(w2).Items.Count == 0, "Clear Recently Opened empties and disables the submenu in every window");
                Check(SessionStore.Load(sessionFile) is { RecentFiles.Count: 0, RecentFolders.Count: 0 }, "Clearing is saved");
            }
            finally { SessionManager.Enabled = false; SessionManager.PathOverride = null; SessionManager.Reset(); }
            w1.Close(); w2.Close(); Pump();
        }
        finally { RecentService.Clear(); try { Directory.Delete(root, true); } catch (Exception) { } }
        Console.WriteLine($"Recent tests passed: {_count}");
    }

    static int _count;
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception("FAIL: " + message);
        _count++;
        Console.WriteLine("PASS: " + message);
    }
}