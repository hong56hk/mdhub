using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 41: Reveal in Side Bar (editor/preview context menus, View menu, Ctrl+E, Ctrl+Shift+E).
internal static class RevealTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        bool Until(Func<bool> ok) { var end = DateTime.UtcNow.AddSeconds(8); while (!ok() && DateTime.UtcNow < end) Pump(); return ok(); }
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-reveal-ui-{Guid.NewGuid():N}");
        string Make(string rel) { var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, "# " + rel); return p; }
        for (var i = 0; i < 60; i++) Make($"d{i:00}/a.md"); // folders sort first: zz/ is far below the fold
        var deep = Make("zz/inner/deepest/target.md");
        var shallow = Make("zz/other.md");
        var outside = Path.Combine(Path.GetTempPath(), $"mdhub-reveal-out-{Guid.NewGuid():N}.md"); File.WriteAllText(outside, "x");
        var w = new MainWindow { Width = 1100, Height = 600 };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var explorer = w.GetVisualDescendants().OfType<FolderExplorer>().Single();
            var tree = explorer.FindControl<TreeView>("FolderTree")!;
            NoteDocument? Active() => (NoteDocument?)typeof(MainView).GetField("_active", Flags)!.GetValue(v);
            int DocCount() => ((System.Collections.IList)typeof(MainView).GetField("_documents", Flags)!.GetValue(v)!).Count;
            Task OpenFile(string p) => (Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [p])!;
            string? Status() => (string?)typeof(MainView).GetProperty("TransientStatus", Flags)!.GetValue(v);
            bool CanReveal() => (bool)typeof(MainView).GetProperty("CanRevealInSidebar", Flags)!.GetValue(v)!;
            void Key(Key key, RawInputModifiers mods) { w.KeyPress(key, mods, PhysicalKey.None, null); w.KeyRelease(key, mods, PhysicalKey.None, null); Pump(); }
            TreeViewItem? Row(string path) => tree.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(t => t.Tag is FolderEntry e && string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            TreeViewItem? Selected() => tree.SelectedItem as TreeViewItem;
            bool SelectedIs(string p) => Selected()?.Tag is FolderEntry e && string.Equals(e.Path, p, StringComparison.OrdinalIgnoreCase);
            bool InView(TreeViewItem item)
            {
                var sv = tree.GetVisualDescendants().OfType<ScrollViewer>().First();
                var head = item.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == "PART_Header") ?? item;
                var top = head.TranslatePoint(new Point(0, 0), sv); if (top is null) return false;
                return top.Value.Y >= -0.5 && top.Value.Y + Math.Min(head.Bounds.Height, 24) <= sv.Viewport.Height + 0.5 && sv.Offset.Y > 0;
            }
            void CollapseAll() { foreach (var t in tree.GetVisualDescendants().OfType<TreeViewItem>().ToList()) t.IsExpanded = false; tree.SelectedItem = null; Pump(); }
            void ScrollTop() { var sv = tree.GetVisualDescendants().OfType<ScrollViewer>().First(); sv.Offset = new Vector(0, 0); Pump(); }
            ContextMenu EditorMenu() => Active()!.Editor.ContextMenu!;
            MenuItem MenuItemOf(ContextMenu m, string name) => m.Items.OfType<MenuItem>().Single(i => i.Name == name);
            void OpenMenu(ContextMenu m, Control target) { m.Open(target); Pump(); }

            var sc = Shortcuts.All.Single(s => s.Id == "file.revealInSidebar");
            Check(sc.IsCustomizable && sc.Gestures.Count == 1 && sc.Gestures[0].Key == "E" && sc.Gestures[0].Mods == ShortcutMods.Command, "Ctrl+E is in the shortcut table as a customizable entry");
            Check(Shortcuts.FindConflicts(sc.Id, sc.Gestures[0]).Count == 0, "Ctrl+E conflicts with no other shortcut");
            Check(!Shortcuts.Rebind(sc.Id, ShortcutGesture.CmdShift("E")) && !sc.IsCustomized, "Rebinding it onto Ctrl+Shift+E is refused by the conflict check");
            foreach (var code in new[] { "en", "zh-Hant", "ja", "ko" })
            {
                var table = Loc.Table(code);
                Check(new[] { "RevealInSidebar", "Sc.file.revealInSidebar", "RevealNoPath", "RevealOutsideFolder" }.All(k => table.TryGetValue(k, out var s) && s.Length > 0), $"{code}: the Reveal in Side Bar strings are localized");
            }
            Check(Loc.Table("ja")["RevealInSidebar"] != Loc.Table("en")["RevealInSidebar"] && Loc.T("RevealInSidebar") != Loc.T("Reveal"), "Reveal in Side Bar is translated and distinct from Reveal in file explorer");
            var viewItem = w.GetVisualDescendants().OfType<TopMenu>().Single().GetLogicalDescendantsSafe().FirstOrDefault(i => i.Name == "RevealInSidebarMenuItem");
            Check(viewItem is { InputGesture: not null } && viewItem.InputGesture.ToString().Contains('E'), "The View menu has Reveal in Side Bar with its Ctrl+E gesture");

            Wait(v.OpenFolderPathAsync(root));
            Wait(OpenFile(deep));
            CollapseAll(); ScrollTop();
            Check(Row(deep) is null || !Row(deep)!.IsVisible || Row(Path.GetDirectoryName(deep)!)?.IsExpanded != true, "Before revealing, the nested folders are collapsed");
            var docs = DocCount();
            Active()!.Editor.Focus(); Pump();
            Key(Avalonia.Input.Key.E, RawInputModifiers.Control);
            Check(Until(() => SelectedIs(deep)), "Ctrl+E selects the active file in the explorer");
            foreach (var dir in new[] { "zz", "zz/inner", "zz/inner/deepest" })
                Check(Row(Path.Combine(root, dir.Replace('/', Path.DirectorySeparatorChar)))?.IsExpanded == true, $"Ctrl+E expands the parent folder {dir}");
            Check(Until(() => InView(Selected()!)), "Ctrl+E scrolls the revealed file into view");
            Check(Until(() => Selected()!.IsKeyboardFocusWithin), "Ctrl+E focuses the explorer on the revealed row");
            Check(DocCount() == docs && Active()?.Path == deep, "Revealing does not reopen or duplicate the file");

            CollapseAll(); ScrollTop(); Active()!.Editor.Focus(); Pump();
            Key(Avalonia.Input.Key.E, RawInputModifiers.Control | RawInputModifiers.Shift);
            Check(Until(() => SelectedIs(deep) && Row(Path.GetDirectoryName(deep)!)?.IsExpanded == true && InView(Selected()!)), "Ctrl+Shift+E shows the explorer and reveals the active file the same way");

            CollapseAll(); ScrollTop();
            var menu = EditorMenu();
            OpenMenu(menu, Active()!.Editor);
            var reveal = MenuItemOf(menu, "RevealInSidebarMenuItem");
            var system = MenuItemOf(menu, "RevealInSystemMenuItem");
            Check(new[] { "EditorCutMenuItem", "EditorCopyMenuItem", "EditorPasteMenuItem", "EditorSelectAllMenuItem" }.All(n => menu.Items.OfType<MenuItem>().Any(i => i.Name == n)), "The source editor menu keeps Cut, Copy, Paste and Select All");
            Check(reveal.IsEnabled && (reveal.Header as string) == Loc.T("RevealInSidebar") && reveal.InputGesture is not null, "The source editor menu has an enabled Reveal in Side Bar with its gesture");
            Check(system.IsEnabled && (system.Header as string) == Loc.T("Reveal"), "Reveal in file explorer stays a separate item");
            menu.Close(); Pump();
            reveal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(Until(() => SelectedIs(deep) && Row(Path.GetDirectoryName(deep)!)?.IsExpanded == true), "Clicking Reveal in Side Bar in the editor menu reveals the file");

            CollapseAll();
            var preview = (Control)typeof(MainView).GetField("MdEditor", Flags)!.GetValue(v)!;
            var pmenu = preview.ContextMenu!;
            OpenMenu(pmenu, preview);
            var preveal = MenuItemOf(pmenu, "RevealInSidebarMenuItem");
            Check(preveal.IsEnabled && pmenu.Items.OfType<MenuItem>().Any(i => i.Name == "RevealInSystemMenuItem"), "The preview menu has Reveal in Side Bar and Reveal in file explorer");
            pmenu.Close(); Pump();
            preveal.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Check(Until(() => SelectedIs(deep)), "Clicking Reveal in Side Bar in the preview menu reveals the file");

            Wait(OpenFile(shallow));
            Check(Until(() => true) && CanReveal(), "A shallower file in the folder can be revealed");

            var untitled = (NoteDocument)typeof(MainView).GetMethod("AddDocument", Flags)!.Invoke(v, [null, "scratch", null, null])!; Pump();
            Check(Active() == untitled && !CanReveal(), "An untitled note cannot be revealed");
            OpenMenu(untitled.Editor.ContextMenu!, untitled.Editor);
            Check(!MenuItemOf(untitled.Editor.ContextMenu!, "RevealInSidebarMenuItem").IsEnabled && !MenuItemOf(untitled.Editor.ContextMenu!, "RevealInSystemMenuItem").IsEnabled, "Both reveal items are disabled for an untitled note");
            untitled.Editor.ContextMenu!.Close(); Pump();
            untitled.Editor.Focus(); Pump();
            Key(Avalonia.Input.Key.E, RawInputModifiers.Control);
            Check(Status() == Loc.T("RevealNoPath"), "Ctrl+E on an untitled note shows a status note");

            Wait(OpenFile(outside)); Pump();
            Check(Active()?.Path == outside && !CanReveal(), "A file outside the open folder cannot be revealed");
            OpenMenu(EditorMenu(), Active()!.Editor);
            Check(!MenuItemOf(EditorMenu(), "RevealInSidebarMenuItem").IsEnabled && MenuItemOf(EditorMenu(), "RevealInSystemMenuItem").IsEnabled, "Outside the folder Reveal in Side Bar is disabled, Reveal in file explorer is not");
            EditorMenu().Close(); Pump();
            var before = Selected();
            Active()!.Editor.Focus(); Pump();
            Key(Avalonia.Input.Key.E, RawInputModifiers.Control);
            Check(Status() == Loc.T("RevealOutsideFolder") && Selected() == before, "Ctrl+E outside the folder shows a status note and changes nothing");
        }
        finally
        {
            foreach (var o in w.OwnedWindows.ToList()) o.Close();
            try { w.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); File.Delete(outside); } catch { }
        }
        Console.WriteLine($"Reveal tests: {passed} passed");
    }

    static IEnumerable<MenuItem> GetLogicalDescendantsSafe(this Control c) => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(c).OfType<MenuItem>();
}