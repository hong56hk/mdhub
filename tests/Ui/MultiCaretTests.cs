using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

internal static class MultiCaretTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SessionManager.Enabled = false;
        EditorBehavior.Import(null);
        var window = new MainWindow { Width = 1000, Height = 700 };
        var passed = 0;
        void Pump() { using var timeout = new CancellationTokenSource(70); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
        try
        {
            window.Show(); Pump();
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var document = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, "", "multi.md", null])!;
            Pump();
            var editor = (MarkdownTextBox)document.Editor;
            void Set(string text, int index)
            {
                editor.IsReadOnly = false; editor.Text = text; editor.Focus();
                editor.SelectionStart = editor.SelectionEnd = index; editor.CaretIndex = index; Pump();
            }
            void Key(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
            {
                window.KeyPress(key, modifiers, PhysicalKey.None, null); Pump();
            }
            void Down() => Key(Avalonia.Input.Key.Down, RawInputModifiers.Alt | RawInputModifiers.Shift);
            void Up() => Key(Avalonia.Input.Key.Up, RawInputModifiers.Alt | RawInputModifiers.Shift);
            void Type(string text)
            {
                editor.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text }); Pump();
            }
            Set("abcd\r\nefgh\r\nijkl", 1); Down(); Down();
            Check(editor.HasMultipleCarets && editor.CaretIndex == 13, "Alt+Shift+Down adds carets on successive lines at the same column");
            window.KeyRelease(Avalonia.Input.Key.LeftAlt, RawInputModifiers.None, PhysicalKey.None, null); Pump();
            window.KeyRelease(Avalonia.Input.Key.LeftShift, RawInputModifiers.None, PhysicalKey.None, null); Pump();
            Check(editor.HasMultipleCarets, "Releasing Alt+Shift preserves every caret");
            Key(Avalonia.Input.Key.A); Type("X");
            Check(editor.Text == "aXbcd\r\neXfgh\r\niXjkl", "Typing after Alt+Shift is released edits all three lines");
            Type("Y");
            Check(editor.Text == "aXYbcd\r\neXYfgh\r\niXYjkl", "Multiple carets persist for repeated typing");
            Key(Avalonia.Input.Key.Back);
            Check(editor.Text == "aXbcd\r\neXfgh\r\niXjkl", "Backspace edits every caret");
            Key(Avalonia.Input.Key.Delete);
            Check(editor.Text == "aXcd\r\neXgh\r\niXkl", "Delete edits every caret");
            Key(Avalonia.Input.Key.Z, RawInputModifiers.Control);
            Check(editor.Text == "aXbcd\r\neXfgh\r\niXjkl" && !editor.HasMultipleCarets, "One undo reverses the complete edit and clears stale carets");
            Key(Avalonia.Input.Key.Y, RawInputModifiers.Control);
            Check(editor.Text == "aXcd\r\neXgh\r\niXkl", "Redo reapplies the complete edit");

            Set("abcd\nefgh\nijkl", 11); Up(); Up(); Up();
            Check(editor.HasMultipleCarets && editor.CaretIndex == 1, "Alt+Shift+Up adds carets upward and stops at the first line");
            Key(Avalonia.Input.Key.Right, RawInputModifiers.Shift); Key(Avalonia.Input.Key.Right, RawInputModifiers.Shift);
            var screenshots = Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, ".artifacts", "multicaret")).FullName;
            using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(screenshots, "selection.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            Key(Avalonia.Input.Key.C, RawInputModifiers.Control);
            Check(window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() == string.Join(editor.NewLine, "bc", "fg", "jk"), "Shift+Right selects at every caret and copy follows document order");
            Type("!");
            Check(editor.Text == "a!d\ne!h\ni!l", "Typing replaces each keyboard selection");

            Set("abcd\nefgh\nijkl", 3); Down(); Down();
            Key(Avalonia.Input.Key.Left, RawInputModifiers.Shift); Key(Avalonia.Input.Key.Left, RawInputModifiers.Shift);
            Key(Avalonia.Input.Key.X, RawInputModifiers.Control);
            Check(editor.Text == "ad\neh\nil", "Shift+Left and cut remove only each selected range");
            editor.InsertText("1\r\n2\r\n3"); Pump();
            Check(editor.Text == "a1d\ne2h\ni3l", "Multiline paste distributes rows across the carets");
            Set("ab\ncd", 1); Down();
            var clipboardItem = new DataTransferItem(); clipboardItem.SetText("!");
            var clipboardData = new DataTransfer(); clipboardData.Add(clipboardItem);
            window.Clipboard!.SetDataAsync(clipboardData).GetAwaiter().GetResult();
            Key(Avalonia.Input.Key.V, RawInputModifiers.Control);
            Check(editor.Text == "a!b\nc!d", "Ctrl+V routes clipboard text through every caret");

            Set("abcd\nefgh\nijkl", 1); Down(); Up(); Down(); Down(); Down(); Type("!");
            Check(editor.Text == "a!bcd\ne!fgh\ni!jkl", "Revisiting lines and reaching the last line never duplicate carets");
            Set("abcdef\nx\n\nuvwxyz", 4); Down(); Down(); Down(); Type("!");
            Check(editor.Text == "abcd!ef\nx!\n!\nuvwx!yz", "Short and empty lines clamp while longer lines retain the desired column");
            Set("a😀b\nc😀d", 3); Down(); Key(Avalonia.Input.Key.Left, RawInputModifiers.Shift); Type("!");
            Check(editor.Text == "a!b\nc!d", "Horizontal selections preserve Unicode characters");
            Set("ab\r\ncd", 0); Down(); Key(Avalonia.Input.Key.Back);
            Check(editor.Text == "abcd", "Backspace at a line start removes the whole CRLF safely");

            Set("ab\ncd", 1); Down(); Key(Avalonia.Input.Key.Enter); Type("!");
            Check(editor.Text == "a\r\n!b\nc\r\n!d", "Enter uses the document newline at each caret and keeps them active");
            Set("ab\ncd", 1); Down(); Key(Avalonia.Input.Key.Tab);
            Check(editor.Text == "a    b\nc    d", "Tab follows editor indentation settings at every caret");
            Set("ab\ncd", 1); Down(); Key(Avalonia.Input.Key.Left); Type("!");
            Check(editor.Text == "!ab\n!cd", "Plain horizontal movement moves every caret");
            Set("abcd\nefgh\nijkl\nmnop", 1); Down();
            Key(Avalonia.Input.Key.Down); Type("!");
            Check(editor.Text == "abcd\ne!fgh\ni!jkl\nmnop" && editor.HasMultipleCarets, "Down moves every caret while keeping multi-caret editing active");
            Set("abcd\nefgh\nijkl\nmnop", 11); Up();
            Key(Avalonia.Input.Key.Up); Type("!");
            Check(editor.Text == "a!bcd\ne!fgh\nijkl\nmnop" && editor.HasMultipleCarets, "Up moves every caret while keeping multi-caret editing active");
            Set("abcd\nefgh\nijkl\nmnop", 1); Down();
            Key(Avalonia.Input.Key.Down, RawInputModifiers.Shift);
            Key(Avalonia.Input.Key.C, RawInputModifiers.Control);
            Check(editor.HasMultipleCarets && window.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() == string.Join(editor.NewLine, "bcd\ne", "fgh\ni"),
                "Shift+Down extends each selection vertically without leaving multi-caret mode");
            Key(Avalonia.Input.Key.Up, RawInputModifiers.Shift); Type("!");
            Check(editor.Text == "a!bcd\ne!fgh\nijkl\nmnop", $"Shift+Up reverses vertical selection and restores the original anchors ({editor.Text?.Replace("\n", "\\n")})");
            const string words = "one two\nred blue";
            Set(words, 0); Key(Avalonia.Input.Key.Right, RawInputModifiers.Control);
            var firstWordEnd = editor.CaretIndex;
            Set(words, 8); Key(Avalonia.Input.Key.Right, RawInputModifiers.Control);
            var secondWordEnd = editor.CaretIndex;
            Set(words, 0); Down();
            Key(Avalonia.Input.Key.Right, RawInputModifiers.Control); Type("!");
            Check(editor.Text == words.Insert(secondWordEnd, "!").Insert(firstWordEnd, "!"), $"Ctrl+Right moves every caret by a word using normal editor behavior ({editor.Text?.Replace("\n", "\\n")})");
            Set("abcd\nefgh", 1); Down(); Key(Avalonia.Input.Key.Home); Type("!");
            Check(editor.Text == "!abcd\n!efgh", "Home moves every caret to its line start");
            Set("abcd\nefgh", 1); Down(); Key(Avalonia.Input.Key.End); Type("!");
            Check(editor.Text == "abcd!\nefgh!", "End moves every caret to its line end");
            Set("abcdef\nghijkl\nx\nmnopqr\nstuvwx", 4); Down();
            Key(Avalonia.Input.Key.Down); Key(Avalonia.Input.Key.Down); Key(Avalonia.Input.Key.Down); Type("!");
            Check(editor.Text == "abcdef\nghijkl\nx\nmnop!qr\nstuv!wx", "Vertical movement retains each preferred column across short lines");

            Set(string.Join('\n', Enumerable.Repeat("abcd", 200)), 1); Down();
            Key(Avalonia.Input.Key.PageDown);
            Check(editor.HasMultipleCarets && editor.CaretIndex > 50, "PageDown moves every caret by a viewport and retains multi-caret mode");
            Key(Avalonia.Input.Key.PageUp); Type("!");
            Check(editor.Text!.Split('\n').Take(2).All(line => line == "a!bcd") && editor.Text.Count(c => c == '!') == 2,
                "PageUp moves the carets back independently");

            editor.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            var paragraph = string.Join(' ', Enumerable.Repeat("word", 60));
            Set(paragraph + "\n" + paragraph, 1); Down();
            var wrappedRows = editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single().TextLayout.TextLines;
            var nextLogicalRow = wrappedRows.ToList().FindIndex(row => row.FirstTextSourceIndex == paragraph.Length + 1);
            var firstWrappedTarget = wrappedRows[1].FirstTextSourceIndex + 1;
            var secondWrappedTarget = wrappedRows[nextLogicalRow + 1].FirstTextSourceIndex + 1;
            Key(Avalonia.Input.Key.Down); Type("!");
            Check(editor.Text == (paragraph + "\n" + paragraph).Insert(secondWrappedTarget, "!").Insert(firstWrappedTarget, "!"),
                "Down follows wrapped visual rows independently at each caret");
            editor.TextWrapping = Avalonia.Media.TextWrapping.NoWrap;
            Set("ab\ncd", 1); Down(); Key(Avalonia.Input.Key.Home, RawInputModifiers.Shift); Type("!");
            Check(editor.Text == "!b\n!d", "Shift+Home selects to each line start");
            Set("ab\ncd", 1); Down(); Key(Avalonia.Input.Key.End, RawInputModifiers.Shift); Type("!");
            Check(editor.Text == "a!\nc!", "Shift+End selects to each line end");

            Set("aa\nbb", 1);
            Key(Avalonia.Input.Key.Down, RawInputModifiers.Alt);
            Check(editor.Text == "bb\naa" && !editor.HasMultipleCarets, "Alt+Down moves the line instead of adding carets");
            Key(Avalonia.Input.Key.Up, RawInputModifiers.Alt);
            Check(editor.Text == "aa\nbb" && !editor.HasMultipleCarets, "Alt+Up restores the line instead of adding carets");

            Set("ab\ncd", 1); Down(); editor.IsReadOnly = true; Type("!"); Key(Avalonia.Input.Key.Delete); editor.InsertText("!");
            Check(editor.Text == "ab\ncd", "Read-only documents reject every multi-caret edit path");
            editor.IsReadOnly = false; Key(Avalonia.Input.Key.Escape); Type("!");
            Check(editor.Text == "ab\nc!d" && !editor.HasMultipleCarets, "Escape keeps only the active caret");
            Set("ab\ncd", 1); Down();
            var presenter = editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
            var r = presenter.TextLayout.HitTestTextPosition(0);
            var point = presenter.TranslatePoint(new Point(r.X, r.Y + r.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left); Pump();
            Check(!editor.HasMultipleCarets, "An ordinary click returns to single-caret editing");
            Set("ab\ncd", 1); Down(); editor.Text = "replacement"; Pump();
            Check(!editor.HasMultipleCarets, "External text changes clear stale multi-caret ranges");
            Set("abc", 1); Down(); Up(); Type("!");
            Check(editor.Text == "a!bc" && !editor.HasMultipleCarets, "A single-line document remains a normal editor");
            Set("abcd\nefgh", 1); Down();
            for (var i = 0; i < 7; i++) Key(Avalonia.Input.Key.Right, RawInputModifiers.Shift);
            Type("!");
            Check(editor.Text == "a!", "Overlapping keyboard selections merge before replacement");
            Set("ab\ncd", 1); Down();
            EditorMode.Toggle(); Type("!"); EditorMode.Toggle();
            Check(editor.Text == "a!\nc!", "Overwrite mode applies independently to every caret");
            Check(Shortcuts.Format("editor.addCaretAbove", false) == "Alt+Shift+Up" && Shortcuts.Format("editor.addCaretBelow", false) == "Alt+Shift+Down"
                && !Shortcuts.Conflicts().Any(), "Multi-caret shortcuts are listed without conflicts");
            Check(LocKeysTranslated(), "Multi-caret shortcut labels are translated in every language");
        }
        finally { window.Close(); }
        Console.WriteLine($"Multi-caret tests: {passed} passed");
    }

    private static bool LocKeysTranslated() => Mdhub.Localization.Loc.Languages.All(l =>
        new[] { "Sc.editor.addCaretAbove", "Sc.editor.addCaretBelow" }.All(k => Mdhub.Localization.Loc.Table(l.Code).ContainsKey(k)));
}
