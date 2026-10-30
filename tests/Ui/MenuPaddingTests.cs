using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Bug 55: context menu items have their (10,2,8,2) padding on the very first open, not only from the second,
// and the right-aligned shortcut hints stay. Each menu is checked on its first open, then again on a reopen.
internal static class MenuPaddingTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        var expected = new Thickness(10, 2, 8, 2);
        Check(LocResources.MenuItemPadding == expected, "menu item padding is Thickness(10,2,8,2)");
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-menupad-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        var file = Path.Combine(root, "a.md"); File.WriteAllText(file, "x");
        var w = new MainWindow { Width = 1100, Height = 700 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Wait(v.OpenFolderPathAsync(root));
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [file])!);
            var tree = w.GetVisualDescendants().OfType<FolderExplorer>().Single().FindControl<TreeView>("FolderTree")!;
            var fileRow = tree.GetVisualDescendants().OfType<TreeViewItem>().First(t => t.Tag is FolderEntry { IsDirectory: false });
            var folderRow = tree.GetVisualDescendants().OfType<TreeViewItem>().First(t => t.Tag is FolderEntry { IsDirectory: true });
            var editor = ((NoteDocument)typeof(MainView).GetField("_active", Flags)!.GetValue(v)!).Editor;
            void Verify(string label, Control owner, ContextMenu menu)
            {
                for (var open = 1; open <= 2; open++)
                {
                    menu.Open(owner); Pump(); Pump();
                    var items = menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).ToList();
                    var offsets = items.Select(i => i.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_HeaderPresenter").TranslatePoint(new Point(0, 0), i)!.Value.X).ToList();
                    Check(items.Count > 0 && items.All(i => i.Padding == expected) && offsets.All(x => Math.Abs(x - 10) < 0.6),
                        $"{label}: open {open} has item padding 10,2,8,2 and text 10px from the edge ({string.Join(",", offsets.Distinct())})");
                    foreach (var item in items.Where(i => ShortcutKeys.GetId(i) is not null && i.IsVisible))
                    {
                        var hint = item.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => b.Name == "PART_InputGestureText");
                        var header = item.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_HeaderPresenter");
                        var hintX = hint?.TranslatePoint(new Point(hint.Bounds.Width, 0), item)?.X ?? 0;
                        Check(hint is { IsVisible: true } && hint.Text == ShortcutKeys.MenuText(item) && hintX > header.TranslatePoint(new Point(header.Bounds.Width, 0), item)!.Value.X && item.Bounds.Width - hintX <= 8.6,
                            $"{label}: open {open} keeps the right-aligned hint for {item.Name} ({hint?.Text})");
                    }
                    menu.Close(); Pump();
                }
            }
            Verify("file menu", fileRow, fileRow.ContextMenu!);
            Verify("folder menu", folderRow, folderRow.ContextMenu!);
            Verify("explorer background menu", tree, tree.ContextMenu!);
            Verify("editor menu", editor, editor.ContextMenu!);
        }
        finally { foreach (var o in w.OwnedWindows.ToList()) o.Close(); try { w.Close(); Pump(); } catch { } try { Directory.Delete(root, true); } catch { } }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}