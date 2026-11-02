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

internal static class TextCaseTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed++; Console.WriteLine("PASS " + label); }
        void Pump() { using var timeout = new CancellationTokenSource(60); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Key(Window window, RawInputModifiers modifiers) { window.KeyPress(Avalonia.Input.Key.U, modifiers, PhysicalKey.None, null); window.KeyRelease(Avalonia.Input.Key.U, modifiers, PhysicalKey.None, null); Pump(); }
        var command = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        Shortcuts.ResetAll();
        Check(Shortcuts.Format("edit.uppercase", false) == "Ctrl+U" && Shortcuts.Format("edit.lowercase", false) == "Ctrl+Shift+U", "Default shortcuts match requested keys");
        Check(Shortcuts.Get("edit.uppercase").IsCustomizable && Shortcuts.Get("edit.lowercase").IsCustomizable && !Shortcuts.Conflicts().Any(), "Both commands are customizable without conflicts");
        Check(Loc.Languages.All(l => Loc.Table(l.Code).ContainsKey("Sc.edit.uppercase") && Loc.Table(l.Code).ContainsKey("Sc.edit.lowercase")), "Both labels exist in all languages");
        var window = new MainWindow();
        try
        {
            window.Show(); Pump();
            var view = window.FindControl<MainView>("MainView")!;
            NoteDocument Open(string name) => (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "", name, null])!;
            var doc = Open("case.md"); Pump();
            void Select(string text, int anchor, int caret)
            {
                doc.Editor.Text = text; Pump(); doc.Editor.Focus();
                doc.Editor.SelectionStart = anchor; doc.Editor.SelectionEnd = caret; Pump();
            }
            Select("before Hello world after", 7, 18); Key(window, command);
            Check(doc.Text == "before HELLO WORLD after" && doc.Editor.SelectedText == "HELLO WORLD", "Uppercase affects only the selection and keeps it selected");
            doc.Editor.Undo(); Pump(); Check(doc.Text == "before Hello world after", "Uppercase is one undo step");
            doc.Editor.Redo(); Pump(); Check(doc.Text == "before HELLO WORLD after", "Uppercase can be redone");
            doc.Editor.SelectionStart = 18; doc.Editor.SelectionEnd = 7; Key(window, command | RawInputModifiers.Shift);
            Check(doc.Text == "before hello world after" && doc.Editor.SelectionStart == 18 && doc.Editor.SelectionEnd == 7, "Lowercase preserves a backwards selection");
            doc.Editor.Undo(); Pump(); Check(doc.Text == "before HELLO WORLD after", "Lowercase is one undo step");
            Select("abc", 1, 1); Key(window, command); Key(window, command | RawInputModifiers.Shift);
            Check(doc.Text == "abc" && doc.Editor.SelectionStart == 1 && doc.Editor.SelectionEnd == 1, "No selection leaves text and caret unchanged");
            Select("été\r\nΑβΓ 中文 123", 0, 15); doc.Editor.SelectAll(); Key(window, command);
            Check(doc.Text == "ÉTÉ\r\nΑΒΓ 中文 123", "Unicode casing preserves line endings and uncased characters");
            Select("Mixed", 0, 5); doc.Editor.IsReadOnly = true; Key(window, command);
            Check(doc.Text == "Mixed", "Read-only text is unchanged"); doc.Editor.IsReadOnly = false;
            doc = Open("case.txt"); Pump(); Select("Mixed", 0, 5); Key(window, command | RawInputModifiers.Shift);
            Check(doc.Text == "mixed", "Case conversion works in plain text files");
            Select("Mixed", 0, 5);
            window.GetVisualDescendants().OfType<FolderExplorer>().Single().FocusTree(); Pump(); Key(window, command);
            Check(doc.Text == "Mixed", "Explorer focus does not change the editor selection");
            var editor = window.GetVisualDescendants().OfType<TextEditor>().Single();
            editor.OpenFind(false); Pump(); Key(window, command);
            Check(doc.Text == "Mixed", "Find field focus does not change the document"); editor.CloseFind();

            _ = DialogService.ShowShortcutsAsync(window); Pump();
            var dialog = window.OwnedWindows.Single();
            foreach (var (id, modifiers) in new[] { ("edit.uppercase", RawInputModifiers.Alt), ("edit.lowercase", RawInputModifiers.Alt | RawInputModifiers.Shift) })
            {
                var row = dialog.GetVisualDescendants().OfType<Grid>().Single(g => g.Tag as string == id);
                Check(row.Children.OfType<TextBlock>().Any(t => t.Text == Shortcuts.Get(id).DisplayLabel), id + " appears in Settings shortcuts");
                row.Children.OfType<Button>().Single(b => b.Name == "ShortcutEdit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Key(dialog, modifiers);
                Check(Shortcuts.IsCustomized(id), id + " can be rebound through the shortcut recorder");
            }
            dialog.Close(); Pump();
            Select("Mixed", 0, 5); Key(window, command);
            Check(doc.Text == "Mixed", "Rebinding removes the old uppercase shortcut");
            Key(window, RawInputModifiers.Alt); Check(doc.Text == "MIXED", "Custom uppercase shortcut takes effect immediately");
            Key(window, RawInputModifiers.Alt | RawInputModifiers.Shift); Check(doc.Text == "mixed", "Custom lowercase shortcut takes effect immediately");
            var session = SessionStore.Deserialize(SessionStore.Serialize(new SessionData { CustomShortcuts = Shortcuts.Export() }));
            Shortcuts.ResetAll(); Shortcuts.Import(session!.CustomShortcuts);
            Check(Shortcuts.Format("edit.uppercase", false) == "Alt+U" && Shortcuts.Format("edit.lowercase", false) == "Alt+Shift+U", "Custom bindings survive session serialization and restore");
            Shortcuts.Reset("edit.uppercase"); Shortcuts.Reset("edit.lowercase");
            Check(Shortcuts.Format("edit.uppercase", false) == "Ctrl+U" && Shortcuts.Format("edit.lowercase", false) == "Ctrl+Shift+U", "Reset restores both defaults");
        }
        finally { window.Close(); Shortcuts.ResetAll(); }
        Console.WriteLine($"{passed} text-case checks passed.");
    }
}
