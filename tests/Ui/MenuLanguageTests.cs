using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 100: switch the language while each context menu is closed, then open it: every header must be in the new
// language. Several switches in a row (not just one), for every context menu in the app.
internal static class MenuLanguageTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly string[] Sequence = ["ja", "de", "zh-Hant", "fr", "ko", "zh-Hans", "es", "en"];

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }

        IEnumerable<MenuItem> All(ItemsControl c) => c.Items.OfType<MenuItem>().SelectMany(i => new[] { i }.Concat(All(i)));
        // The string key of each header, found from its English text while the app is in English (several keys can
        // share one English text: any of them may be the source). Items with no key (spelling words, file names) are skipped.
        Dictionary<MenuItem, string[]> Keys(ContextMenu menu) => All(menu)
            .Select(i => (i, k: Loc.En.Where(p => p.Value == i.Header as string).Select(p => p.Key).ToArray()))
            .Where(x => x.k.Length > 0).ToDictionary(x => x.i, x => x.k);

        // Open the way the app does (right click / menu key raise ContextRequested on the target), so Opening runs.
        void Show(ContextMenu menu, Action open)
        {
            open(); Pump();
            if (!menu.IsOpen) throw new Exception("the menu did not open");
        }
        void Request(Control target) => target.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs());
        void Verify(string name, ContextMenu menu, Action target)
        {
            Loc.Set(Loc.English); Pump();
            Show(menu, target);
            var keys = Keys(menu);
            menu.Close(); Pump();
            Check(keys.Count > 0, $"{name}: {keys.Count} localized headers found");
            foreach (var code in Sequence)
            {
                Loc.Set(code); Pump();             // switched while the menu is closed
                Check(!menu.IsOpen, $"{name}: closed while switching to {code}");
                Show(menu, target);
                var stale = keys.Where(p => !p.Value.Any(k => (p.Key.Header as string) == Loc.T(k)))
                    .Select(p => $"{p.Key.Name ?? p.Value[0]}='{p.Key.Header}'").ToList();
                menu.Close(); Pump();
                Check(stale.Count == 0, $"{name}: every header in {code} after opening{(stale.Count > 0 ? " STALE: " + string.Join(", ", stale) : "")}");
            }
            Loc.Set(Loc.English); Pump();
        }

        // Image viewer (XAML menu).
        {
            var viewer = new ImageViewer();
            var window = new Window { Width = 440, Height = 440, Content = viewer };
            window.Show(); Pump();
            viewer.ShowImage(new RenderTargetBitmap(new PixelSize(200, 100))); Pump();
            var c = viewer.TranslatePoint(new Point(200, 200), window)!.Value;
            Verify("image viewer menu", viewer.ContextMenu!, () => { window.MouseDown(c, Avalonia.Input.MouseButton.Right); window.MouseUp(c, Avalonia.Input.MouseButton.Right); });
            window.Close(); Pump();
        }

        var root = Path.Combine(Path.GetTempPath(), $"mdhub-menulang-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        var a = Path.Combine(root, "a.md"); File.WriteAllText(a, "# a\n\nhello");
        File.WriteAllText(Path.Combine(root, "b.md"), "# b");
        var w = new MainWindow { Width = 1100, Height = 600 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var explorer = w.GetVisualDescendants().OfType<FolderExplorer>().Single();
            var tree = explorer.FindControl<TreeView>("FolderTree")!;
            Wait(v.OpenFolderPathAsync(root));
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [a])!);
            var doc = (NoteDocument)typeof(MainView).GetField("_active", Flags)!.GetValue(v)!;
            var tabs = w.GetVisualDescendants().OfType<FileTabs>().First();
            TreeViewItem Row(string p) => tree.GetVisualDescendants().OfType<TreeViewItem>().First(t => t.Tag is FolderEntry e && string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase));

            var tabItem = tabs.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.DataContext is NoteDocument);
            Verify("tab menu", tabs.TabMenu!, () => Request(tabItem));
            Verify("explorer file menu", Row(a).ContextMenu!, () => Request(Row(a)));
            Verify("explorer folder menu", Row(Path.Combine(root, "sub")).ContextMenu!, () => Request(Row(Path.Combine(root, "sub"))));
            Verify("explorer background menu", tree.ContextMenu!, () => Request(tree));
            Point At(TreeViewItem i) { w.UpdateLayout(); var h = i.GetVisualDescendants().OfType<Control>().First(x => x.Name == "PART_HeaderPresenter"); return h.TranslatePoint(new Point(10, h.Bounds.Height / 2), w)!.Value; }
            void Click(string p, Avalonia.Input.RawInputModifiers m = Avalonia.Input.RawInputModifiers.None) { var pt = At(Row(p)); w.MouseDown(pt, Avalonia.Input.MouseButton.Left, m); w.MouseUp(pt, Avalonia.Input.MouseButton.Left, m); Pump(); }
            var ctrl = OperatingSystem.IsMacOS() ? Avalonia.Input.RawInputModifiers.Meta : Avalonia.Input.RawInputModifiers.Control;
            var b = Path.Combine(root, "b.md");
            Verify("explorer multi-select menu", explorer.MultiMenu, () => { if (!explorer.IsMultiSelect) { Click(a); Click(b, ctrl); } Request(Row(a)); });
            Check(explorer.IsMultiSelect, "multi-select kept for the multi-select menu");
            w.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); Pump();
            doc = (NoteDocument)typeof(MainView).GetField("_active", Flags)!.GetValue(v)!;
            Verify("editor menu", doc.Editor.ContextMenu!, () => Request(doc.Editor));
            var preview = (Control)typeof(MainView).GetProperty("MdEditor", Flags)!.GetValue(v)!;
            Verify("preview menu", preview.ContextMenu!, () => Request(preview));
        }
        finally
        {
            Loc.Set(Loc.English);
            w.Close();
            try { Directory.Delete(root, true); } catch (IOException) { }
        }
        Console.WriteLine($"Menu language tests passed: {passed}");
    }
}