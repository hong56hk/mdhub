using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 92: keyboard navigation in the emoji picker.
internal static class EmojiKeysTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SessionManager.Enabled = false;
        Shortcuts.ResetAll(); Shortcuts.ReleaseAll();
        EmojiRecents.Import([]);
        var window = new MainWindow { Width = 1200, Height = 800 };
        void Pump() { using var timeout = new CancellationTokenSource(60); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 4; i++) Pump(); }
        var passed = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
        Flyout? flyout = null;
        try
        {
            window.Show(); Settle();
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            var document = (NoteDocument)typeof(MainView).GetField("_active", Flags)!.GetValue(view)!;
            var anchor = window.GetVisualDescendants().OfType<TextFormatter>().Single().FindControl<Button>("EmojiButton")!;
            var editor = document.Editor;
            editor.Text = "hello world"; editor.Focus(); editor.CaretIndex = 5; Settle();

            EmojiPicker panel = null!; TextBox search = null!; ScrollViewer scroll = null!; TextBlock footer = null!;
            void Open()
            {
                flyout = (Flyout)typeof(MainView).GetMethod("BuildEmojiPicker", Flags)!.Invoke(view, [document])!;
                panel = (EmojiPicker)flyout.Content!;
                search = panel.FindControl<TextBox>("EmojiSearchBox")!;
                scroll = panel.FindControl<ScrollViewer>("EmojiScroll")!;
                footer = panel.FindControl<TextBlock>("EmojiFooter")!;
                flyout.ShowAt(anchor); Settle();
            }
            List<Button> Cells() => (List<Button>)typeof(EmojiPicker).GetField("_cells", Flags)!.GetValue(panel)!;
            List<List<int>> Rows() => (List<List<int>>)typeof(EmojiPicker).GetField("_rows", Flags)!.GetValue(panel)!;
            int H() => (int)typeof(EmojiPicker).GetField("_highlight", Flags)!.GetValue(panel)!;
            (int Row, int Col) Pos() { var rows = Rows(); var h = H(); for (var r = 0; r < rows.Count; r++) { var c = rows[r].IndexOf(h); if (c >= 0) return (r, c); } return (-1, -1); }
            void At(int row, int col) { var b = Cells()[Rows()[row][col]]; b.Focus(); Settle(); }
            IInputElement? Focused() => TopLevel.GetTopLevel(panel)!.FocusManager!.GetFocusedElement();
            void Key(Key key)
            {
                var target = (Interactive)Focused()!;
                target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = target }); Settle();
            }
            void Type(string text) { var target = (Interactive)Focused()!; target.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = target }); Settle(); }
            string Cat(int row) => ((EmojiEntry)Cells()[Rows()[row][0]].Tag!).Category;

            Open();
            Check(flyout!.IsOpen && search.IsFocused && H() == 0 && Cells()[0].Classes.Contains("highlighted"), "opening focuses search and highlights the first result");
            Check(footer.Text == ((EmojiEntry)Cells()[0].Tag!).Name, $"the footer shows the highlighted emoji's name ({footer.Text})");
            var presenter = Cells()[0].GetVisualDescendants().OfType<ContentPresenter>().First();
            object? Res(string key) => window.TryFindResource(key, window.ActualThemeVariant, out var value) ? value : null;
            string? Color(object? brush) => (brush as Avalonia.Media.ISolidColorBrush)?.Color.ToString();
            Check(Color(Res("ExplorerSelectionBorder")) is { } border && Color(presenter.BorderBrush) == border && presenter.BorderThickness.Top == 2
                && Color(presenter.Background) == Color(Res("ExplorerSelectionBackground")), "the highlight draws a 2px border in the item 29 selection colours");

            Key(Avalonia.Input.Key.Down);
            Check(Focused() == Cells()[0], "Down from the search box moves into the grid");
            for (var i = 0; i < 8; i++) Key(Avalonia.Input.Key.Right);
            Check(Pos() == (0, 8), $"Right walks along the row ({Pos()})");
            Key(Avalonia.Input.Key.Right);
            Check(Pos() == (1, 0) && Focused() == Cells()[H()], $"Right at the end of a row wraps to the next row ({Pos()})");
            Key(Avalonia.Input.Key.Left);
            Check(Pos() == (0, 8), $"Left at the start of a row wraps to the previous row ({Pos()})");

            // Category boundaries: rows are per category, the last row of a category may be short.
            var rows = Rows();
            var boundary = Enumerable.Range(0, rows.Count - 1).First(r => Cat(r) != Cat(r + 1) && rows[r].Count < 9);
            Check(Cat(0) == "Smileys" && rows.Take(rows.Count - 1).Where((r, i) => Cat(i) == Cat(i + 1)).All(r => r.Count == 9),
                "in All, each category starts on its own row");
            At(boundary - 1, 8); Key(Avalonia.Input.Key.Down);
            Check(Pos() == (boundary, rows[boundary].Count - 1), $"Down into a short last row clamps the column ({Pos()}, row length {rows[boundary].Count})");
            At(boundary, rows[boundary].Count - 1); Key(Avalonia.Input.Key.Down);
            Check(Pos() == (boundary + 1, rows[boundary].Count - 1) && Cat(boundary + 1) != Cat(boundary), $"Down crosses into the next category, keeping the column ({Pos()})");
            At(boundary + 1, 8); Key(Avalonia.Input.Key.Up);
            Check(Pos() == (boundary, rows[boundary].Count - 1), $"Up crosses back into the previous category, clamped to its last row ({Pos()})");

            Key(Avalonia.Input.Key.End);
            Check(H() == Cells().Count - 1 && scroll.Offset.Y > 0, $"End jumps to the last emoji and scrolls ({scroll.Offset.Y})");
            var last = Cells()[H()];
            var top = last.TranslatePoint(new Point(0, 0), scroll)!.Value.Y;
            Check(top >= -0.5 && top + last.Bounds.Height <= scroll.Viewport.Height + 0.5, $"auto-scroll keeps the highlight visible ({top})");
            Key(Avalonia.Input.Key.Home);
            Check(H() == 0 && scroll.Offset.Y == 0, "Home jumps to the first emoji and scrolls back");
            var page = (int)Math.Floor(scroll.Viewport.Height / Cells()[0].Bounds.Height);
            At(0, 3); Key(Avalonia.Input.Key.PageDown);
            Check(page >= 2 && Pos() == (page, Math.Min(3, rows[page].Count - 1)), $"Page Down moves one visible screen ({page} rows -> {Pos()})");
            Key(Avalonia.Input.Key.PageDown); Key(Avalonia.Input.Key.PageUp);
            Check(Pos() == (page, Math.Min(3, rows[page].Count - 1)), $"Page Up moves back one screen ({Pos()})");
            Key(Avalonia.Input.Key.PageUp);
            Check(Pos() == (0, 3), "Page Up stops at the first row");
            Key(Avalonia.Input.Key.Up);
            Check(search.IsFocused && H() == 3, "Up from the top row returns to the search box");

            // Hover and click stay in sync.
            Cells()[12].RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, Cells()[12], new Avalonia.Input.Pointer(0, PointerType.Mouse, true), panel, default, 0, PointerPointProperties.None, KeyModifiers.None));
            Settle();
            Check(H() == 12 && Cells()[12].Classes.Contains("highlighted") && !Cells()[3].Classes.Contains("highlighted"), "mouse hover moves the highlight");

            // Typing from the grid goes to the search box.
            At(2, 2); Type("cof");
            Check(search.Text == "cof" && search.IsFocused && search.CaretIndex == 3 && H() == 0, "typing in the grid continues in the search box");
            search.Text = ""; Settle();

            // Search then Enter inserts at the caret as one undo step and closes.
            search.Text = "coffee"; Settle();
            var coffee = ((EmojiEntry)Cells()[0].Tag!).Value;
            Key(Avalonia.Input.Key.Enter);
            Check(!flyout.IsOpen && editor.Text == "hello" + coffee + " world" && editor.CaretIndex == 5 + coffee.Length && editor.IsFocused,
                "search then Enter inserts the first result at the caret, closes the picker and focuses the editor");
            editor.Undo(); Settle();
            Check(editor.Text == "hello world", "the Enter insertion is one undo step");
            Check(EmojiRecents.Values.FirstOrDefault() == coffee, "Enter records the emoji in Recent");

            // Enter from the grid inserts the highlighted emoji.
            editor.CaretIndex = 0; Settle();
            Open(); Key(Avalonia.Input.Key.Down); Key(Avalonia.Input.Key.Right); Key(Avalonia.Input.Key.Right);
            var third = ((EmojiEntry)Cells()[H()].Tag!).Value;
            Key(Avalonia.Input.Key.Enter);
            Check(!flyout.IsOpen && editor.Text == third + "hello world" && editor.IsFocused, "Enter in the grid inserts the highlighted emoji once and closes");
            editor.Undo(); Settle();

            // Esc closes without inserting and returns focus and caret.
            editor.Focus(); editor.CaretIndex = 7; Settle();
            Open(); Key(Avalonia.Input.Key.Down); Key(Avalonia.Input.Key.Right); Key(Avalonia.Input.Key.Escape);
            Check(!flyout.IsOpen && editor.Text == "hello world" && editor.IsFocused && editor.CaretIndex == 7, $"Esc closes without inserting and restores editor focus and caret ({editor.CaretIndex})");

            // Mouse click keeps working (inserts, keeps the picker open) and syncs the highlight.
            Open();
            Cells()[4].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
            Check(flyout.IsOpen && editor.Text!.Length > "hello world".Length, "clicking an emoji still inserts and keeps the picker open");
            flyout.Hide(); Settle();
            Console.WriteLine($"Emoji keyboard tests: {passed} passed");
        }
        finally
        {
            flyout?.Hide();
            var view = window.GetVisualDescendants().OfType<MainView>().FirstOrDefault();
            if (view is not null) { typeof(MainView).GetField("_allowClose", Flags)!.SetValue(view, true); view.StopPreview(); }
            window.Close();
        }
    }
}