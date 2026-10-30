using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 49: Ctrl+Tab / Ctrl+Shift+Tab cycle tabs from any focus, wrapping at both ends.
internal static class TabCycleTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        var root = Directory.CreateDirectory(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdhub-tabs-{Guid.NewGuid():N}")).FullName;
        var paths = new[] { "a.md", "b.md", "c.txt" }.Select(n => { var p = System.IO.Path.Combine(root, n); File.WriteAllText(p, "text " + n); return p; }).ToArray();
        var w = new MainWindow { Width = 1100, Height = 700 };
        var startLanguage = Loc.Info.Code;
        Shortcuts.ResetAll();
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var tabs = w.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "Tabs");
            Wait((Task)typeof(MainView).GetMethod("LoadRootAsync", Flags)!.Invoke(v, [root])!);
            foreach (var p in paths) Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [p])!);
            NoteDocument Active() => (NoteDocument)tabs.SelectedItem!;
            string Name() => System.IO.Path.GetFileName(Active().Path!);
            void Press(Interactive target, Key key, KeyModifiers mods, string label)
            {
                var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = target };
                target.RaiseEvent(e); Pump();
                Check(e.Handled, $"{label}: the key press is handled");
            }
            var ctrl = ShortcutKeys.Modifiers(ShortcutMods.Command);
            var ctrlShift = ShortcutKeys.Modifiers(ShortcutMods.Command | ShortcutMods.Shift);
            foreach (var untitled in tabs.Items.OfType<NoteDocument>().Where(d => d.Path is null).ToList())
                Wait((Task)typeof(MainView).GetMethod("CloseDocumentAsync", Flags)!.Invoke(v, [untitled])!);
            Check(tabs.Items.Count == 3, "Exactly the three notes are open");
            Check(Name() == "c.txt", "Three tabs are open, the last one active");

            // From the editor: right wraps from the last tab to the first; no tab character is inserted.
            Active().Editor.Focus(); Pump();
            var before = paths.ToDictionary(p => p, p => File.ReadAllText(p));
            Press(Active().Editor, Key.Tab, ctrl, "editor Ctrl+Tab");
            Check(Name() == "a.md", "Ctrl+Tab from the last tab wraps to the first");
            Press(Active().Editor, Key.Tab, ctrl, "editor Ctrl+Tab");
            Check(Name() == "b.md", "Ctrl+Tab moves one tab right");
            Press(Active().Editor, Key.Tab, ctrlShift, "editor Ctrl+Shift+Tab");
            Press(Active().Editor, Key.Tab, ctrlShift, "editor Ctrl+Shift+Tab");
            Check(Name() == "c.txt", "Ctrl+Shift+Tab moves left and wraps from the first tab to the last");
            var docs = tabs.Items.OfType<NoteDocument>().ToList();
            Check(docs.All(d => !d.Editor.Text!.Contains('\t') && d.Editor.Text == before[d.Path!]), "No tab character was inserted into any editor");

            // From the explorer.
            var tree = w.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "FolderTree");
            var row = tree.Items.OfType<TreeViewItem>().First(); row.Focus(); Pump();
            Check(tree.IsKeyboardFocusWithin, "The explorer has focus");
            Press(row, Key.Tab, ctrl, "explorer Ctrl+Tab");
            Check(Name() == "a.md", "Ctrl+Tab works from the explorer focus");
            Press(row, Key.Tab, ctrlShift, "explorer Ctrl+Shift+Tab");
            Check(Name() == "c.txt", "Ctrl+Shift+Tab works from the explorer focus");

            // Plain Tab still reaches the editor (indent), so only the Ctrl combinations are taken.
            var plain = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = KeyModifiers.None };
            Active().Editor.Focus(); Pump();
            var count = Active().Editor.Text!.Length; Active().Editor.CaretIndex = count;
            Active().Editor.RaiseEvent(plain); Pump();
            Check(Name() == "c.txt" && Active().Editor.Text!.Contains('\t'), "Plain Tab still inserts a tab and does not switch tabs");
            Active().Editor.Text = before[Active().Path!]; Pump();

            // Menu items under View.
            var menu = w.GetVisualDescendants().OfType<TopMenu>().Single();
            var items = menu.GetLogicalDescendants().OfType<MenuItem>().ToList();
            var view = items.Single(i => i.Header is string h && h == Loc.T("MenuView"));
            var next = view.Items.OfType<MenuItem>().Single(i => ShortcutKeys.GetId(i) == "view.nextTab");
            var prev = view.Items.OfType<MenuItem>().Single(i => ShortcutKeys.GetId(i) == "view.previousTab");
            Check(next.Header as string == Loc.T("MenuNextTab") && prev.Header as string == Loc.T("MenuPreviousTab"), "View menu lists Next Tab and Previous Tab");
            Check(next.InputGesture?.Equals(new KeyGesture(Key.Tab, ctrl)) == true && prev.InputGesture?.Equals(new KeyGesture(Key.Tab, ctrlShift)) == true, "The menu items show Ctrl+Tab and Ctrl+Shift+Tab");
            next.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Check(Name() == "a.md", "View > Next Tab wraps to the first tab");
            prev.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Check(Name() == "c.txt", "View > Previous Tab wraps to the last tab");

            // Registry: listed, customizable, conflict-free, localized.
            var sn = Shortcuts.Get("view.nextTab"); var sp = Shortcuts.Get("view.previousTab");
            Check(sn.Area == ShortcutArea.View && sp.Area == ShortcutArea.View && sn.IsCustomizable && sp.IsCustomizable, "Both entries are in the View area and customizable");
            Check(sn.Format(false) == "Ctrl+Tab" && sp.Format(false) == "Ctrl+Shift+Tab", "Default gestures are Ctrl+Tab and Ctrl+Shift+Tab");
            Check(!Shortcuts.Conflicts().Any(c => c.A.Id.EndsWith("Tab") || c.B.Id.EndsWith("Tab")), "No conflicts in the shortcut table");
            Check(Shortcuts.FindConflicts("view.wordWrap", sn.Defaults[0]).Single().Id == "view.nextTab", "Rebinding another entry to Ctrl+Tab reports the conflict");
            Check(Shortcuts.Rebind("view.nextTab", ShortcutGesture.Cmd("PageDown")), "Next tab can be rebound to Ctrl+PageDown");
            Press(tree, Key.PageDown, ctrl, "rebound Ctrl+PageDown");
            Check(Name() == "a.md", "The custom binding switches tabs");
            var old = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Tab, KeyModifiers = ctrl };
            tree.RaiseEvent(old); Pump();
            Check(Name() == "a.md", "The old Ctrl+Tab no longer switches tabs once rebound");
            Shortcuts.ResetAll();
            foreach (var code in new[] { "en", "zh-Hant", "ja", "ko" })
            {
                Loc.Set(code); Pump();
                var t = Loc.Table(code)!;
                var a = t["Sc.view.nextTab"]; var b = t["Sc.view.previousTab"];
                Check(a is { Length: > 0 } && b is { Length: > 0 } && a != b && t["MenuNextTab"] is { Length: > 0 } && t["MenuPreviousTab"] is { Length: > 0 }
                    && (code == "en" || a != "Next tab") && sn.DisplayLabel == a && next.Header as string == t["MenuNextTab"], $"{code}: labels are localized ({a} / {b})");
            }
        }
        finally
        {
            Shortcuts.ResetAll();
            Loc.Set(startLanguage);
            try { w.Close(); Pump(); } catch { }
            Directory.Delete(root, true);
        }
        Console.WriteLine($"Tab cycle tests: {passed} passed");
    }
}