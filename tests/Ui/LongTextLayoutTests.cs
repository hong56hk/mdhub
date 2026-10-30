using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 59: long strings (German and French above all) must not clip anywhere: main window buttons, labels and status
// bar, every top-level menu and submenu with its shortcut hints, the explorer and editor context menus, and the dialogs.
internal static class LongTextLayoutTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static int _count;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false, OverlayPopups = false }).SetupWithoutStarting();
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-longtext-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        var file = Path.Combine(root, "a.md"); File.WriteAllText(file, "# Title\n\nSome words here.\n");
        // The narrowest window the layout is meant for (MainWindow MinWidth/MinHeight), so long labels get the least room.
        var w = new MainWindow { Width = 1000, Height = 680 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Wait(v.OpenFolderPathAsync(root));
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [file])!);
            var find = w.GetVisualDescendants().OfType<FindBar>().Single();
            var tree = w.GetVisualDescendants().OfType<FolderExplorer>().Single().FindControl<TreeView>("FolderTree")!;
            var editor = ((NoteDocument)typeof(MainView).GetField("_active", Flags)!.GetValue(v)!).Editor;
            void Sidebar(bool search) { var m = typeof(MainView).GetMethod("ShowSidebar", Flags | BindingFlags.Public)!; m.Invoke(v, m.GetParameters().Length == 2 ? [search, false] : [search]); }
            var menu = w.GetVisualDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu");
            // The check itself: a too-narrow button and a too-narrow label are reported.
            var probe = new Window { Width = 300, Height = 120, Content = new StackPanel { Children = {
                new Button { Width = 60, Content = "Alle Ordner reduzieren" }, new TextBlock { Width = 50, Text = "Tastenkombinationen" } } } };
            probe.Show(); Pump();
            var caught = Clipped(probe);
            Check(caught.Count == 2, $"the clip check catches a clipped button and label ({string.Join(" | ", caught)})");
            probe.Close(); Pump();
            foreach (var info in Loc.Languages)
            {
                var code = info.Code;
                Loc.Set(code); Pump(); Pump();
                // Main window: explorer, find + replace bar, status bar, tab strip.
                find.IsVisible = true; Pump();
                foreach (var toggle in find.GetVisualDescendants().OfType<ToggleButton>().Where(b => b.Name == "ExpandReplaceToggle")) toggle.IsChecked = true;
                Pump();
                Check(Clipped(w).Count == 0, $"{code}: main window with the explorer, find/replace bar and status bar ({string.Join(" | ", Clipped(w))})");
                Sidebar(true); Pump(); Pump();
                Check(Clipped(w).Count == 0, $"{code}: main window with the search sidebar ({string.Join(" | ", Clipped(w))})");
                Sidebar(false); find.IsVisible = false; Pump();

                // Top menus and their submenus, with shortcut hints.
                foreach (var top in menu.Items.OfType<MenuItem>())
                {
                    top.Open(); Pump(); Pump();
                    CheckMenu($"{code}: menu {top.Name ?? top.Header}", top.Items.OfType<MenuItem>().ToList());
                    foreach (var sub in top.Items.OfType<MenuItem>().Where(i => i.IsVisible && i.ItemCount > 0).ToList())
                    {
                        sub.Open(); Pump(); Pump();
                        CheckMenu($"{code}: submenu {sub.Name ?? sub.Header}", sub.Items.OfType<MenuItem>().ToList());
                        sub.Close(); Pump();
                    }
                    top.Close(); Pump();
                }
                // Context menus.
                foreach (var (label, owner) in new (string, Control)[] {
                    ("file", tree.GetVisualDescendants().OfType<TreeViewItem>().First(t => t.Tag is FolderEntry { IsDirectory: false })),
                    ("folder", tree.GetVisualDescendants().OfType<TreeViewItem>().First(t => t.Tag is FolderEntry { IsDirectory: true })),
                    ("explorer background", tree), ("editor", editor) })
                {
                    var context = owner.ContextMenu!;
                    context.Open(owner); Pump(); Pump();
                    CheckMenu($"{code}: {label} context menu", context.Items.OfType<MenuItem>().ToList());
                    context.Close(); Pump();
                }
                // Preview- and editor-settings flyouts (item 63; the editor one includes item 62's colours).
                foreach (var (gearName, label) in new[] { ("PreviewSettingsButton", "preview settings"), ("EditorSettingsButton", "editor settings") })
                {
                    var gear = w.GetVisualDescendants().OfType<Button>().First(b => b.Name == gearName);
                    var flyout = (Flyout)gear.Flyout!;
                    flyout.ShowAt(gear); Pump(); Pump(); Pump();
                    var content = (Control)flyout.Content!;
                    Check(flyout.IsOpen && content.IsEffectivelyVisible, $"{code}: {label} flyout opens");
                    if (gearName == "EditorSettingsButton")
                        Check(content.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == "EditorInsertSpacesBox" && c.IsEffectivelyVisible)
                            && content.GetVisualDescendants().OfType<NumericUpDown>().Any(c => c.Name == "EditorTabSizeBox" && c.IsEffectivelyVisible), $"{code}: editor settings show the tab settings (item 70)");
                    if (gearName == "EditorSettingsButton")
                        Check(new[] { "EditorAutoListsBox", "EditorAutoTablesBox", "EditorAlignTablesBox" }.All(n => content.GetVisualDescendants().OfType<CheckBox>().Any(c => c.Name == n && c.IsEffectivelyVisible)),
                            $"{code}: editor settings show the auto-complete switches (item 71)");
                    var clipped = ClippedIn(content);
                    Check(clipped.Count == 0, $"{code}: {label} flyout fits ({string.Join(" | ", clipped)})");
                    flyout.Hide(); Pump();
                }
                // Dialogs.
                void Dialog(string label, Func<Task> show)
                {
                    var task = show(); Pump(); Pump(); Pump();
                    var dialog = w.OwnedWindows.LastOrDefault();
                    Check(dialog is not null, $"{code}: {label} dialog opens");
                    var clipped = Clipped(dialog!);
                    Check(clipped.Count == 0, $"{code}: {label} dialog fits ({string.Join(" | ", clipped)})");
                    dialog!.Close(); Wait(task);
                }
                Dialog("about", () => DialogService.ShowAboutAsync(w));
                Dialog("shortcuts", () => DialogService.ShowShortcutsAsync(w));
                Dialog("new file", () => DialogService.PromptNameAsync(w, Loc.T("NewFile"), root, "Untitled.md"));
                Dialog("go to line", () => DialogService.PromptLineAsync(w, 120, 3));
                Dialog("unsaved changes", () => DialogService.ShowAsync(w, Loc.T("UnsavedTitle"), Loc.F("SaveChangesTo", "a.md"), Loc.T("Save"), Loc.T("Discard"), Loc.T("Cancel")));
                Dialog("replace in folder", () => DialogService.ShowAsync(w, Loc.T("ReplaceInFolderTitle"), Loc.F("ReplaceConfirm", 1234, 56), Loc.T("ReplaceAll"), Loc.T("Cancel")));
                Dialog("large file", () => DialogService.ShowAsync(w, Loc.T("LargeFileTitle"), Loc.T("LargeFileConfirm"), Loc.T("OpenAnyway"), Loc.T("Cancel")));
                Dialog("run program", () => DialogService.ShowAsync(w, Loc.T("RunProgramTitle"), Loc.F("RunProgramConfirm", "tool.exe"), Loc.T("RunProgram"), Loc.T("Cancel")));
                Dialog("reset shortcuts", () => DialogService.ShowAsync(w, Loc.T("ShortcutResetAll"), Loc.T("ShortcutResetAllConfirm"), Loc.T("ResetAll"), Loc.T("Cancel")));
                Dialog("not found", () => DialogService.ShowAsync(w, Loc.T("NotFoundTitle"), Loc.F("RemoveRecentConfirm", "notes"), Loc.T("Remove"), Loc.T("Keep")));
            }
        }
        finally { Loc.Set(Loc.English); foreach (var o in w.OwnedWindows.ToList()) o.Close(); try { w.Close(); Pump(); } catch { } try { Directory.Delete(root, true); } catch { } }
        Console.WriteLine($"Long text layout tests passed: {_count}");
    }

    // Menu items: each header fits its popup, ends before the shortcut hint, and the hint is unclipped inside the popup.
    static void CheckMenu(string tag, List<MenuItem> items)
    {
        items = items.Where(i => i.IsEffectivelyVisible).ToList();
        if (items.Count == 0) return;
        var popup = TopLevel.GetTopLevel(items[0]);
        Check(popup is not null, $"{tag} opens");
        var problems = new List<string>();
        foreach (var item in items)
        {
            var header = item.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().FirstOrDefault(p => p.Name == "PART_HeaderPresenter");
            var text = header?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrEmpty(t.Text));
            if (header is null || text is null) continue;
            var needed = text.DesiredSize.Width - text.Margin.Left - text.Margin.Right;
            var textRight = text.TranslatePoint(new Point(Math.Max(text.Bounds.Width, needed), 0), popup!)!.Value.X;
            if (needed > text.Bounds.Width + 1 || textRight > popup!.ClientSize.Width - 1) problems.Add($"{item.Header} (needs {needed:0}, has {text.Bounds.Width:0}, right {textRight:0} of {popup!.ClientSize.Width:0})");
            var hint = item.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(b => b.Name == "PART_InputGestureText" && b.IsEffectivelyVisible && !string.IsNullOrEmpty(b.Text));
            if (hint is null) continue;
            var hintLeft = hint.TranslatePoint(default, popup!)!.Value.X;
            var hintRight = hint.TranslatePoint(new Point(hint.Bounds.Width, 0), popup!)!.Value.X;
            if (hintLeft < textRight + 4 || hintRight > popup!.ClientSize.Width - 1 || hint.DesiredSize.Width - hint.Margin.Left - hint.Margin.Right > hint.Bounds.Width + 0.6)
                problems.Add($"{item.Header} hint {hint.Text} ({hintLeft:0}-{hintRight:0}, text ends {textRight:0}, popup {popup!.ClientSize.Width:0})");
        }
        Check(problems.Count == 0, $"{tag}: every item and shortcut hint fits ({string.Join(" | ", problems)})");
    }

    // Visible text that is cut off: a single-line text wider than its box, any text box running past its button,
    // dialog or window, or a button running out of its window.
    static List<string> Clipped(TopLevel top)
    {
        var result = new List<string>();
        var width = top.ClientSize.Width;
        foreach (var tb in top.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!tb.IsEffectivelyVisible || string.IsNullOrEmpty(tb.Text) || tb.Bounds.Width <= 0) continue;
            if (tb.FindAncestorOfType<TextBox>() is not null || tb.FindAncestorOfType<MdEditor>() is not null || tb.FindAncestorOfType<ScrollViewer>() is { } sv && sv.FindAncestorOfType<TabControl>() is not null && tb.FindAncestorOfType<TabItem>() is null) continue;
            if (tb.TextTrimming != TextTrimming.None) continue; // ellipsis is the intended fallback there
            var needed = tb.TextWrapping == TextWrapping.NoWrap ? tb.TextLayout.WidthIncludingTrailingWhitespace : tb.TextLayout.Width;
            var trailing = tb.Text!.EndsWith(' ') ? tb.TextLayout.WidthIncludingTrailingWhitespace - tb.TextLayout.Width : 0;
            if (needed - trailing > tb.Bounds.Width + 1) { result.Add($"{tb.Text} ({needed:0}>{tb.Bounds.Width:0})"); continue; }
            if (tb.FindAncestorOfType<ScrollViewer>() is not null && tb.FindAncestorOfType<Window>() is not null && top is Window && tb.FindAncestorOfType<ScrollViewer>()!.Name is not null) continue;
            var container = (Visual?)tb.FindAncestorOfType<Button>() ?? top;
            var right = tb.TranslatePoint(new Point(Math.Min(tb.Bounds.Width, needed), 0), container);
            var limit = container == top ? width : container.Bounds.Width;
            if (right is { } r && r.X > limit + 1) result.Add($"{tb.Text} (ends at {r.X:0} of {limit:0})");
        }
        foreach (var button in top.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible && b.Bounds.Width > 0))
        {
            var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), top);
            if (right is { } r && r.X > width + 1 && button.FindAncestorOfType<ScrollViewer>() is null) result.Add($"button {button.Name ?? button.Content?.ToString()} ends at {r.X:0} of {width:0}");
        }
        return result;
    }

    // Text clipped inside a flyout or other panel: a single-line text wider than its box, or any text or
    // control running past the panel's right edge.
    static List<string> ClippedIn(Control root)
    {
        var result = new List<string>();
        var width = root.Bounds.Width;
        foreach (var tb in root.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!tb.IsEffectivelyVisible || string.IsNullOrEmpty(tb.Text) || tb.Bounds.Width <= 0 || tb.TextTrimming != TextTrimming.None) continue;
            if (tb.FindAncestorOfType<TextBox>() is not null) continue;
            var needed = tb.TextWrapping == TextWrapping.NoWrap ? tb.TextLayout.WidthIncludingTrailingWhitespace : tb.TextLayout.Width;
            var trailing = tb.Text!.EndsWith(' ') ? tb.TextLayout.WidthIncludingTrailingWhitespace - tb.TextLayout.Width : 0;
            if (needed - trailing > tb.Bounds.Width + 1) { result.Add($"{tb.Text} ({needed:0}>{tb.Bounds.Width:0})"); continue; }
            var right = tb.TranslatePoint(new Point(Math.Min(tb.Bounds.Width, needed - trailing), 0), root);
            if (right is { } r && r.X > width + 1) result.Add($"{tb.Text} (ends at {r.X:0} of {width:0})");
        }
        foreach (var c in root.GetVisualDescendants().OfType<Control>().Where(c => c is Button or ComboBox or NumericUpDown or TextBox && c.IsEffectivelyVisible && c.Bounds.Width > 0))
        {
            var right = c.TranslatePoint(new Point(c.Bounds.Width, 0), root);
            if (right is { } r && r.X > width + 1) result.Add($"{c.GetType().Name} {c.Name} ends at {r.X:0} of {width:0}");
        }
        return result;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _count++;
        Console.WriteLine("PASS: " + message);
    }
}
