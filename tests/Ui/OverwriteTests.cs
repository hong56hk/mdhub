using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 66: Insert toggles insert/overwrite typing (app-wide, Insert at startup), shown by the caret and an INS/OVR status item.
internal static class OverwriteTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Settle(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        Check(!EditorMode.Overwrite && !typeof(SessionData).GetProperties().Any(p => p.Name.Contains("Overwrite") || p.Name.Contains("Insert")), "The app starts in insert mode and the mode is never saved");
        var sc = Shortcuts.All.FirstOrDefault(s => s.Id == "editor.toggleOverwrite");
        Check(sc is { Area: ShortcutArea.Editor, Label: "Toggle insert/overwrite", HandledByTextBox: false } && sc.Defaults.Count == 1 && sc.Defaults[0].Key == "Insert" && sc.Defaults[0].Mods == ShortcutMods.None,
            "The shortcut registry has 'Toggle insert/overwrite' on Insert");
        Check(Loc.Languages.All(l => Loc.Table(l.Code).ContainsKey("Sc.editor.toggleOverwrite") && Loc.Table(l.Code).ContainsKey("ModeInsert") && Loc.Table(l.Code).ContainsKey("ModeOverwrite") && Loc.Table(l.Code).ContainsKey("ModeToggleTip"))
            && Loc.Languages.Where(l => l.Code != Loc.English).All(l => Loc.Table(l.Code)["Sc.editor.toggleOverwrite"] != Loc.En["Sc.editor.toggleOverwrite"] && Loc.Table(l.Code)["ModeOverwrite"] != Loc.En["ModeOverwrite"]),
            $"The shortcut and INS/OVR are localized in all {Loc.Languages.Count} languages");
        var w = new MainWindow { Width = 1000, Height = 700 };
        try
        {
            w.Show(); Settle();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            var status = w.GetVisualDescendants().OfType<BottomStatusBar>().Single();
            var mode = status.FindControl<TextBlock>("ModeLabel")!;
            var doc = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "abc\ndef", "o.md", null])!;
            Settle();
            var ed = doc.Editor;
            ed.Focus(); Settle();
            void Type(string s) { ed.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = s }); Settle(); }
            void Caret(int i) { ed.SelectionStart = ed.SelectionEnd = i; ed.CaretIndex = i; Settle(); }
            Check(mode.IsVisible && mode.Text == Loc.T("ModeInsert"), $"The status bar shows INS next to Ln/Col ({mode.Text})");
            Caret(1); Type("X");
            Check(ed.Text == "aXbc\ndef", "Insert mode inserts");

            w.KeyPress(Key.Insert, RawInputModifiers.None, PhysicalKey.Insert, null); Settle();
            Check(EditorMode.Overwrite && mode.Text == Loc.T("ModeOverwrite"), "The Insert key switches to overwrite (OVR)");
            Caret(1); Type("Y");
            Check(ed.Text == "aYbc\ndef" && ed.CaretIndex == 2, "Overwrite replaces the character under the caret");
            Type("ZZZ");
            Check(ed.Text == "aYZZZ\ndef", "At the end of the line typing inserts instead of eating the line break");
            ed.SelectionStart = 6; ed.SelectionEnd = 9; Settle(); Type("q");
            Check(ed.Text == "aYZZZ\nq", "With a selection typing replaces just the selection");
            var presenter = ed.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
            Caret(0); presenter.InvalidateVisual(); Settle();
            var overlay = w.GetVisualDescendants().OfType<CurrentLineOverlay>().First();
            Check(presenter.OverwriteCaret is { Width: > 2 } r && r.Height > 4 && overlay.OverwriteRect() is { Width: > 2 }, $"The caret becomes a block the width of the next character ({presenter.OverwriteCaret})");
            Caret(0);
            Wait(w.Clipboard!.SetTextAsync("PP"));
            ed.Paste(); Settle();
            Check(ed.Text == "PPaYZZZ\nq", $"Paste always inserts ({ed.Text})");

            var other = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "xyz", "p.md", null])!;
            Settle(); other.Editor.Focus(); Settle();
            other.Editor.SelectionStart = other.Editor.SelectionEnd = 0; other.Editor.CaretIndex = 0;
            other.Editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "A" }); Settle();
            Check(other.Editor.Text == "Ayz" && mode.Text == Loc.T("ModeOverwrite"), "The mode is app-wide, not per tab");

            typeof(BottomStatusBar).GetMethod("ModeLabel_Toggle", flags)!.Invoke(status, [mode, null]); Settle();
            Check(!EditorMode.Overwrite && mode.Text == Loc.T("ModeInsert") && presenter.OverwriteCaret is null, "Clicking the status item toggles back to insert (thin caret)");
            Check(ToolTip.GetTip(mode) is string tip && tip.Contains("Insert"), "The status item's tooltip names the shortcut");
            foreach (var l in Loc.Languages) { Loc.Set(l.Code); Settle(); Check(mode.Text == Loc.T("ModeInsert"), $"{l.Code}: the status item follows the language ({mode.Text})"); }
            Loc.Set(Loc.English);
        }
        finally
        {
            EditorMode.Overwrite = false; Loc.Set(Loc.English);
            foreach (var d in w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!.Items.OfType<NoteDocument>()) d.SavedText = d.Text;
            w.Close();
        }
        Console.WriteLine($"Overwrite tests: {passed} passed");
    }
}
