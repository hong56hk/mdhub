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
using Mdhub.Text;
using Mdhub.Views;

// Item 72: Alt+Up/Down move the current/selected lines, Ctrl+D duplicates them (editor focus only; the explorer keeps Ctrl+D).
internal static class LineEditTests
{
    public static void Run()
    {
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        string Show(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");

        // ---- Pure logic ----
        string Apply(string text, LineEdit? e) => e is { } x ? text[..x.Start] + x.Text + text[(x.Start + x.Length)..] : text;
        var t = "a\nbb\nccc\nd";
        var down = LineEdits.Move(t, 3, 3, 1)!.Value;
        Check(Apply(t, down) == "a\nccc\nbb\nd" && down.Caret == 7 && down.Anchor == 7, $"Alt+Down moves the caret's line down and keeps its column ({Show(Apply(t, down))}, caret {down.Caret})");
        var up = LineEdits.Move(t, 3, 3, -1)!.Value;
        Check(Apply(t, up) == "bb\na\nccc\nd" && up.Caret == 1, $"Alt+Up moves it up ({Show(Apply(t, up))}, caret {up.Caret})");
        Check(LineEdits.Move(t, 0, 0, -1) is null && LineEdits.Move(t, t.Length, t.Length, 1) is null, "No move above the first line or below the last");
        var multi = LineEdits.Move(t, 3, 7, 1)!.Value;
        Check(Apply(t, multi) == "a\nd\nbb\nccc" && multi.Anchor == 5 && multi.Caret == 9, $"A selection touching two lines moves both, selection kept ({Show(Apply(t, multi))}, {multi.Anchor}-{multi.Caret})");
        var reversed = LineEdits.Move(t, 7, 3, -1)!.Value;
        Check(Apply(t, reversed) == "bb\nccc\na\nd" && reversed.Anchor == 5 && reversed.Caret == 1, "A backwards selection keeps its anchor and caret");
        var lineStart = LineEdits.Move(t, 2, 5, 1)!.Value;
        Check(Apply(t, lineStart) == "a\nccc\nbb\nd" && lineStart.Anchor == 6 && lineStart.Caret == 9, $"A selection ending at a line start leaves that line out ({Show(Apply(t, lineStart))}, {lineStart.Anchor}-{lineStart.Caret})");
        var last = LineEdits.Move(t, 9, 9, -1)!.Value;
        Check(Apply(t, last) == "a\nbb\nd\nccc" && last.Caret == 5, $"The last line (no trailing newline) moves up and the text still ends without one ({Show(Apply(t, last))})");
        var crlf = "one\r\ntwo\r\nthree";
        var c1 = LineEdits.Move(crlf, 6, 6, 1)!.Value;
        Check(Apply(crlf, c1) == "one\r\nthree\r\ntwo" && c1.Caret == 13, $"CRLF is preserved ({Show(Apply(crlf, c1))})");
        var trailing = "x\r\ny\r\n";
        Check(Apply(trailing, LineEdits.Move(trailing, 0, 0, 1)) == "y\r\nx\r\n", "A trailing newline stays at the end");
        var mixed = "a\r\nb\nc";
        Check(Apply(mixed, LineEdits.Move(mixed, 0, 0, 1)) == "b\r\na\nc", "Mixed endings stay in their slots");
        var dup = LineEdits.Duplicate(t, 4, 4);
        Check(Apply(t, dup) == "a\nbb\nbb\nccc\nd" && dup.Caret == 7, $"Ctrl+D duplicates the line below and keeps the column ({Show(Apply(t, dup))}, caret {dup.Caret})");
        var dupLast = LineEdits.Duplicate(crlf, 12, 12);
        Check(Apply(crlf, dupLast) == "one\r\ntwo\r\nthree\r\nthree" && dupLast.Caret == 19, $"Duplicating the last line adds the text's own line ending ({Show(Apply(crlf, dupLast))})");
        var dupMulti = LineEdits.Duplicate(t, 0, 3);
        Check(Apply(t, dupMulti) == "a\nbb\na\nbb\nccc\nd" && dupMulti.Anchor == 5 && dupMulti.Caret == 8, "Ctrl+D duplicates every selected line, selection on the copy");
        Check(Apply("", LineEdits.Duplicate("", 0, 0)) == Environment.NewLine && LineEdits.Move("", 0, 0, 1) is null, "An empty note: duplicate adds a line, move does nothing");

        // ---- Registry ----
        Check(Shortcuts.Get("edit.moveLineUp") is { Area: ShortcutArea.Edit, HandledByTextBox: false } && Shortcuts.Format("edit.moveLineUp", false) == "Alt+Up" && Shortcuts.Format("edit.moveLineDown", false) == "Alt+Down"
            && Shortcuts.Format("edit.duplicateLine", false) == "Ctrl+D" && Shortcuts.Format("explorer.duplicate", false) == "Ctrl+D", "Registered: Alt+Up, Alt+Down, Ctrl+D (explorer Duplicate keeps Ctrl+D)");
        Check(!Shortcuts.Conflicts().Any() && Shortcuts.FindConflicts("edit.duplicateLine", ShortcutGesture.Cmd("D")).Count == 0, "No registry conflicts (the editor and explorer Ctrl+D never meet)");
        var keys = new[] { "Sc.edit.moveLineUp", "Sc.edit.moveLineDown", "Sc.edit.duplicateLine", "MenuMoveLineUp", "MenuMoveLineDown", "MenuDuplicateLine" };
        Check(Loc.Languages.All(l => keys.All(k => Loc.Table(l.Code).TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v)))
            && Loc.Languages.Where(l => l.Code != Loc.English).All(l => keys.All(k => Loc.Table(l.Code)[k] != Loc.En[k])), $"Localized in all {Loc.Languages.Count} languages");

        // ---- In the app ----
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 6; i++) Pump(); }
        void Wait(Task task) { var end = DateTime.UtcNow.AddSeconds(15); while (!task.IsCompleted && DateTime.UtcNow < end) Pump(); task.GetAwaiter().GetResult(); Settle(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-lineedit-" + Guid.NewGuid().ToString("N"))).FullName;
        var w = new MainWindow { Width = 1000, Height = 700 };
        try
        {
            w.Show(); Settle();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            var doc = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "", "l.md", null])!;
            Settle();
            var ed = doc.Editor;
            void Reset(string text, int a, int c) { ed.Text = text; Settle(); ed.Focus(); ed.SelectionStart = a; ed.SelectionEnd = c; Settle(); }
            void Key(Key key, RawInputModifiers mods) { w.KeyPress(key, mods, PhysicalKey.None, null); Settle(); }
            var ctrl = ShortcutKeys.IsMac ? RawInputModifiers.Meta : RawInputModifiers.Control;

            Reset("a\nbb\nccc\nd", 3, 3); Key(Avalonia.Input.Key.Down, RawInputModifiers.Alt);
            Check(ed.Text == "a\nccc\nbb\nd" && ed.SelectionStart == 7 && ed.SelectionEnd == 7, $"Alt+Down in the editor ({Show(ed.Text!)}, {ed.SelectionStart}-{ed.SelectionEnd})");
            Key(Avalonia.Input.Key.Down, RawInputModifiers.Alt);
            Check(ed.Text == "a\nccc\nd\nbb" && ed.SelectionEnd == 9, "Alt+Down again reaches the last line");
            Key(Avalonia.Input.Key.Down, RawInputModifiers.Alt);
            Check(ed.Text == "a\nccc\nd\nbb", "Alt+Down on the last line does nothing");
            ed.Undo(); Settle();
            Check(ed.Text == "a\nccc\nbb\nd", $"One undo reverts one move ({Show(ed.Text!)})");
            ed.Undo(); Settle();
            Check(ed.Text == "a\nbb\nccc\nd", "A second undo reverts the first move");

            Reset("a\nbb\nccc\nd", 3, 7); Key(Avalonia.Input.Key.Up, RawInputModifiers.Alt);
            Check(ed.Text == "bb\nccc\na\nd" && ed.SelectionStart == 1 && ed.SelectionEnd == 5, $"Alt+Up moves both selected lines and keeps the selection ({ed.SelectionStart}-{ed.SelectionEnd})");
            Key(Avalonia.Input.Key.Up, RawInputModifiers.Alt);
            Check(ed.Text == "bb\nccc\na\nd", "Alt+Up on the first line does nothing");

            Reset("one\r\ntwo\r\nthree", 7, 7); Key(Avalonia.Input.Key.Down, RawInputModifiers.Alt);
            Check(ed.Text == "one\r\nthree\r\ntwo", $"CRLF survives in the editor ({Show(ed.Text!)})");
            Key(Avalonia.Input.Key.D, ctrl);
            Check(ed.Text == "one\r\nthree\r\ntwo\r\ntwo" && ed.SelectionEnd == 19, $"Ctrl+D in the editor duplicates the last line below, caret column kept ({Show(ed.Text!)}, {ed.SelectionEnd})");
            ed.Undo(); Settle();
            Check(ed.Text == "one\r\nthree\r\ntwo", "Ctrl+D is one undo step");

            // Menu items in the Edit menu, with their shortcuts, localized.
            var menu = w.GetVisualDescendants().OfType<TopMenu>().Single();
            var items = menu.GetLogicalDescendants().OfType<MenuItem>().ToList();
            MenuItem Item(string id) => items.Single(i => ShortcutKeys.GetId(i) == id);
            var edit = items.Single(i => i.Header as string == Loc.T("MenuEdit"));
            foreach (var (id, key) in new[] { ("edit.moveLineUp", "MenuMoveLineUp"), ("edit.moveLineDown", "MenuMoveLineDown"), ("edit.duplicateLine", "MenuDuplicateLine") })
                Check(edit.Items.Contains(Item(id)) && Item(id).Header as string == Loc.T(key), $"Edit menu: '{Loc.T(key)}' ({Shortcuts.Format(id, ShortcutKeys.IsMac)})");
            Loc.Set("de"); Settle();
            Check(Item("edit.duplicateLine").Header as string == Loc.T("MenuDuplicateLine") && Loc.T("MenuDuplicateLine") != Loc.En["MenuDuplicateLine"], $"The menu follows the UI language ({Item("edit.duplicateLine").Header})");
            Loc.Set("en"); Settle();
            Reset("a\nb", 0, 0);
            Item("edit.moveLineDown").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            Check(ed.Text == "b\na", "Edit > Move Line Down moves the line");
            Item("edit.moveLineUp").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            Check(ed.Text == "a\nb", "Edit > Move Line Up moves it back");
            Item("edit.duplicateLine").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Settle();
            Check(ed.Text == "a\na\nb", "Edit > Duplicate Line duplicates it");

            // Ctrl+D with the explorer focused still duplicates the explorer item, and leaves the note alone.
            var file = Path.Combine(folder, "note.md"); File.WriteAllText(file, "x");
            Wait((Task)typeof(MainView).GetMethod("LoadRootAsync", flags)!.Invoke(view, [folder])!);
            var explorer = w.GetVisualDescendants().OfType<FolderExplorer>().Single();
            var requests = new List<ExplorerRequest>();
            explorer.ExplorerRequested += requests.Add;
            Wait(explorer.RevealAsync(file, focus: true));
            var before = ed.Text;
            Check(explorer.IsKeyboardFocusWithin && !ed.IsKeyboardFocusWithin, "The explorer has focus");
            Key(Avalonia.Input.Key.D, ctrl);
            Check(requests.Select(r => r.Action).SequenceEqual([ExplorerAction.Duplicate]) && ed.Text == before, $"Explorer focused: Ctrl+D duplicates the explorer item, not the line ({requests.Count})");
            Key(Avalonia.Input.Key.Down, RawInputModifiers.Alt);
            Check(ed.Text == before, "Explorer focused: Alt+Down does not touch the note");
            requests.Clear();
            // Revealing the file opened it; Ctrl+D in its editor duplicates its line (CRLF-free "x" -> "x" + newline + "x").
            var active = (NoteDocument)typeof(MainView).GetField("_active", flags)!.GetValue(view)!;
            var noteEd = active.Editor; var noteBefore = noteEd.Text ?? "";
            noteEd.Focus(); Settle(); noteEd.SelectionStart = noteEd.SelectionEnd = 0; Settle();
            Key(Avalonia.Input.Key.D, ctrl);
            Check(requests.Count == 0 && noteEd.Text == noteBefore + Environment.NewLine + noteBefore && File.Exists(file), $"Editor focused: Ctrl+D duplicates the line, not the explorer item ({Show(noteEd.Text ?? "")})");
        }
        finally
        {
            Loc.Set("en");
            try { w.Close(); Settle(); } catch { }
            try { Directory.Delete(folder, true); } catch { }
        }
        Console.WriteLine($"Line edit tests: {passed} passed");
    }
}