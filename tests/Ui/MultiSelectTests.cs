using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 80: explorer multi-select (Ctrl/Shift/Ctrl+Shift click, Shift+arrows, Ctrl+A, Esc) and the actions on it.
internal static class MultiSelectTests
{
    public static void Run()
    {
        // Pure checks first.
        var r = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ms-root"));
        var top = FolderExplorer.TopLevelOnly([Path.Combine(r, "Docs"), Path.Combine(r, "Docs", "x.md"), Path.Combine(r, "a.md"), Path.Combine(r, "Docs", "Sub", "y.md")]);
        Check(top.SequenceEqual([Path.Combine(r, "Docs"), Path.Combine(r, "a.md")]), "a selected folder overrides its selected children");
        Check(FolderExplorer.TopLevelOnly([Path.Combine(r, "Doc"), Path.Combine(r, "Docs", "x.md")]).Count == 2, "a name prefix is not a parent");
        Check(Shortcuts.Format("explorer.selectAll", false) == "Ctrl+A" && Shortcuts.All.Single(s => s.Id == "explorer.selectAll").ExplorerOnly
            && Shortcuts.Format("edit.selectAll", false) == "Ctrl+A" && !Shortcuts.Conflicts().Any(), "Ctrl+A is an explorer-only shortcut beside the editor's Select all");
        foreach (var language in Loc.Languages.Select(l => l.Code))
        {
            Loc.Set(language);
            foreach (var key in new[] { "Sc.explorer.selectAll", "DeleteItemsConfirmOne", "DeleteItemsConfirmMany", "MovedItemsToBinOne", "MovedItemsToBinMany", "CopyPaths", "PathsCopiedOne", "PathsCopiedMany",
                "CopiedItemsOne", "CopiedItemsMany", "CutItemsOne", "CutItemsMany", "PastedItemsOne", "PastedItemsMany", "MovedItemsOne", "MovedItemsMany", "MoveItemsToRecycleBin" })
                Check(Loc.T(key) != key && !string.IsNullOrWhiteSpace(Loc.T(key)), $"{key} is localized in {language}");
            Check(Loc.Plural("DeleteItemsConfirmOne", "DeleteItemsConfirmMany", 5).Contains('5'), $"the delete count is shown in {language}");
        }
        Loc.Set(Loc.Languages[0].Code);
        Check(Loc.Plural("DeleteItemsConfirmOne", "DeleteItemsConfirmMany", 5).StartsWith("Delete 5 items?") && Loc.Plural("DeleteItemsConfirmOne", "DeleteItemsConfirmMany", 1).StartsWith("Delete 1 item?"), "delete confirmation is pluralized");

        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow { Width = 1100, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<MainView>("MainView")!;
        var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
        explorer.WatchEnabled = false;
        var tree = explorer.FindControl<TreeView>("FolderTree")!;
        var tabs = window.GetVisualDescendants().OfType<ListBox>().Single(x => x.Name == "Tabs");
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Pump() { for (var k = 0; k < 3; k++) { using var timeout = new CancellationTokenSource(20); Dispatcher.UIThread.MainLoop(timeout.Token); } }
        void Await(Task task) { var end = DateTime.UtcNow.AddSeconds(10); while (!task.IsCompleted && DateTime.UtcNow < end) Pump(); Check(task.IsCompleted, "operation completes"); task.GetAwaiter().GetResult(); Pump(); }
        void Run(string method, params object[] args) => Await((Task)typeof(MainView).GetMethod(method, flags)!.Invoke(view, args)!);
        bool Busy() => (bool)typeof(MainView).GetField("_busy", flags)!.GetValue(view)!;
        void WaitFor(Func<bool> condition) { var end = DateTime.UtcNow.AddSeconds(10); while ((!condition() || Busy()) && DateTime.UtcNow < end) Pump(); }
        var recycled = new List<string>();
        typeof(MainView).GetProperty("RecycleOverride", flags)!.SetValue(view, new Action<string>(p => { recycled.Add(p); if (Directory.Exists(p)) Directory.Delete(p, true); else File.Delete(p); }));
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-multi-" + Guid.NewGuid().ToString("N"))).FullName;
        string P(string name) => Path.Combine(root, name);
        try
        {
            Directory.CreateDirectory(P("Docs"));
            foreach (var f in new[] { "a.md", "b.md", "c.md", "z.md", Path.Combine("Docs", "x.md"), Path.Combine("Docs", "y.md") }) File.WriteAllText(P(f), f);
            Run("LoadRootAsync", root);
            TreeViewItem Row(string path) => tree.GetVisualDescendants().OfType<TreeViewItem>().Concat(TreeAll()).First(i => i.Tag is FolderEntry e && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            IEnumerable<TreeViewItem> TreeAll() { var stack = new Stack<TreeViewItem>(tree.Items.OfType<TreeViewItem>()); while (stack.Count > 0) { var i = stack.Pop(); yield return i; foreach (var c in i.Items.OfType<TreeViewItem>()) stack.Push(c); } }
            Row(P("Docs")).IsExpanded = true; WaitFor(() => TreeAll().Any(i => i.Tag is FolderEntry { Path: var p } && p.EndsWith("x.md"))); window.UpdateLayout(); Pump();
            Point At(TreeViewItem i) { window.UpdateLayout(); var h = i.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_HeaderPresenter"); return h.TranslatePoint(new Point(10, h.Bounds.Height / 2), window)!.Value; }
            var ctrlMod = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            void Click(string path, RawInputModifiers mods = RawInputModifiers.None) { var p = At(Row(path)); window.MouseDown(p, MouseButton.Left, mods); window.MouseUp(p, MouseButton.Left, mods); Pump(); }
            void Key(Key k, RawInputModifiers m = RawInputModifiers.None) { window.KeyPress(k, m, PhysicalKey.None, null); window.KeyRelease(k, m, PhysicalKey.None, null); Pump(); }
            string Names() => string.Join(",", explorer.SelectedPaths.Select(Path.GetFileName));
            string Focused() => Path.GetFileName(((FolderEntry)((TreeViewItem)tree.SelectedItem!).Tag!).Path);

            Click(P("a.md"));
            Check(Names() == "a.md" && !explorer.IsMultiSelect && tabs.SelectedItem is NoteDocument { Path: var opened } && opened == P("a.md"), "a plain click selects and opens one file");
            var tabCount = tabs.ItemCount;
            Click(P("c.md"), ctrlMod);
            Check(Names() == "a.md,c.md" && explorer.IsMultiSelect && Focused() == "c.md" && tabs.ItemCount == tabCount, $"Ctrl+click adds a row without opening it ({Names()})");
            Check(Row(P("a.md")).Classes.Contains("multiSelected") && Row(P("c.md")).Classes.Contains("multiSelected") && !Row(P("b.md")).Classes.Contains("multiSelected"), "selected rows carry the multiSelected class");

            // Visuals: every selected row has the item 29 selection colour; only the focused row has the border.
            Color C(string key) => ThemeService.Lookup(key, ThemeService.Current);
            Border LayoutRoot(TreeViewItem i) => i.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");
            Color? Bg(string p) => (LayoutRoot(Row(p)).Background as ISolidColorBrush)?.Color;
            Color? Line(string p) => (LayoutRoot(Row(p)).BorderBrush as ISolidColorBrush)?.Color;
            window.MouseMove(new Point(window.Bounds.Width - 5, window.Bounds.Height - 5)); Pump();
            Check(tree.IsKeyboardFocusWithin && Bg(P("a.md")) == C("ExplorerSelectionBackground") && Bg(P("c.md")) == C("ExplorerSelectionBackground"), $"selected rows use ExplorerSelectionBackground ({Bg(P("a.md"))})");
            Check(Line(P("c.md")) == C("ExplorerSelectionBorder") && Line(P("a.md")) != C("ExplorerSelectionBorder"), "only the focused row gets the focus border");

            Click(P("a.md"), ctrlMod);
            Check(Names() == "c.md" && !explorer.IsMultiSelect && !Row(P("a.md")).Classes.Contains("multiSelected"), $"Ctrl+click toggles a row off ({Names()})");

            Click(P("b.md"));
            Click(P(Path.Combine("Docs", "y.md")), RawInputModifiers.Shift);
            Check(Names() == "y.md,a.md,b.md" && Focused() == "y.md", $"Shift+click selects the visible range from the anchor, across folders ({Names()})");
            Click(P("z.md"), ctrlMod | RawInputModifiers.Shift);
            Check(Names() == "y.md,a.md,b.md,c.md,z.md", $"Ctrl+Shift+click adds a range ({Names()})");
            Key(Avalonia.Input.Key.Escape);
            Check(Names() == "z.md" && !explorer.IsMultiSelect, "Esc returns to single selection");

            Click(P("a.md"));
            Key(Avalonia.Input.Key.Down, RawInputModifiers.Shift); Key(Avalonia.Input.Key.Down, RawInputModifiers.Shift);
            Check(Names() == "a.md,b.md,c.md" && Focused() == "c.md", $"Shift+Down extends the selection ({Names()})");
            Key(Avalonia.Input.Key.Up, RawInputModifiers.Shift);
            Check(Names() == "a.md,b.md", $"Shift+Up shrinks it back toward the anchor ({Names()})");
            Click(P("c.md"));
            Check(Names() == "c.md" && !explorer.IsMultiSelect, "a plain click returns to single selection");

            Key(Avalonia.Input.Key.A, ctrlMod);
            Check(Names() == "Docs,x.md,y.md,a.md,b.md,c.md,z.md", $"Ctrl+A selects every visible row while the explorer has focus ({Names()})");
            Check(explorer.ActionPaths.Select(Path.GetFileName).SequenceEqual(["Docs", "a.md", "b.md", "c.md", "z.md"]), "actions skip the children of a selected folder");
            Row(P("Docs")).IsExpanded = false; Pump();
            Check(Names() == "Docs,a.md,b.md,c.md,z.md" && !Row(P(Path.Combine("Docs", "x.md"))).Classes.Contains("multiSelected"), $"collapsing a folder deselects its hidden children ({Names()})");
            Row(P("Docs")).IsExpanded = true; Pump(); window.UpdateLayout();
            Check(!Row(P(Path.Combine("Docs", "x.md"))).Classes.Contains("multiSelected"), "re-expanding does not bring them back");

            // Ctrl+A in the editor is the editor's.
            Click(P("c.md"));
            var editorBox = view.FindControl<TextEditor>("TextEditor")!.GetVisualDescendants().OfType<TextBox>().First(b => b.IsEffectivelyVisible && b.AcceptsReturn);
            editorBox.Focus(); Pump();
            Key(Avalonia.Input.Key.A, ctrlMod);
            Check(!explorer.IsMultiSelect && editorBox.SelectionStart == 0 && editorBox.SelectionEnd == (editorBox.Text ?? "").Length && editorBox.SelectionEnd > 0, "Ctrl+A in the editor selects the editor text, not explorer rows");

            // Reduced context menu.
            Click(P("a.md")); Click(P("b.md"), ctrlMod);
            Row(P("a.md")).RaiseEvent(new ContextRequestedEventArgs()); Pump();
            var menu = explorer.MultiMenu;
            var headers = string.Join("|", menu.Items.Cast<Control>().Select(e => e is Separator ? "-" : ((MenuItem)e).Header?.ToString()));
            Check(menu.IsOpen && headers == "Reveal in file explorer|-|Cut|Copy|Copy path|-|Delete", $"the multi-select menu has only Reveal, Cut, Copy, Copy path and Delete ({headers})");
            Check(Row(P("a.md")).ContextMenu is { IsOpen: false }, "the single-item menu (Rename, New, Open, Duplicate) stays closed");
            Check(explorer.IsMultiSelect, "opening the menu keeps the selection");
            menu.Close(); Pump();
            Row(P("z.md")).RaiseEvent(new ContextRequestedEventArgs()); Pump();
            Check(!explorer.IsMultiSelect && !menu.IsOpen, "right-clicking outside the selection drops it");
            Row(P("z.md")).ContextMenu?.Close(); Pump();

            // Copy path (newline-separated) and Reveal.
            Click(P("a.md")); Click(P("b.md"), ctrlMod);
            Await(window.Clipboard!.SetTextAsync("old"));
            menu.Items.OfType<MenuItem>().Single(i => i.Name == "MultiCopyPathMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitFor(() => window.Clipboard!.TryGetTextAsync().Result != "old");
            Check(window.Clipboard!.TryGetTextAsync().Result == P("a.md") + Environment.NewLine + P("b.md"), "Copy path copies every path, one per line");
            var revealed = new List<string>();
            typeof(MainView).GetProperty("RevealOverride", flags)!.SetValue(view, new Action<string>(revealed.Add));
            menu.Items.OfType<MenuItem>().Single(i => i.Name == "MultiRevealMenuItem").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitFor(() => revealed.Count > 0);
            Check(revealed.SequenceEqual([P("a.md")]), "Reveal opens one File Explorer window per folder");

            // Copy and paste the selection; the second paste gets -copy names.
            Directory.CreateDirectory(P("Dest"));
            Await(explorer.RefreshAsync());
            Click(P("c.md")); Click(P("z.md"), ctrlMod);
            Key(Avalonia.Input.Key.C, ctrlMod); WaitFor(() => typeof(MainView).GetProperty("ExplorerClipboardPaths", flags)!.GetValue(view) is IReadOnlyList<string> { Count: 2 });
            Check(typeof(MainView).GetProperty("ExplorerClipboardPaths", flags)!.GetValue(view) is IReadOnlyList<string> { Count: 2 }, "Ctrl+C copies the whole selection");
            Click(P("Dest"));
            Key(Avalonia.Input.Key.V, ctrlMod); WaitFor(() => File.Exists(P(Path.Combine("Dest", "z.md"))));
            Click(P(Path.Combine("Dest", "z.md")));
            Key(Avalonia.Input.Key.V, ctrlMod); WaitFor(() => File.Exists(P(Path.Combine("Dest", "z-copy.md"))));
            Check(File.Exists(P(Path.Combine("Dest", "c.md"))) && File.Exists(P(Path.Combine("Dest", "z.md"))) && File.Exists(P(Path.Combine("Dest", "c-copy.md"))) && File.Exists(P(Path.Combine("Dest", "z-copy.md")))
                && File.Exists(P("c.md")), $"Paste copies every item, duplicates get -copy ({string.Join(",", Directory.GetFiles(P("Dest")).Select(Path.GetFileName))}; focused {Focused()})");

            // Cut + paste moves them.
            Click(P(Path.Combine("Dest", "c-copy.md"))); Click(P(Path.Combine("Dest", "z-copy.md")), ctrlMod);
            Key(Avalonia.Input.Key.X, ctrlMod); Pump();
            Click(P("Docs"));
            Key(Avalonia.Input.Key.V, ctrlMod); WaitFor(() => File.Exists(P(Path.Combine("Docs", "z-copy.md"))));
            Check(File.Exists(P(Path.Combine("Docs", "c-copy.md"))) && !File.Exists(P(Path.Combine("Dest", "c-copy.md"))) && !File.Exists(P(Path.Combine("Dest", "z-copy.md"))), "Cut + Paste moves every item");

            // Drag: a press on a selected row carries the whole selection, and the move handles all of it.
            Click(P("Docs")); if (!Row(P("Docs")).IsExpanded) { Row(P("Docs")).IsExpanded = true; Pump(); }
            Click(P(Path.Combine("Dest", "c.md"))); Click(P(Path.Combine("Dest", "z.md")), ctrlMod);
            var press = At(Row(P(Path.Combine("Dest", "c.md"))));
            window.MouseDown(press, MouseButton.Left); Pump();
            var pending = typeof(FolderExplorer).GetField("_pendingDragPaths", flags)!.GetValue(explorer) as IReadOnlyList<string>;
            Check(pending is { Count: 2 } && explorer.IsMultiSelect, "pressing a selected row prepares to drag the whole selection");
            window.MouseUp(press, MouseButton.Left); Pump();
            Check(!explorer.IsMultiSelect && Names() == "c.md", "releasing without dragging returns to single selection");
            Run("MoveExplorerFileAsync", new ExplorerMoveRequest(P(Path.Combine("Dest", "c.md")), P("Docs"), [P(Path.Combine("Dest", "c.md")), P(Path.Combine("Dest", "z.md"))]));
            Check(File.Exists(P(Path.Combine("Docs", "c.md"))) && File.Exists(P(Path.Combine("Docs", "z.md"))) && !File.Exists(P(Path.Combine("Dest", "z.md"))), "dropping moves every dragged item into the folder");

            // New file/folder uses the focused row's location.
            Click(P("a.md")); Click(P(Path.Combine("Docs", "x.md")), ctrlMod);
            Check(explorer.IsMultiSelect && explorer.GetCreationDirectory() == P("Docs"), "New file/folder targets the focused row's folder");

            // Enter opens every selected file and skips folders.
            Click(P("b.md")); Click(P("c.md"), ctrlMod); Click(P("Dest"), ctrlMod);
            Key(Avalonia.Input.Key.Enter);
            WaitFor(() => tabs.Items.OfType<NoteDocument>().Any(d => d.Path == P("c.md")));
            Check(tabs.Items.OfType<NoteDocument>().Any(d => d.Path == P("b.md")) && tabs.Items.OfType<NoteDocument>().Any(d => d.Path == P("c.md")), "Enter opens every selected file in a tab");

            // A refresh keeps the selection for paths that still exist.
            Click(P("b.md")); Click(P("c.md"), ctrlMod); Click(P("z.md"), ctrlMod);
            Await(explorer.RefreshAsync());
            Check(Names() == "b.md,c.md,z.md", $"a refresh keeps the selection ({Names()})");
            File.Delete(P("z.md"));
            Await(explorer.RefreshAsync());
            Check(Names() == "b.md,c.md", $"a refresh drops deleted paths from the selection ({Names()})");

            // Delete asks once, then recycles the top-level items.
            Click(P("Docs")); if (!Row(P("Docs")).IsExpanded) { Row(P("Docs")).IsExpanded = true; Pump(); }
            Click(P(Path.Combine("Docs", "x.md")), ctrlMod); Click(P("a.md"), ctrlMod); Click(P("b.md"), ctrlMod);
            Key(Avalonia.Input.Key.Delete);
            WaitFor(() => window.OwnedWindows.Count == 1);
            var dialog = window.OwnedWindows.Single();
            Check(dialog.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Delete 3 items?") == true), "one confirmation for the selection: Delete 3 items?");
            ClickDialog(dialog, Loc.T("Cancel"));
            Check(recycled.Count == 0 && Directory.Exists(P("Docs")) && File.Exists(P("a.md")), "Cancel keeps every item");
            WaitFor(() => true); explorer.FocusTree(); Pump();
            Check(explorer.IsMultiSelect, "the selection survives a cancelled delete");
            Key(Avalonia.Input.Key.Delete);
            WaitFor(() => window.OwnedWindows.Count == 1);
            ClickDialog(window.OwnedWindows.Single(), Loc.T("MoveItemsToRecycleBin"));
            WaitFor(() => recycled.Count == 3);
            Check(recycled.SequenceEqual([P("Docs"), P("a.md"), P("b.md")]) && !File.Exists(P("b.md")) && !Directory.Exists(P("Docs")), $"confirming recycles the folder (not its children separately) and the files ({string.Join(",", recycled.Select(Path.GetFileName))})");
            Check(!tabs.Items.OfType<NoteDocument>().Any(d => d.Path == P("a.md") || d.Path == P("b.md")), "tabs of deleted files close");
            foreach (var d in tabs.Items.OfType<NoteDocument>()) d.SavedText = d.Text;

            void ClickDialog(Window w, string choice)
            {
                var button = w.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, choice));
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump();
            }
        }
        finally { window.Close(); try { Directory.Delete(root, true); } catch { } }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
