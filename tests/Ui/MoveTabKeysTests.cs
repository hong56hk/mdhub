using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 90: Ctrl+Alt+Right/Left (Cmd+Ctrl on macOS) move the active tab with item 89's rules.
internal static class MoveTabKeysTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SessionManager.Enabled = false;
        Shortcuts.ResetAll(); Shortcuts.ReleaseAll();
        var root = Path.Combine(Path.GetTempPath(), $"mdhub-movekeys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string Make(string name, string text) { var p = Path.Combine(root, name); File.WriteAllText(p, text); return p; }
        var a = Make("a.md", "alpha"); var b = Make("b.md", "bravo"); var c = Make("c.md", "charlie");
        var window = new MainWindow { Width = 1600, Height = 800 };
        void Pump() { using var timeout = new CancellationTokenSource(35); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 3; i++) Pump(); }
        void Wait(Task task) { var until = DateTime.UtcNow.AddSeconds(12); while (!task.IsCompleted && DateTime.UtcNow < until) Pump(); if (!task.IsCompleted) throw new Exception("Operation timed out"); task.GetAwaiter().GetResult(); Pump(); }
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
        const RawInputModifiers CtrlAlt = RawInputModifiers.Control | RawInputModifiers.Alt;
        void Down(Key key, RawInputModifiers mods = CtrlAlt) { window.KeyPress(key, mods, PhysicalKey.None, null); Settle(); }
        void Up(Key key, RawInputModifiers mods = CtrlAlt) { window.KeyRelease(key, mods, PhysicalKey.None, null); Settle(); }
        void Press(Key key, RawInputModifiers mods = CtrlAlt) { Down(key, mods); Up(key, mods); }
        try
        {
            // Registry: listed (item 32 dialog reads Shortcuts.All), one-shot, localized, no clashes.
            foreach (var id in new[] { "view.moveTabRight", "view.moveTabLeft" })
            {
                var s = Shortcuts.Get(id);
                Check(s.NoRepeat && s.IsCustomizable && s.Area == ShortcutArea.View, $"{id} is a customizable one-shot View shortcut");
                Check(Loc.T("Sc." + id) != "Sc." + id && s.DisplayLabel.StartsWith("Move tab to"), $"{id} has a localized label ({s.DisplayLabel})");
            }
            Check(Shortcuts.Format("view.moveTabRight", false) == "Ctrl+Alt+Right" && Shortcuts.Format("view.moveTabLeft", false) == "Ctrl+Alt+Left", "Windows/Linux gestures Ctrl+Alt+Right/Left");
            Check(new ShortcutGesture("Right", ShortcutMods.Command | ShortcutMods.Control).Format(true) == "Cmd+Ctrl+Right", "macOS form formats as Cmd+Ctrl+Right");
            Check(Shortcuts.Parse(Shortcuts.Serialize(new ShortcutGesture("Left", ShortcutMods.Command | ShortcutMods.Control))) == new ShortcutGesture("Left", ShortcutMods.Command | ShortcutMods.Control), "Cmd+Ctrl gestures round-trip through settings");
            Check(!Shortcuts.Conflicts().Any(), "No shortcut shares a gesture with the new entries");
            Check(ShortcutKeys.FromKey(Key.Right, KeyModifiers.Control | KeyModifiers.Alt) == new ShortcutGesture("Right", ShortcutMods.Command | ShortcutMods.Alt), "Recording Ctrl+Alt+Right in the Shortcuts dialog gives the default gesture");
            foreach (var other in new[] { "editor.wordLeft", "editor.wordRight", "view.focusLeftPane", "view.focusRightPane" })
                Check(!Shortcuts.Get(other).Gestures.Any(g => Shortcuts.Get("view.moveTabRight").Gestures.Concat(Shortcuts.Get("view.moveTabLeft").Gestures).Any(x => Shortcuts.SameKeys(g, x))), $"Distinct from {other}");

            window.Show(); Settle();
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            EditorPane ActivePane() => (EditorPane)typeof(MainView).GetProperty("ActivePane", Flags)!.GetValue(view)!;
            List<EditorPane> Panes() => ((IReadOnlyList<EditorPane>)typeof(MainView).GetProperty("EditorPanes", Flags)!.GetValue(view)!).ToList();
            Task Invoke(string method, params object?[] args) => (Task)typeof(MainView).GetMethod(method, Flags)!.Invoke(view, args)!;
            NoteDocument Doc(EditorPane pane, string path) => pane.Documents.First(d => d.Path is { } p && Path.GetFullPath(p) == Path.GetFullPath(path));
            Wait(view.OpenFolderPathAsync(root));
            Wait(Invoke("OpenPathAsync", a));
            var first = ActivePane();
            var docA = Doc(first, a);
            Wait(Invoke("CloseOtherDocumentsAsync", docA));
            docA.Editor.Focus(); Settle();

            Press(Key.Right);
            Check(Panes().Count == 1 && first.Documents.Single() == docA && docA.Text == "alpha", "No-op when the split has one tab (and the editor text is untouched)");

            Wait(Invoke("OpenPathAsync", b));
            Wait(Invoke("OpenPathAsync", c));
            var docC = Doc(first, c);
            Check(first.Active == docC, "c is active");
            docC.Editor.Focus(); Settle();
            docC.Editor.CaretIndex = docC.Editor.Text!.Length;
            docC.Editor.SelectedText = "!"; Settle();
            var caret = docC.Editor.CaretIndex;
            Down(Key.Right);
            Check(Panes().Count == 2 && Panes()[1].Documents.Single() == docC && ActivePane() == Panes()[1] && !first.Documents.Contains(docC), "Ctrl+Alt+Right moves the active tab to a new right split");
            Check(docC.IsDirty && docC.Text == "charlie!" && docC.Editor.CaretIndex == caret, "Unsaved edit and caret travel with the tab");
            Check(docC.Editor.IsKeyboardFocusWithin, "Moved tab's editor has focus");
            Up(Key.Right);
            var right = Panes()[1];
            Press(Key.Left);
            Check(Panes().Count == 2 && right.Documents.Single() == docC && ActivePane() == right, "Ctrl+Alt+Left is a no-op on a one-tab split");
            void Show(EditorPane pane, NoteDocument d)
            {
                typeof(MainView).GetMethod("ActivatePane", Flags)!.Invoke(view, [pane]);
                pane.FindControl<FileTabs>("FileTabs")!.SelectedDocument = d; Settle();
                d.Editor.Focus(); Settle();
            }
            var docB = Doc(first, b);
            Show(first, docB);
            Down(Key.Right); // b -> existing right split
            Check(right.Documents.Contains(docB) && right.Active == docB && ActivePane() == right && first.Documents.Single() == docA, "Ctrl+Alt+Right moves into the existing right split");
            Down(Key.Right); // auto-repeat KeyDowns: right now has 2 tabs, so a repeat would create a third split
            Down(Key.Right);
            Check(Panes().Count == 2 && right.Documents.Contains(docB), "Holding the key moves the tab once (bug 87 no-repeat)");
            Up(Key.Right);
            Press(Key.Left);
            Check(first.Documents.Contains(docB) && first.Active == docB && ActivePane() == first && right.Documents.Single() == docC, "Ctrl+Alt+Left moves into the existing left split");
            docC.Undo(); Settle();
            Check(docC.Text == "charlie", "Undo history kept after a keyboard move");
            docC.Redo(); Settle();

            // Duplicate: b is in the left split; open b again in the right split (dirty there) and move it left.
            Show(right, docC);
            Wait(Invoke("OpenPathAsync", b));
            var dup = ActivePane().Active!;
            Check(ActivePane() == right && dup != docB && dup.Buffer == docB.Buffer && right.Documents.Count == 2, "Second view of b in the right split");
            dup.Editor.Focus(); Settle();
            dup.Editor.CaretIndex = 0; dup.Editor.SelectedText = "X"; Settle();
            Press(Key.Left);
            Check(first.Documents.Count(d => d.Buffer == docB.Buffer) == 1 && !right.Documents.Contains(dup) && ActivePane() == first && first.Active == docB, "Duplicate in target: existing tab focused, no second copy");
            Check(docB.IsDirty && docB.Text == "Xbravo", "Dirty edit kept in the existing tab");

            // Menu hints.
            var list = first.FindControl<FileTabs>("FileTabs")!.FindControl<ListBox>("Tabs")!;
            var menu = list.ContextMenu!;
            MenuItem Item(string name) => menu.Items.OfType<MenuItem>().Single(i => i.Name == name);
            Check(ShortcutKeys.MenuText(Item("TabMoveRightMenuItem")) == "Ctrl+Alt+Right" && ShortcutKeys.MenuText(Item("TabMoveLeftMenuItem")) == "Ctrl+Alt+Left", "Tab menu entries show the shortcut hints");
            Check(Item("TabMoveRightMenuItem").InputGesture?.ToString() == "Ctrl+Alt+Right", $"InputGesture set ({Item("TabMoveRightMenuItem").InputGesture})");
            // Custom binding applies to the key handler and the hint.
            Check(Shortcuts.Rebind("view.moveTabLeft", new ShortcutGesture("F9", ShortcutMods.Command)), "Rebind Move tab left to Ctrl+F9");
            Check(ShortcutKeys.MenuText(Item("TabMoveLeftMenuItem")) == "Ctrl+F9", "Hint follows the custom binding");
            Show(first, docB);
            Press(Key.Left);
            Check(Panes().Count == 2 && first.Documents.Contains(docB), "Old gesture no longer moves");
            Press(Key.F9, RawInputModifiers.Control);
            Check(Panes().Count == 3 && Panes()[0].Documents.Single() == docB && ActivePane() == Panes()[0], "Custom gesture moves left into a new split");
            Shortcuts.ResetAll();
            Console.WriteLine("ALL MOVETAB KEY TESTS PASSED");
        }
        finally
        {
            Shortcuts.ResetAll();
            foreach (var v in window.GetVisualDescendants().OfType<MainView>())
                foreach (var d in (IEnumerable<NoteDocument>)typeof(MainView).GetProperty("AllDocuments", Flags)!.GetValue(v)!) d.SavedText = d.Text;
            window.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
