using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Text;

// Item 33: Markdown source highlighting: tokenizer per construct, rendered colours/fonts, blocks, speed, themes.
internal static class SyntaxTests
{
    public static void Run()
    {
        Tokenizer();
        Ui();
    }

    private static SyntaxSpan? At(SyntaxResult r, int index) => r.Spans.Where(s => s.Start <= index && index < s.End).Select(s => (SyntaxSpan?)s).FirstOrDefault();
    private static SyntaxColor C(SyntaxResult r, string text, string find, int skip = 0, int occurrence = 0)
    {
        var i = -1; for (var k = 0; k <= occurrence; k++) i = text.IndexOf(find, i + 1, StringComparison.Ordinal);
        if (i < 0) throw new Exception("test text lacks " + find);
        return At(r, i + skip)?.Color ?? SyntaxColor.Default;
    }
    private static SyntaxFont F(SyntaxResult r, string text, string find, int skip = 0)
    {
        var i = text.IndexOf(find, StringComparison.Ordinal);
        return At(r, i + skip)?.Font ?? SyntaxFont.None;
    }

    private static void Tokenizer()
    {
        // Headers: whole line coloured and bold, '#' dimmed.
        var t = "# One\n## Two\n### Three\n#### Four\n##### Five\n###### Six\n#hashtag\n";
        var r = MarkdownHighlighter.Highlight(t);
        Check(C(r, t, "One") == SyntaxColor.H1 && C(r, t, "Two") == SyntaxColor.H2 && C(r, t, "Three") == SyntaxColor.H3
            && C(r, t, "Four") == SyntaxColor.H4 && C(r, t, "Five") == SyntaxColor.H4 && C(r, t, "Six") == SyntaxColor.H4, "H1-H6 text colours (H4-H6 share one)");
        Check(F(r, t, "One").HasFlag(SyntaxFont.Bold) && F(r, t, "Six").HasFlag(SyntaxFont.Bold), "Heading text is bold");
        Check(C(r, t, "# One") == SyntaxColor.Marker && C(r, t, "###### Six", 5) == SyntaxColor.Marker && C(r, t, "## Two", 2) == SyntaxColor.H2, "Heading '#' marks are dimmed, the space after them is heading text");
        Check(C(r, t, "#hashtag") == SyntaxColor.Default, "#hashtag without a space is not a heading");
        var hc = "## Title with `code` and **bold** ##\n";
        r = MarkdownHighlighter.Highlight(hc);
        Check(C(r, hc, "code") == SyntaxColor.InlineCode && C(r, hc, "bold") == SyntaxColor.H2 && C(r, hc, "##", 0, 1) == SyntaxColor.Marker, "Inline constructs inside a heading; closing #s dimmed");

        // Emphasis and markers.
        t = "a **bold** b __strong__ c *it* d _em_ e ~~gone~~ f ***both*** g `x` h\n";
        r = MarkdownHighlighter.Highlight(t);
        Check(new[] { "**bold", "__strong", "*it", "_em", "~~gone", "`x" }.All(m => C(r, t, m) == SyntaxColor.Marker)
            && C(r, t, "bold**", 4) == SyntaxColor.Marker && C(r, t, "gone~~", 4) == SyntaxColor.Marker, "** __ * _ ~~ ` markers are dimmed");
        Check(F(r, t, "bold") == SyntaxFont.Bold && F(r, t, "strong") == SyntaxFont.Bold && F(r, t, "it*") == SyntaxFont.Italic && F(r, t, "em_") == SyntaxFont.Italic
            && F(r, t, "gone") == SyntaxFont.Strike && F(r, t, "both") == (SyntaxFont.Bold | SyntaxFont.Italic), "Bold is bold, italic is italic, strikethrough is struck, *** is both");
        Check(C(r, t, "bold") == SyntaxColor.Bold && C(r, t, "strong") == SyntaxColor.Bold && C(r, t, "it*") == SyntaxColor.Italic && C(r, t, "em_") == SyntaxColor.Italic
            && C(r, t, "gone") == SyntaxColor.Strike && C(r, t, "both") == SyntaxColor.Bold && C(r, t, " b ") == SyntaxColor.Default, "Item 44: emphasis uses Syntax Bold/Italic/Strike colours; plain text is untouched");
        var em = "[**lb** and *li* and ~~ls~~](u) - [x] done **cb**\n### H **hb** *hi* ~~hs~~\n**b ~~bs~~ *bi***\n";
        r = MarkdownHighlighter.Highlight(em);
        Check(C(r, em, "lb") == SyntaxColor.Link && C(r, em, "li") == SyntaxColor.Link && C(r, em, "ls") == SyntaxColor.Link && F(r, em, "ls") == SyntaxFont.Strike, "Item 44: inside links the link colour wins (strike still struck)");
        Check(C(r, em, "hb") == SyntaxColor.H3 && C(r, em, "hi") == SyntaxColor.H3 && C(r, em, "hs") == SyntaxColor.H3 && F(r, em, "hs").HasFlag(SyntaxFont.Strike), "Item 44: inside headings the heading colour wins");
        Check(C(r, em, "bs") == SyntaxColor.Strike && F(r, em, "bs") == (SyntaxFont.Bold | SyntaxFont.Strike) && C(r, em, "bi") == SyntaxColor.Bold, "Item 44: nested emphasis: strike over bold, bold over italic");
        Check(C(r, em, "**hb", 0) == SyntaxColor.Marker && C(r, em, "~~bs") == SyntaxColor.Marker && C(r, em, "**b ") == SyntaxColor.Marker, "Item 44: emphasis markers stay Marker colour");
        var nested = "**bold *and italic* text**\n";
        r = MarkdownHighlighter.Highlight(nested);
        Check(F(r, nested, "and") == (SyntaxFont.Bold | SyntaxFont.Italic) && F(r, nested, "text") == SyntaxFont.Bold, "Nested emphasis combines fonts");
        var noEm = "snake_case_name 2 * 3 * 4 a*b `**not bold**` \\*escaped\\*\n";
        r = MarkdownHighlighter.Highlight(noEm);
        Check(F(r, noEm, "case") == SyntaxFont.None && F(r, noEm, "3") == SyntaxFont.None && F(r, noEm, "not bold") == SyntaxFont.None
            && C(r, noEm, "not bold") == SyntaxColor.InlineCode && F(r, noEm, "escaped") == SyntaxFont.None && C(r, noEm, "\\*") == SyntaxColor.Marker,
            "No false emphasis: intraword _, spaced *, inside code, escaped");

        // Inline code.
        t = "Use `var x = 1;` or ``a ` b`` here\n";
        r = MarkdownHighlighter.Highlight(t);
        Check(C(r, t, "var") == SyntaxColor.InlineCode && At(r, t.IndexOf("var"))!.Value.CodeBackground && C(r, t, "a ` b", 2) == SyntaxColor.InlineCode
            && !(At(r, t.IndexOf('`'))?.CodeBackground ?? false), "Inline code is coloured on the code background; backticks are dimmed without it");

        // Links.
        t = "See [the docs](https://example.com/a_(b)) and ![logo](img.png) or <https://x.dev>.\n";
        r = MarkdownHighlighter.Highlight(t);
        Check(C(r, t, "the docs") == SyntaxColor.Link && C(r, t, "logo") == SyntaxColor.Link && C(r, t, "https://example") == SyntaxColor.Url && C(r, t, "img.png") == SyntaxColor.Url
            && C(r, t, "[the") == SyntaxColor.Marker && C(r, t, "](https") == SyntaxColor.Marker && C(r, t, "](https", 1) == SyntaxColor.Marker && C(r, t, ")) and", 1) == SyntaxColor.Marker
            && C(r, t, "![") == SyntaxColor.Marker && C(r, t, "https://x.dev") == SyntaxColor.Link, "Link text blue, URL dimmed, [ ] ( ) and ! dimmed; autolinks");
        Check(C(r, t, "_(b)", 2) == SyntaxColor.Url, "Balanced parentheses stay inside the URL");

        // Lists, quotes, tasks.
        t = "- bullet\n* star\n+ plus\n1. one\n12) twelve\n  - nested\n> quoted **text**\n> > deeper\n- [ ] todo\n- [x] done item\n-not a list\n";
        r = MarkdownHighlighter.Highlight(t);
        Check(new[] { "- bullet", "* star", "+ plus", "- nested" }.All(m => C(r, t, m) == SyntaxColor.ListMarker) && new[] { "1.", "12)" }.All(m => C(r, t, m) == SyntaxColor.OrderedMarker) && C(r, t, "12)", 2) == SyntaxColor.OrderedMarker
            && C(r, t, "bullet") == SyntaxColor.Default && C(r, t, "-not") == SyntaxColor.Default, "Bullets use the list colour, numbers the ordered-list colour (item 62)");
        Check(C(r, t, "> quoted") == SyntaxColor.Marker && C(r, t, "quoted") == SyntaxColor.Quote && C(r, t, "text**") == SyntaxColor.Quote && F(r, t, "text**") == SyntaxFont.Bold
            && C(r, t, "> deeper", 0) == SyntaxColor.Marker && C(r, t, "deeper") == SyntaxColor.Quote, "Quote markers dimmed, quote text grey (inline styles kept)");
        Check(r.Blocks.Count(b => b.Kind == SyntaxBlockKind.Quote) == 1 && r.Blocks.First(b => b.Kind == SyntaxBlockKind.Quote) is var qb
            && t[qb.Start..qb.End] == "> quoted **text**\n> > deeper", "Consecutive quote lines form one quote-bar block");
        Check(C(r, t, "[ ]") == SyntaxColor.Checkbox && C(r, t, "[x]") == SyntaxColor.Checkbox && C(r, t, "todo") == SyntaxColor.Default
            && C(r, t, "done item") == SyntaxColor.Checked && F(r, t, "done item") == SyntaxFont.Strike, "Checkboxes coloured; checked items struck and dimmed");

        // Tables, rules, front matter.
        t = "---\ntitle: Note\ntags: [a]\n---\n\n| A | B |\n|---|:-:|\n| 1 | **2** |\n\nx | y\n--|--\n1 | 2\n\n---\n***\n- - -\n";
        r = MarkdownHighlighter.Highlight(t);
        Check(C(r, t, "title") == SyntaxColor.FrontMatter && C(r, t, "---") == SyntaxColor.FrontMatter && C(r, t, "---", 0, 1) == SyntaxColor.FrontMatter, "YAML front matter is dimmed");
        Check(C(r, t, "| A") == SyntaxColor.Marker && C(r, t, " A ", 1) == SyntaxColor.Default && C(r, t, "|---|") == SyntaxColor.Marker && C(r, t, ":-:") == SyntaxColor.Marker
            && F(r, t, "2**") == SyntaxFont.Bold && C(r, t, "x | y", 2) == SyntaxColor.Marker && C(r, t, "1 | 2", 2) == SyntaxColor.Marker, "Table pipes and delimiter rows dimmed, cells keep inline styles");
        var rules = t.IndexOf("\n---\n***", StringComparison.Ordinal) + 1;
        Check(At(r, rules)?.Color == SyntaxColor.Rule && At(r, rules + 4)?.Color == SyntaxColor.Rule && At(r, rules + 8)?.Color == SyntaxColor.Rule, "--- *** - - - rules are faint");
        var notFront = "Text\n---\n";
        r = MarkdownHighlighter.Highlight(notFront);
        Check(C(r, notFront, "Text") == SyntaxColor.Default, "--- after the first line is not front matter");

        // Fenced code with languages.
        string Code(string lang, string code) => $"```{lang}\n{code}\n```\n";
        (SyntaxResult, string) H(string lang, string code) { var s = Code(lang, code); return (MarkdownHighlighter.Highlight(s), s); }
        var (cr, cs) = H("csharp", "public class A { string s = \"hi\"; int n = 42; } // note");
        Check(C(cr, cs, "public") == SyntaxColor.CodeKeyword && C(cr, cs, "\"hi\"") == SyntaxColor.CodeString && C(cr, cs, "42") == SyntaxColor.CodeNumber
            && C(cr, cs, "// note") == SyntaxColor.CodeComment && C(cr, cs, "string") == SyntaxColor.CodeType && C(cr, cs, " A ", 1) == SyntaxColor.CodeText, "C# keywords, types, strings, numbers, comments");
        Check(C(cr, cs, "```csharp") == SyntaxColor.Marker && cr.Blocks.Single().Kind == SyntaxBlockKind.Code && cr.Blocks.Single().Start == 0 && cr.Blocks.Single().End == cs.Length - 1,
            "The fence lines are dimmed and the block covers fence to fence");
        (cr, cs) = H("ts", "const x: number = 1; /* multi\nline */ let y = `t`;");
        Check(C(cr, cs, "const") == SyntaxColor.CodeKeyword && C(cr, cs, "number") == SyntaxColor.CodeType && C(cr, cs, "multi") == SyntaxColor.CodeComment
            && C(cr, cs, "line */") == SyntaxColor.CodeComment && C(cr, cs, "let") == SyntaxColor.CodeKeyword && C(cr, cs, "`t`") == SyntaxColor.CodeString, "JS/TS, including a block comment across lines");
        (cr, cs) = H("json", "{ \"key\": \"value\", \"n\": 3, \"ok\": true }");
        Check(C(cr, cs, "\"key\"") == SyntaxColor.CodeProperty && C(cr, cs, "\"value\"") == SyntaxColor.CodeString && C(cr, cs, "3") == SyntaxColor.CodeNumber && C(cr, cs, "true") == SyntaxColor.CodeKeyword, "JSON keys, strings, numbers, literals");
        (cr, cs) = H("python", "def f(x):  # c\n    return \"s\" if x else None");
        Check(C(cr, cs, "def") == SyntaxColor.CodeKeyword && C(cr, cs, "# c") == SyntaxColor.CodeComment && C(cr, cs, "None") == SyntaxColor.CodeKeyword && C(cr, cs, "\"s\"") == SyntaxColor.CodeString, "Python");
        (cr, cs) = H("sql", "SELECT name FROM users WHERE id = 1 -- c");
        Check(C(cr, cs, "SELECT") == SyntaxColor.CodeKeyword && C(cr, cs, "FROM") == SyntaxColor.CodeKeyword && C(cr, cs, "name") == SyntaxColor.CodeText && C(cr, cs, "-- c") == SyntaxColor.CodeComment, "SQL (case-insensitive keywords)");
        (cr, cs) = H("bash", "if [ -f \"$HOME/x\" ]; then echo $USER; fi # done");
        Check(C(cr, cs, "if") == SyntaxColor.CodeKeyword && C(cr, cs, "$USER") == SyntaxColor.CodeProperty && C(cr, cs, "\"$HOME") == SyntaxColor.CodeString && C(cr, cs, "# done") == SyntaxColor.CodeComment, "bash");
        (cr, cs) = H("powershell", "$path = Join-Path $env:TEMP 'x' # c");
        Check(C(cr, cs, "$path") == SyntaxColor.CodeProperty && C(cr, cs, "Join-Path") == SyntaxColor.CodeType && C(cr, cs, "$env:TEMP") == SyntaxColor.CodeProperty && C(cr, cs, "'x'") == SyntaxColor.CodeString, "PowerShell");
        (cr, cs) = H("html", "<div class=\"a\">Hi &amp; bye<!-- c --></div>");
        Check(C(cr, cs, "div") == SyntaxColor.CodeTag && C(cr, cs, "class") == SyntaxColor.CodeAttribute && C(cr, cs, "\"a\"") == SyntaxColor.CodeString
            && C(cr, cs, "<!--") == SyntaxColor.CodeComment && C(cr, cs, "Hi") == SyntaxColor.CodeText && C(cr, cs, "&amp;") == SyntaxColor.CodeKeyword, "XML/HTML tags, attributes, strings, comments, entities");
        (cr, cs) = H("css", ".btn {\n  color: red; /* c */\n}");
        Check(C(cr, cs, ".btn") == SyntaxColor.CodeTag && C(cr, cs, "color") == SyntaxColor.CodeProperty && C(cr, cs, "/* c */") == SyntaxColor.CodeComment, "CSS selectors, properties, comments");
        (cr, cs) = H("yaml", "name: mdhub # c\nitems:\n  - key: 'v'\nn: 3");
        Check(C(cr, cs, "name") == SyntaxColor.CodeProperty && C(cr, cs, "key") == SyntaxColor.CodeProperty && C(cr, cs, "'v'") == SyntaxColor.CodeString && C(cr, cs, "# c") == SyntaxColor.CodeComment && C(cr, cs, "3") == SyntaxColor.CodeNumber, "YAML keys, strings, comments, numbers");
        (cr, cs) = H("rust", "fn main() { let x = \"s\"; }");
        Check(cr.Spans.Where(s => s.Start > 7 && s.Start < cs.Length - 4).All(s => s.Color == SyntaxColor.CodeText) && cr.Blocks.Single().Kind == SyntaxBlockKind.Code, "Other languages stay plain (still on the code background)");
        (cr, cs) = H("", "# not a heading\n- not a list **x**");
        Check(C(cr, cs, "not a heading") == SyntaxColor.CodeText && F(cr, cs, "x**") == SyntaxFont.None, "No Markdown inside a fenced block");
        var tilde = "~~~py\nx = 1\n~~~\n# after\n";
        r = MarkdownHighlighter.Highlight(tilde);
        Check(C(r, tilde, "x =") == SyntaxColor.CodeText && C(r, tilde, "1") == SyntaxColor.CodeNumber && C(r, tilde, "after") == SyntaxColor.H1, "~~~ fences, and Markdown resumes after the closing fence");
        var open = "```js\nlet a = 1;\n";
        r = MarkdownHighlighter.Highlight(open);
        Check(C(r, open, "let") == SyntaxColor.CodeKeyword, "An unclosed fence runs to the end");
        var crlf = "# Head\r\n- item\r\n";
        r = MarkdownHighlighter.Highlight(crlf);
        Check(C(r, crlf, "Head") == SyntaxColor.H1 && At(r, crlf.IndexOf('\r')) is null && C(r, crlf, "- item") == SyntaxColor.ListMarker, "CRLF line endings");

        // Speed: memoized per line, so an edit only re-tokenizes the changed line.
        var big = string.Concat(Enumerable.Range(0, 4000).Select(i => $"## Section {i}\n\nSome **bold** and *italic* text with `code` and a [link](https://x.y/{i}).\n- item {i}\n```cs\nvar x{i} = \"{i}\"; // c\n```\n\n"));
        var hl = new MarkdownHighlighter();
        var sw = Stopwatch.StartNew(); hl.Run(big); var cold = sw.Elapsed.TotalMilliseconds;
        var before = hl.TokenizedLines;
        var edited = big.Insert(big.IndexOf("Some **bold**", big.Length / 2, StringComparison.Ordinal), "x");
        sw.Restart(); var er = hl.Run(edited); var warm = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"  tokenizer: {big.Split('\n').Length} lines cold {cold:F0} ms, after a one-character edit {warm:F0} ms, re-tokenized {hl.TokenizedLines - before} line(s)");
        Check(hl.TokenizedLines - before == 1 && warm < 150, "A one-character edit in a 32,000-line note re-tokenizes one line, quickly");
        Check(er.Spans.Count > 30000, "Large notes are fully highlighted");
    }

    private static void Ui()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        ThemeService.Apply(AppTheme.Dark, persist: false);
        var window = new MainWindow { Width = 1100, Height = 700 }; window.Show();
        void Layout() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        Layout();
        var tabs = window.GetVisualDescendants().OfType<ListBox>().Single(x => x.Name == "Tabs");
        var document = (NoteDocument)tabs.SelectedItem!;
        var editor = (MarkdownTextBox)document.Editor;
        const string text = "# Heading\n\n**bold** tail\n*it* tail\n~~gone~~\n- item `code`\n> quote\n\n```cs\npublic int X;\n```\n[link](u)\n";
        editor.Text = text;
        document.Refresh(); Layout();
        var presenter = editor.GetVisualDescendants().OfType<MarkdownTextPresenter>().Single();
        TextRunProperties? RunAt(int index)
        {
            var line = presenter.TextLayout.TextLines[presenter.TextLayout.GetLineIndexFromCharacterIndex(index, false)];
            var pos = line.FirstTextSourceIndex;
            foreach (var run in line.TextRuns) { if (index >= pos && index < pos + run.Length) return run.Properties; pos += run.Length; }
            return null;
        }
        Color? Fg(int index) => (RunAt(index)?.ForegroundBrush as ISolidColorBrush)?.Color;
        Color L(string key) => ThemeService.Lookup(key);
        int I(string s, int skip = 0) => text.IndexOf(s, StringComparison.Ordinal) + skip;
        bool Colours() =>
            Fg(I("Heading")) == L("SyntaxH1") && Fg(0) == L("SyntaxMarker") && Fg(I("**bold")) == L("SyntaxMarker") && Fg(I("- item")) == L("SyntaxListMarker")
            && Fg(I("code`")) == L("SyntaxInlineCode") && (RunAt(I("code`"))?.BackgroundBrush as ISolidColorBrush)?.Color == L("SyntaxInlineCodeBackground")
            && Fg(I("quote")) == L("SyntaxQuote") && Fg(I("public")) == L("SyntaxCodeKeyword") && Fg(I("link")) == L("SyntaxLink") && Fg(I("u)")) == L("SyntaxUrl");
        Check(Colours(), "Dark: each construct reaches the rendered runs in its theme colour");
        Check(ThemeService.Lookup("SyntaxBold", AppTheme.Dark) == Color.Parse("#FFFFFF") && ThemeService.Lookup("SyntaxItalic", AppTheme.Dark) == Color.Parse("#E0E0E0")
            && ThemeService.Lookup("SyntaxStrike", AppTheme.Dark) == Color.Parse("#C8C8C8") && ThemeService.Lookup("SyntaxMarker", AppTheme.Dark) == Color.Parse("#6A6A6A"), "Item 44: Dark emphasis colours #FFFFFF/#E0E0E0/#C8C8C8, markers #6A6A6A");
        Check(new[] { "SyntaxBold", "SyntaxItalic", "SyntaxStrike" }.All(k => ThemeService.Lookup(k, AppTheme.Light) == ThemeService.Lookup("TextPrimary", AppTheme.Light)), "Item 44: Light emphasis colours equal the Light text colour (unchanged look)");
        Check(Fg(I("bold")) == Color.Parse("#FFFFFF") && Fg(I("it*")) == Color.Parse("#E0E0E0") && Fg(I("gone")) == Color.Parse("#C8C8C8") && Fg(I("~~gone")) == Color.Parse("#6A6A6A")
            && RunAt(I("gone"))!.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true, "Item 44: Dark bold/italic/strike runs render in their colours, strike still struck, markers dim");
        Check(L("SyntaxH1") == Color.Parse("#569CD6") && L("SyntaxH2") == Color.Parse("#4EC9B0") && L("SyntaxH3") == Color.Parse("#C586C0") && L("SyntaxH4") == Color.Parse("#9CDCFE")
            && L("SyntaxMarker") == Color.Parse("#6A6A6A") && L("SyntaxInlineCode") == Color.Parse("#CE9178") && L("SyntaxInlineCodeBackground") == Color.Parse("#2D2D2D")
            && L("SyntaxCodeBlockBackground") == Color.Parse("#1E1E1E") && L("SyntaxLink") == Color.Parse("#3794FF") && L("SyntaxListMarker") == Color.Parse("#D7BA7D")
            && L("SyntaxQuote") == Color.Parse("#9E9E9E") && L("SyntaxCheckbox") == Color.Parse("#D7BA7D"), "Dark palette matches the spec");
        Check(RunAt(I("Heading"))!.Typeface.Weight == FontWeight.Bold && RunAt(I("bold"))!.Typeface.Weight == FontWeight.Bold && RunAt(I("it*"))!.Typeface.Style == FontStyle.Italic
            && RunAt(I("gone"))!.TextDecorations?.Any(d => d.Location == TextDecorationLocation.Strikethrough) == true && RunAt(I(" tail"))!.Typeface.Weight == FontWeight.Normal,
            "Bold, italic and strikethrough fonts reach the rendered runs");
        var height = presenter.TextLayout.Height;
        var tailX = presenter.TextLayout.HitTestTextPosition(I(" tail") + 5).X;
        var italicX = presenter.TextLayout.HitTestTextPosition(I("* tail") + 6).X;
        editor.HighlightMarkdown = false; Layout();
        var plainTailX = presenter.TextLayout.HitTestTextPosition(I(" tail") + 5).X;
        var plainItalicX = presenter.TextLayout.HitTestTextPosition(I("* tail") + 6).X;
        Console.WriteLine($"  column shift after **bold**: {tailX - plainTailX:F2}px, after *it*: {italicX - plainItalicX:F2}px (font size {editor.FontSize})");
        Check(Math.Abs(presenter.TextLayout.Height - height) < 0.01, "Highlighting keeps line heights (caret, gutter and find geometry unchanged)");
        Check(Math.Abs(tailX - plainTailX) <= editor.FontSize * 0.25 && Math.Abs(italicX - plainItalicX) <= editor.FontSize * 0.25, "Bold and italic shift later columns by under a quarter character");
        editor.HighlightMarkdown = true; Layout();

        var overlay = window.GetVisualDescendants().OfType<MarkdownBlockOverlay>().Single();
        overlay.InvalidateVisual(); Layout();
        var blocks = overlay.VisibleBlocks();
        var code = blocks.Single(b => b.Kind == SyntaxBlockKind.Code).Rect;
        var quote = blocks.Single(b => b.Kind == SyntaxBlockKind.Quote).Rect;
        var lineH = presenter.TextLayout.TextLines[0].Height;
        Check(code.X == 0 && Math.Abs(code.Width - overlay.Bounds.Width) < 0.5 && Math.Abs(code.Height - 3 * lineH) < 1, "The fenced block gets a full-width background over its three lines");
        Check(quote.Width == MarkdownBlockOverlay.QuoteBarWidth && Math.Abs(quote.Height - lineH) < 1 && presenter.TranslatePoint(default, overlay)!.Value.X > quote.Right, "Quotes get a left bar beside the text");
        var current = window.GetVisualDescendants().OfType<CurrentLineOverlay>().Single();
        var host = overlay.Parent as Panel;
        Check(host is not null && host.Children.IndexOf(overlay) < host.Children.IndexOf(current), "Block backgrounds are drawn under the current-line band and the text");

        // Selection colour still wins over syntax colours, without changing the layout.
        editor.Focus(); editor.SelectionStart = I("Heading"); editor.SelectionEnd = I("Heading") + 4; Layout();
        if (editor.SelectionForegroundBrush is ISolidColorBrush sel)
            Check(Fg(I("Heading")) == sel.Color && Fg(I("Heading") + 5) == L("SyntaxH1") && Math.Abs(presenter.TextLayout.Height - height) < 0.01, "Selection colour overrides syntax colours inside the selection only");
        else Check(Fg(I("Heading")) == L("SyntaxH1"), "Without a selection foreground, syntax colours show under the selection");
        editor.SelectionStart = editor.SelectionEnd = editor.CaretIndex = 0; Layout();

        // Typing and undo refresh the tokens.
        editor.SelectedText = "#"; Layout();
        Check(editor.Text!.StartsWith("## Heading") && Fg(I("Heading") + 1) == L("SyntaxH2"), "Typing refreshes highlighting (H1 becomes H2)");
        editor.Undo(); Layout();
        Check(editor.Text == text && Fg(I("Heading")) == L("SyntaxH1"), "Undo restores text and colours");

        // Live theme switch.
        ThemeService.Apply(AppTheme.Light, persist: false); Layout();
        Check(Colours() && L("SyntaxH1") != ThemeService.Lookup("SyntaxH1", AppTheme.Dark), "Light: colours switch live to the Light+ palette");
        Check(((ISolidColorBrush)MarkdownBlockOverlay.CodeBrush).Color == L("SyntaxCodeBlockBackground"), "Light: the code block background switches too");
        ThemeService.Apply(AppTheme.Dark, persist: false); Layout();
        Check(Colours(), "Dark again after switching back");

        // Word wrap and zoom.
        editor.TextWrapping = TextWrapping.Wrap;
        editor.FontSize = 24;
        editor.Text = "# " + new string('x', 180) + "\n12. item **b**";
        Layout();
        Check(presenter.TextLayout.TextLines.Count > 2 && Fg(5) == L("SyntaxH1"), "Highlighted text supports word wrap and zoom");

        // Typing in a large note: the layout with highlighting costs about the same as without.
        editor.TextWrapping = TextWrapping.NoWrap; editor.FontSize = 15;
        var big = string.Concat(Enumerable.Range(0, 1500).Select(i => $"## Section {i}\n\nSome **bold** and *italic* text with `code` and a [link](https://x.y/{i}).\n- item {i}\n```cs\nvar x{i} = 1; // c\n```\n\n"));
        double TypeMs()
        {
            editor.Text = big; Layout();
            editor.CaretIndex = editor.SelectionStart = editor.SelectionEnd = big.Length / 2;
            var watch = Stopwatch.StartNew(); editor.SelectedText = "x"; Layout(); return watch.Elapsed.TotalMilliseconds;
        }
        var lit = TypeMs(); editor.HighlightMarkdown = false; var unlit = TypeMs(); editor.HighlightMarkdown = true;
        Console.WriteLine($"  typing one character in a 12,000-line note: {lit:F0} ms highlighted, {unlit:F0} ms plain");
        Check(lit < unlit * 3 + 150, "Typing in a large note stays close to the unhighlighted cost");

        // Plain-text notes stay unhighlighted.
        document.Path = "plain.txt"; document.Refresh(); Layout();
        Check(!presenter.HighlightMarkdown && presenter.TextLayout.TextLines.SelectMany(l => l.TextRuns).All(r => r.Properties?.ForegroundBrush is not { } b || !ReferenceEquals(b, MarkdownTextPresenter.BrushFor(SyntaxColor.H1)))
            && overlay.VisibleBlocks().Count == 0, "Plain-text notes have no Markdown colours or block decorations");
        document.SavedText = document.Text;
        window.Close();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
