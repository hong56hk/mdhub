using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
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
// and the right-aligned shortcut hints stay. Each menu is right-clicked open twice (at 150% then 100% zoom) and
// its first open must look like the second: popup window fits the menu, border, corners, padding, unclipped hints.
internal static class MenuPaddingTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = false }).SetupWithoutStarting();
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
            // A real right click (pointer-placed ContextRequested) into a native PopupRoot, as on Windows. Before the fix
            // the first open's popup window kept its first-pass size (Avalonia #22573): narrower than the menu, the menu
            // centered in it at a negative offset, so its border, left padding and hint ends were cut off.
            void Verify(string label, Control owner, int zoom)
            {
                var menu = owner.ContextMenu!; // rounding: a 1px layout-rounding overhang at 150% is not clipping
                (Size client, Thickness border, CornerRadius corner)? first = null;
                for (var open = 1; open <= 2; open++)
                {
                    var tag = $"{label} @{zoom}%: open {open}";
                    var at = owner.TranslatePoint(owner == tree ? new Point(20, owner.Bounds.Height - 12) : owner == editor ? new Point(60, 60) : new Point(20, 8), w)!.Value;
                    w.MouseMove(at); w.MouseDown(at, MouseButton.Right); w.MouseUp(at, MouseButton.Right);
                    for (var k = 0; k < 6; k++) Pump();
                    var popup = TopLevel.GetTopLevel(menu) as PopupRoot;
                    Check(menu.IsOpen && popup is not null, $"{tag} opens in its own popup window");
                    var chrome = menu.GetVisualDescendants().OfType<Border>().FirstOrDefault();
                    Check(chrome is not null && menu.GetVisualDescendants().OfType<ItemsPresenter>().Any(), $"{tag} has its presenter template applied");
                    Check(menu.Padding == LocResources.MenuPadding && chrome!.BorderThickness == new Thickness(1) && chrome.CornerRadius.TopLeft > 0,
                        $"{tag} has padding {menu.Padding}, border {chrome!.BorderThickness}, corner {chrome.CornerRadius}");
                    var tl = menu.TranslatePoint(default, popup!)!.Value;
                    var br = menu.TranslatePoint(new Point(menu.Bounds.Width, menu.Bounds.Height), popup!)!.Value;
                    Check(Math.Abs(tl.X) < 0.6 && Math.Abs(tl.Y) < 0.6 && br.X <= popup!.ClientSize.Width + 1.6 && br.Y <= popup.ClientSize.Height + 1.6,
                        $"{tag} fits its popup window (menu {tl}-{br}, window {popup.ClientSize})");
                    var screenAt = w.PointToScreen(at); var screenMenu = popup.PointToScreen(default);
                    Check(Math.Abs(screenAt.X - screenMenu.X) <= 2 && Math.Abs(screenAt.Y - screenMenu.Y) <= 2, $"{tag} opens at the pointer ({screenMenu} vs {screenAt})");
                    Check(menu.Placement == PlacementMode.AnchorAndGravity, $"{tag} avoids pointer placement (Avalonia #22573)");
                    var items = menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).ToList();
                    var offsets = items.Select(i => i.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_HeaderPresenter").TranslatePoint(new Point(0, 0), i)!.Value.X).ToList();
                    Check(items.Count > 0 && items.All(i => i.Padding == expected) && offsets.All(x => Math.Abs(x - 10) < 0.6),
                        $"{tag} has item padding 10,2,8,2 and text 10px from the edge ({string.Join(",", offsets.Distinct())})");
                    foreach (var item in items.Where(i => ShortcutKeys.GetId(i) is not null))
                    {
                        var hint = item.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => b.Name == "PART_InputGestureText");
                        var header = item.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_HeaderPresenter");
                        var hintX = hint?.TranslatePoint(new Point(hint.Bounds.Width, 0), item)?.X ?? 0;
                        var hintInWindow = hint?.TranslatePoint(new Point(hint.Bounds.Width, 0), popup)?.X ?? double.MaxValue;
                        Check(hint is { IsVisible: true } && hint.Text == ShortcutKeys.MenuText(item) && hintX > header.TranslatePoint(new Point(header.Bounds.Width, 0), item)!.Value.X
                              && item.Bounds.Width - hintX <= 8.6 && hintInWindow <= popup.ClientSize.Width - 1 && hint.DesiredSize.Width - hint.Margin.Left - hint.Margin.Right <= hint.Bounds.Width + 0.6,
                            $"{tag} keeps the right-aligned, unclipped hint for {item.Name} ({hint?.Text}; right {hintInWindow} of {popup.ClientSize.Width}, desired {hint?.DesiredSize} bounds {hint?.Bounds})");
                    }
                    var now = (popup.ClientSize, chrome.BorderThickness, chrome.CornerRadius);
                    if (first is null) first = now;
                    else Check(first.Value == now, $"{label} @{zoom}%: first open matches the second ({first.Value.client} vs {now.ClientSize})");
                    menu.Close(); Pump();
                }
            }
            Check(LocResources.AtPointer(new Point(2, -10), new Size(172, 284)) == new Rect(2, 0, 1, 1) && LocResources.AtPointer(new Point(5, 6), new Size(10, 10)) == new Rect(5, 6, 1, 1),
                "the pointer anchor rectangle is kept inside its target");
            foreach (var zoom in new[] { 150, 100 })
            {
                w.SetZoom(zoom, false); Pump();
                Verify("file menu", fileRow, zoom);
                Verify("folder menu", folderRow, zoom);
                Verify("explorer background menu", tree, zoom);
                Verify("editor menu", editor, zoom);
            }
        }
        finally { foreach (var o in w.OwnedWindows.ToList()) o.Close(); try { w.Close(); Pump(); } catch { } try { Directory.Delete(root, true); } catch { } }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}