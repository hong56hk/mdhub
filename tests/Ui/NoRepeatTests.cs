using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Bug 87: one-shot shortcuts (NoRepeat) fire once per key press; held-key auto-repeat is ignored until release.
// Editing keys (Ctrl+Z, Ctrl+D, Alt+Up/Down) keep repeating.
internal static class NoRepeatTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 4; i++) Pump(); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"mdhub-norepeat-{Guid.NewGuid():N}")).FullName;
        var w = new MainWindow { Width = 1100, Height = 700 };
        Shortcuts.ResetAll(); Shortcuts.ReleaseAll();
        try
        {
            w.Show(); Settle();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var fired = new List<string>();
            typeof(MainView).GetEvent("ShortcutFired", Flags)!.GetAddMethod(true)!.Invoke(v, [new Action<string>(fired.Add)]);
            var tabs = w.GetVisualDescendants().OfType<ListBox>().Single(l => l.Name == "Tabs");
            NoteDocument Active() => (NoteDocument)tabs.SelectedItem!;
            int Count(string id) => fired.Count(f => f == id);
            const RawInputModifiers Ctrl = RawInputModifiers.Control;
            void Down(Key key, RawInputModifiers mods = Ctrl) { w.KeyPress(key, mods, PhysicalKey.None, null); Settle(); }
            void Up(Key key, RawInputModifiers mods = Ctrl) { w.KeyRelease(key, mods, PhysicalKey.None, null); Settle(); }
            void Hold(Key key, int downs, RawInputModifiers mods = Ctrl) { for (var i = 0; i < downs; i++) Down(key, mods); }
            string Open(string name, string text) { var p = Path.Combine(root, name); File.WriteAllText(p, text); Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [p])!); Settle(); return p; }

            // The flag itself.
            string[] once = ["file.new", "file.open", "file.quickOpen", "file.openFolder", "file.save", "file.saveAs", "file.closeTab", "explorer.rename", "explorer.delete", "view.wordWrap", "view.togglePreview", "editor.goToLine", "editor.toggleOverwrite"];
            Check(once.All(id => Shortcuts.Get(id).NoRepeat), "one-shot commands are NoRepeat: " + string.Join(", ", once));
            Check(new[] { "edit.undo", "edit.redo", "edit.duplicateLine", "edit.moveLineUp", "edit.moveLineDown", "editor.bold", "editor.insertTime", "find.next", "zoom.in" }.All(id => !Shortcuts.Get(id).NoRepeat), "editing, find-next and zoom keys keep auto-repeat");

            // Ctrl+S held: five KeyDowns, one save; release, press again: a second save.
            var path = Open("a.md", "hello");
            Active().Editor.Focus(); Active().Editor.CaretIndex = 5; Settle();
            Active().Editor.Text = "hello edited"; Settle();
            fired.Clear();
            Hold(Key.S, 5);
            Check(Count("file.save") == 1 && File.ReadAllText(path) == "hello edited", $"holding Ctrl+S (5 KeyDowns) saves exactly once ({Count("file.save")})");
            Check(Active().Editor.Text == "hello edited", "the swallowed repeats type nothing into the editor");
            Up(Key.S);
            Hold(Key.S, 3); Up(Key.S);
            Check(Count("file.save") == 2, $"after release, pressing Ctrl+S again saves again ({Count("file.save")})");
            // Releasing Ctrl (S still down) also re-arms.
            Hold(Key.S, 2); w.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.None, null); Settle(); Down(Key.S); Up(Key.S);
            Check(Count("file.save") == 4, $"releasing Ctrl re-arms Ctrl+S ({Count("file.save")})");
            // Deactivation (a dialog / another app took the KeyUp) never leaves it stuck.
            Hold(Key.S, 2);
            Check(Shortcuts.IsHeld("file.save"), "Ctrl+S is held while down");
            Shortcuts.ReleaseAll(); // what MainWindow.Deactivated does
            Down(Key.S);
            Check(Count("file.save") == 6 && Shortcuts.IsHeld("file.save"), $"after deactivation the next Ctrl+S fires again ({Count("file.save")})");
            Up(Key.S);
            Check(!Shortcuts.IsHeld("file.save"), "KeyUp clears the held state");

            // Ctrl+W held: closes one tab per press.
            Open("b.md", "b"); Open("c.md", "c"); Open("d.md", "d"); Open("e.md", "e");
            var before = tabs.ItemCount;
            fired.Clear();
            Active().Editor.Focus(); Settle();
            Hold(Key.W, 4);
            Check(tabs.ItemCount == before - 1 && Count("file.closeTab") == 1, $"holding Ctrl+W (4 KeyDowns) closes exactly one tab ({before} -> {tabs.ItemCount})");
            Up(Key.W);
            Active().Editor.Focus(); Settle();
            Hold(Key.W, 2); Up(Key.W);
            Check(tabs.ItemCount == before - 2 && Count("file.closeTab") == 2, $"after release, Ctrl+W closes the next tab ({tabs.ItemCount})");

            // Ctrl+D (app-handled) and Ctrl+Z (TextBox) still auto-repeat.
            Open("f.md", "line");
            var ed = Active().Editor; ed.Focus(); ed.CaretIndex = 0; Settle();
            fired.Clear();
            Hold(Key.D, 3); Up(Key.D);
            Check(Count("edit.duplicateLine") == 3 && ed.Text!.Split('\n').Length == 4, $"holding Ctrl+D duplicates the line three times ({Count("edit.duplicateLine")}, '{ed.Text!.Replace("\n", "\\n")}')");
            Hold(Key.Z, 3); Up(Key.Z);
            Check(ed.Text == "line", $"holding Ctrl+Z undoes three times ('{ed.Text!.Replace("\n", "\\n")}')");
            ed.CaretIndex = 0; Settle(); fired.Clear();
            Hold(Key.Down, 2, RawInputModifiers.Alt); Up(Key.Down, RawInputModifiers.Alt);
            Check(Count("edit.moveLineDown") == 2, $"Alt+Down keeps repeating ({Count("edit.moveLineDown")})");
            Console.WriteLine($"No-repeat tests: {passed} passed");
        }
        finally { Shortcuts.ReleaseAll(); try { w.Close(); Pump(); } catch { } try { Directory.Delete(root, true); } catch { } }
    }
}