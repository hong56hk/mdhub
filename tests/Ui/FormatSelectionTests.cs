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
using Mdhub.Models;
using Mdhub.Views;

// Item 84: format-bar buttons act on the selection (VS Code / Typora style): line-level list/ordered/quote, inline
// bold/italic/strike/code, link; toggling, indentation, blank lines, line endings, one undo step, shortcuts share the path.
internal static class FormatSelectionTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        var misses = new List<string>();
        void Check(bool ok, string name) { if (ok) { passed++; Console.WriteLine("PASS " + name); } else { misses.Add(name); Console.WriteLine("MISS " + name); } }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 3; i++) Pump(); }
        string Show(string? s) => (s ?? "<null>").Replace("\r", "\\r").Replace("\n", "\\n");
        var w = new MainWindow { Width = 1300, Height = 700 };
        try
        {
            w.Show(); Settle();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var bar = w.GetVisualDescendants().OfType<TextFormatter>().Single();
            var buttons = bar.FindControl<StackPanel>("FormatBar")!.Children.OfType<Button>().ToList();
            var doc = (NoteDocument)typeof(MainView).GetMethod("AddDocument", Flags)!.Invoke(v, [null, "", "sel.md", null])!;
            Settle();
            var ed = doc.Editor;
            void Click(string tag) { buttons.Single(b => b.Tag as string == tag).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); }
            void Prep(string text, int s, int e) { ed.Text = text; ed.Focus(); ed.SelectionStart = s; ed.SelectionEnd = e; Settle(); }
            int At(string text, string marker) => text.IndexOf(marker, StringComparison.Ordinal);

            // One case: select [s, e), click, compare the text and the selected text, then one undo restores the original.
            void Case(string label, string tag, string text, int s, int e, string expected, string? selected = null)
            {
                Prep(text, s, e);
                Click(tag);
                var got = ed.Text; var sel = ed.SelectedText;
                Check(got == expected && (selected is null || sel == selected), $"{tag}: {label}: '{Show(got)}' selected '{Show(sel)}'" + (got == expected ? "" : $" (expected '{Show(expected)}')") + (selected is null || sel == selected ? "" : $" (expected selection '{Show(selected)}')"));
                ed.Undo(); Settle();
                Check(ed.Text == text, $"{tag}: {label}: one undo restores the original ('{Show(ed.Text)}')");
            }
            // Select everything of [from, to) markers inclusive.
            (int, int) Span(string text, string from, string to) => (At(text, from), At(text, to) + to.Length);

            // ---- Bullet list ----
            Case("single line", "list", "alpha", 0, 3, "- alpha", "- alpha");
            var three = "one\ntwo\nthree";
            Case("partial lines (o of one .. tw of two)", "list", three, 2, 6, "- one\n- two\nthree", "- one\n- two");
            Case("all lines", "list", three, 0, three.Length, "- one\n- two\n- three");
            Case("toggle off", "list", "- one\n- two", 0, 11, "one\ntwo", "one\ntwo");
            Case("mixed lines: adds to the ones without", "list", "- one\ntwo", 0, 9, "- one\n- two");
            Case("keeps indentation", "list", "  one\n    two", 0, 13, "  - one\n    - two");
            Case("indented toggle off", "list", "  - one\n    - two", 0, 17, "  one\n    two");
            Case("blank lines get no marker", "list", "one\n\n  \ntwo", 0, 10, "- one\n\n  \n- two");
            Case("blank lines do not block the toggle", "list", "- one\n\n- two", 0, 12, "one\n\ntwo");
            Case("replaces ordered markers", "list", "1. one\n2. two", 0, 13, "- one\n- two");
            Case("selection ending at the next line start leaves it out", "list", three, 0, 4, "- one\ntwo\nthree");
            Case("CRLF and mixed endings are preserved", "list", "one\r\ntwo\nthree\rfour", 0, 19, "- one\r\n- two\n- three\r- four");

            // ---- Ordered list ----
            Case("single line", "ordered", "alpha", 1, 2, "1. alpha");
            Case("numbers lines", "ordered", three, 0, three.Length, "1. one\n2. two\n3. three", "1. one\n2. two\n3. three");
            Case("toggle off", "ordered", "1. one\n2. two\n3. three", 0, 21, "one\ntwo\nthree");
            Case("replaces bullets and renumbers", "ordered", "- one\n* two\n5. three", 0, 19, "1. one\n2. two\n3. three");
            Case("keeps indentation", "ordered", "  one\n  two", 0, 11, "  1. one\n  2. two");
            Case("blank lines get no number", "ordered", "one\n\ntwo", 0, 8, "1. one\n\n2. two");
            Case("CRLF preserved", "ordered", "one\r\ntwo", 0, 8, "1. one\r\n2. two");

            // ---- Quote ----
            Case("single line", "quote", "alpha", 0, 5, "> alpha");
            Case("multiple lines", "quote", three, 1, 9, "> one\n> two\n> three");
            Case("toggle off", "quote", "> one\n> two", 0, 11, "one\ntwo");
            Case("keeps indentation", "quote", "  one\n\ttwo", 0, 9, "  > one\n\t> two");
            Case("blank lines get no marker", "quote", "one\n\ntwo", 0, 8, "> one\n\n> two");
            Case("quotes a list without touching its markers", "quote", "- one\n- two", 0, 11, "> - one\n> - two");
            Case("CRLF preserved", "quote", "one\r\ntwo", 0, 8, "> one\r\n> two");

            // ---- Inline: bold, italic, strike, code ----
            foreach (var (tag, d) in new[] { ("bold", "**"), ("italic", "*"), ("strike", "~~") })
            {
                var t = "say hello world";
                Case("single line wraps the selection", tag, t, 4, 9, $"say {d}hello{d} world", "hello");
                Case("toggle off, delimiters inside the selection", tag, $"say {d}hello{d} world", 4, 9 + 2 * d.Length, "say hello world", "hello");
                Case("toggle off, delimiters right around the selection", tag, $"say {d}hello{d} world", 4 + d.Length, 9 + d.Length, "say hello world", "hello");
                Case("surrounding whitespace stays outside", tag, t, 3, 10, $"say {d}hello{d} world", "hello");
                Case("multiple lines wrap each line's content", tag, "one\n  two\n\nthree", 0, 16, $"{d}one{d}\n  {d}two{d}\n\n{d}three{d}");
                Case("multiple lines toggle off", tag, $"{d}one{d}\n  {d}two{d}\n\n{d}three{d}", 0, 16 + 6 * d.Length, "one\n  two\n\nthree");
                Case("multiple lines keep CRLF", tag, "one\r\ntwo", 0, 8, $"{d}one{d}\r\n{d}two{d}");
            }
            Case("italic on bold text adds italic", "italic", "**hi**", 0, 6, "***hi***", "**hi**"); // the selection keeps covering what was selected
            Case("bold off on bold italic keeps italic", "bold", "***hi***", 0, 8, "*hi*", "*hi*");
            Case("italic off on bold italic keeps bold", "italic", "***hi***", 0, 8, "**hi**", "**hi**");
            Case("single line wraps the selection", "code", "run npm install now", 4, 15, "run `npm install` now", "npm install");
            Case("toggle off inside", "code", "run `npm` now", 4, 9, "run npm now", "npm");
            Case("toggle off around", "code", "run `npm` now", 5, 8, "run npm now", "npm");
            Case("a backtick in the selection uses a longer fence", "code", "use a`b here", 4, 7, "use ``a`b`` here", "a`b");
            Case("a selection starting with a backtick is padded", "code", "x `y z", 2, 4, "x `` `y `` z", "`y");
            Case("double fence toggles off", "code", "use ``a`b`` here", 4, 11, "use a`b here", "a`b");
            Case("multiple lines wrap each line", "code", "a\n  b\n\nc", 0, 8, "`a`\n  `b`\n\n`c`");

            // ---- Link ----
            Case("text becomes [text](url) with url selected", "link", "see docs here", 4, 8, "see [docs](url) here", "url");
            Prep("see docs here", 4, 8); Click("link");
            Check(ed.SelectionStart == At(ed.Text!, "url") && ed.SelectionEnd == At(ed.Text!, "url") + 3, "link: 'url' is the selection (ready to type over)");
            Case("a URL becomes [](url) with the caret inside []", "link", "go https://example.org/x now", 3, 25, "go [](https://example.org/x) now", "");
            Prep("go https://example.org/x now", 3, 25); Click("link");
            Check(ed.SelectionStart == 4 && ed.SelectionEnd == 4, $"link: caret inside [] ({ed.SelectionStart}-{ed.SelectionEnd})");
            Case("www. counts as a URL", "link", "www.example.com", 0, 15, "[](www.example.com)");

            // ---- No selection: unchanged behaviour ----
            Case("no selection: bold inserts **text**", "bold", "ab", 1, 1, "a**text**b", "text");
            Case("no selection: list prefixes the caret line", "list", "ab\ncd", 4, 4, "ab\n- cd");
            Case("no selection: link inserts the placeholder", "link", "", 0, 0, "[link text](https://example.com)", "https://example.com");
            Case("headings still prefix the selected lines", "h2", "a\nb", 0, 3, "## a\n## b");

            // ---- Shortcuts share the path ----
            void Key2(Key key) { w.KeyPress(key, RawInputModifiers.Control, PhysicalKey.None, null); w.KeyRelease(key, RawInputModifiers.Control, PhysicalKey.None, null); Settle(); }
            Prep("say hello world", 4, 9); Key2(Key.B);
            Check(ed.Text == "say **hello** world" && ed.SelectedText == "hello", $"Ctrl+B wraps the selection like the button ('{Show(ed.Text)}')");
            Key2(Key.B);
            Check(ed.Text == "say hello world" && ed.SelectedText == "hello", $"Ctrl+B again toggles it off ('{Show(ed.Text)}')");
            Prep("say hello world", 4, 9); Key2(Key.I);
            Check(ed.Text == "say *hello* world", $"Ctrl+I wraps the selection ('{Show(ed.Text)}')");
            Prep("one\ntwo", 0, 7); w.KeyPress(Key.OemQuotes, RawInputModifiers.Control, PhysicalKey.None, null); w.KeyRelease(Key.OemQuotes, RawInputModifiers.Control, PhysicalKey.None, null); Settle();
            Check(ed.Text == "> one\n> two", $"Ctrl+' quotes every selected line ('{Show(ed.Text)}')");

            // ---- Undo: one step per click, toggling twice is two steps ----
            Prep("one\ntwo", 0, 7); Click("list"); Click("list");
            Check(ed.Text == "one\ntwo", "list twice toggles back");
            ed.Undo(); Settle(); Check(ed.Text == "- one\n- two", $"first undo returns to the bulleted text ('{Show(ed.Text)}')");
            ed.Undo(); Settle(); Check(ed.Text == "one\ntwo", $"second undo returns to the original ('{Show(ed.Text)}')");

            // ---- Only in .md files (item 65 dims the bar) ----
            var txt = (NoteDocument)typeof(MainView).GetMethod("AddDocument", Flags)!.Invoke(v, [null, "plain text", "plain.txt", null])!; Settle();
            txt.Editor.Focus(); txt.Editor.SelectionStart = 0; txt.Editor.SelectionEnd = 5; Settle();
            Check(!buttons.Single(b => b.Tag as string == "bold").IsEffectivelyEnabled, "a .txt tab dims the format bar (item 65)");
            Key2(Key.B);
            Check(txt.Editor.Text == "plain text", "Ctrl+B in a .txt tab changes nothing");
            if (misses.Count > 0) throw new Exception($"{misses.Count} format selection checks failed: {string.Join(" | ", misses)}");
            Console.WriteLine($"Format selection tests: {passed} passed");
        }
        finally { try { w.Close(); Pump(); } catch { } }
    }
}