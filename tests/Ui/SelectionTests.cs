using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Rendering;
using Mdhub.Services;
using Mdhub.Views;

// Item 24: selectable Markdown preview, rendered-content copy (CF_HTML / text / RTF) and the I-beam.
internal static class SelectionTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var timeout = new CancellationTokenSource(100); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { using var timeout = new CancellationTokenSource(700); Dispatcher.UIThread.MainLoop(timeout.Token); } // past the double-click time
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        var w = new MainWindow { Width = 1400, Height = 1000 };
        w.Show(); Pump();
        var view = w.GetVisualDescendants().OfType<MainView>().First();
        var md = w.GetVisualDescendants().OfType<MdEditor>().First();
        T G<T>(string name) => (T)typeof(MdEditor).GetProperty(name, flags)!.GetValue(md)!;
        void SetSel(PreviewPosition a, PreviewPosition b) => typeof(MdEditor).GetMethod("SetPreviewSelection", flags)!.Invoke(md, [a, b]);
        string LastCopy(string part) { var copy = typeof(MdEditor).GetProperty("LastCopy", flags)!.GetValue(md)!; return (string)copy.GetType().GetProperty(part)!.GetValue(copy)!; }
        void Wait(Task task) { for (var i = 0; i < 50 && !task.IsCompleted; i++) Pump(); }
        bool Copy() { var task = (Task<bool>)typeof(MdEditor).GetMethod("CopyPreviewAsync", flags)!.Invoke(md, [])!; Wait(task); return task.IsCompleted && task.Result; }
        void Key(Key key, RawInputModifiers mods) { w.KeyPress(key, mods, PhysicalKey.None, null); w.KeyRelease(key, mods, PhysicalKey.None, null); Pump(); }

        var source = "# Caf\u00e9 \u4e2d\u6587 Title\n\nHello **bold** and *italic* with `code` and a [link](https://example.com/a?b=1&c=2) ~~gone~~.\n\n"
            + "- first item\n- second **item**\n\n3. three\n4. four\n\n> quoted text\n\n| H1 | H2 |\n|---|---|\n| c1 | c2 |\n| c3 | c4 |\n\n"
            + "```\nlet x = 1;\nlet y = 2;\n```\n\n---\n\nLine one\nline two\n";
        var doc = (Mdhub.Models.NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, source, "select.md", null])!;
        for (var i = 0; i < 6; i++) Pump();
        var preview = w.GetVisualDescendants().OfType<StackPanel>().First(c => c.Name == "PreviewContent");
        var selection = G<PreviewSelection>("Selection");
        Check(selection is not null && preview.Bounds.Width > 100, "a rendered Markdown preview is selectable");
        var lb = PreviewSelection.LineBreakLength;
        string Flat(InlineCollection inl) => string.Concat(inl.Select(i => i switch { Run r => r.Text, Span s => Flat(s.Inlines), LineBreak => new string('\n', lb), _ => "\uFFFC" }));
        string Text(int item) => selection.Items[item] is TextBlock tb ? (tb.Inlines is { Count: > 0 } inl ? Flat(inl) : tb.Text ?? "") : "";
        int Item(string prefix) => Enumerable.Range(0, selection.Items.Count).First(i => Text(i).StartsWith(prefix));
        PreviewPosition Pos(string prefix, string at, int extra = 0) { var i = Item(prefix); return new(i, Text(i).IndexOf(at, StringComparison.Ordinal) + extra); }

        // Text offsets agree with the text layout (line breaks included) for every text block.
        var texts = Enumerable.Range(0, selection.Items.Count).Where(i => selection.Items[i] is TextBlock).ToList();
        Check(texts.All(i => selection.Length(i) + 1 == ((TextBlock)selection.Items[i]).TextLayout.TextLines.Sum(l => l.Length) || selection.Length(i) == 0),
            $"{texts.Count} text blocks: offsets match the text layout plus its end-of-paragraph mark (line break = {lb})");
        Check(texts.Count >= 13 && selection.Items.Any(c => PreviewTags.Kind(c) == "hr") && !selection.Items.Any(c => PreviewTags.Kind(c) == "marker"),
            "items run in document order: headings, paragraphs, list items, cells, code, rule; list markers are not text items");
        Check(texts.All(i => selection.Items[i].Cursor?.ToString() == new Cursor(StandardCursorType.Ibeam).ToString()), "the I-beam shows over all selectable preview text");

        // Mouse: drag from the heading into the paragraph.
        Point At(PreviewPosition p)
        {
            var tb = (TextBlock)selection.Items[p.Item];
            var r = tb.TextLayout.HitTestTextPosition(p.Offset);
            return tb.TranslatePoint(new Point(r.X + 1, r.Y + r.Height / 2), w)!.Value;
        }
        Check(G<bool>("HasPreviewSelection") == false, "nothing is selected at first");
        var from = Pos("Caf", "Title"); var to = Pos("Hello", "and", 0);
        w.MouseDown(At(from), MouseButton.Left); Pump();
        w.MouseMove(At(new PreviewPosition(to.Item, 2)), RawInputModifiers.LeftMouseButton); Pump();
        w.MouseMove(At(to), RawInputModifiers.LeftMouseButton); Pump();
        w.MouseUp(At(to), MouseButton.Left); Pump();
        var range = G<(PreviewPosition From, PreviewPosition To)>("PreviewSelectionRange");
        Check(G<bool>("HasPreviewSelection") && range.From == from && range.To == to, $"dragging selects across blocks ({range.From} to {range.To})");
        var overlay = w.GetVisualDescendants().OfType<PreviewSelectionOverlay>().First();
        var rects = (IReadOnlyList<Rect>)typeof(MdEditor).GetMethod("SelectionRects", flags)!.Invoke(md, [overlay])!;
        Check(rects.Count >= 2 && rects.Any(r => r.Top < rects.Max(x => x.Top)), $"the selection is painted over both blocks ({rects.Count} rects)");
        Check(!G<bool>("IsEditingPreview"), "a single click or drag does not start a preview edit");
        Check(Copy() && LastCopy("Plain") == "Title\n\nHello **bold** ".Replace("**", ""), $"a cross-block selection copies only the selected rendered text: '{LastCopy("Plain")}'");
        Check(LastCopy("Html").StartsWith("<h1 style=\"font-size:28px;font-weight:600") && LastCopy("Html").Contains(">Title</h1><p style=\"margin:0 0 8px\">Hello <b style=\"font-weight:bold\">bold</b> </p>"),
            "partial HTML keeps the heading and bold styling of just the selected part");
        Settle();

        // Shift+click extends the selection.
        w.MouseDown(At(Pos("Hello", "link")), MouseButton.Left, RawInputModifiers.Shift); w.MouseUp(At(Pos("Hello", "link")), MouseButton.Left, RawInputModifiers.Shift); Pump();
        range = G<(PreviewPosition From, PreviewPosition To)>("PreviewSelectionRange");
        Check(range.From == from && range.To == Pos("Hello", "link"), "Shift+click extends the selection");
        Settle();

        // Keyboard: Ctrl+A selects all when the preview has focus, Ctrl+C copies.
        var empty = Pos("Line", "one");
        w.MouseDown(At(empty), MouseButton.Left); w.MouseUp(At(empty), MouseButton.Left); Pump();
        Check(G<bool>("PreviewHasFocus") && !G<bool>("HasPreviewSelection"), "a click focuses the preview and collapses the selection");
        var cmd = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        Key(Avalonia.Input.Key.A, cmd);
        range = G<(PreviewPosition From, PreviewPosition To)>("PreviewSelectionRange");
        Check(range.From == selection.Start && range.To == selection.End && doc.Editor.SelectionStart == doc.Editor.SelectionEnd, "Ctrl+A in the preview selects the whole preview (not the source)");
        var clipboard = TopLevel.GetTopLevel(w)!.Clipboard!;
        Wait(clipboard.SetTextAsync("old"));
        Key(Avalonia.Input.Key.C, cmd);
        for (var i = 0; i < 5; i++) Pump();
        var plain = LastCopy("Plain"); var html = LastCopy("Html"); var rtf = LastCopy("Rtf"); var cf = LastCopy("CfHtml");
        var clipText = clipboard.TryGetTextAsync(); Wait(clipText);
        Check(clipText.Result == plain, "Ctrl+C puts the plain text on the clipboard");
        var formats = clipboard.GetDataFormatsAsync(); Wait(formats);
        var names = formats.Result.Select(f => f.ToString()).ToList();
        Check(names.Any(n => n.Contains(OperatingSystem.IsWindows() ? "HTML Format" : "html")) && names.Any(n => n.Contains(OperatingSystem.IsWindows() ? "Rich Text Format" : "rtf")),
            $"the clipboard also carries HTML and RTF ({string.Join(", ", names)})");
        var htmlRead = clipboard.TryGetValueAsync(DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsWindows() ? "HTML Format" : OperatingSystem.IsMacOS() ? "public.html" : "text/html")); Wait(htmlRead);
        Check(htmlRead.Result is { } bytes && Encoding.UTF8.GetString(bytes) == (OperatingSystem.IsWindows() ? cf : html), "the HTML format holds the CF_HTML payload as UTF-8 bytes");

        Check(plain.StartsWith("Caf\u00e9 \u4e2d\u6587 Title\n\nHello bold and italic with code and a link gone.") && plain.Contains("\u2022 first item\n\u2022 second item") && plain.Contains("3. three\n4. four")
            && plain.Contains("H1\tH2\nc1\tc2\nc3\tc4") && plain.Contains("let x = 1;\nlet y = 2;") && plain.Contains("quoted text") && plain.EndsWith("Line one\nline two"),
            "plain text is the rendered text: no Markdown markers, bullets and numbers for lists, tabs for table cells");
        Check(!Regex.IsMatch(plain, @"\*\*|`|\[link\]|^#|\|", RegexOptions.Multiline), "plain text has no #, **, `, [](), or | markers");
        Check(html.Contains("<i style=\"font-style:italic\">italic</i>") && html.Contains("<code style=\"font-family:Consolas,Menlo,monospace;") && html.Contains(">code</code>")
            && html.Contains("<a href=\"https://example.com/a?b=1&amp;c=2\" style=\"color:#") && html.Contains("<s style=\"text-decoration:line-through\">gone</s>"),
            "HTML styles italic, inline code, links (escaped href) and strikethrough inline");
        Check(html.Contains("<ul style=\"margin:0 0 8px;padding-left:24px\"><li style=\"margin:2px 0\">first item</li><li style=\"margin:2px 0\">second <b style=\"font-weight:bold\">item</b></li></ul>")
            && html.Contains("<ol start=\"3\" style=") && html.Contains("<blockquote style=") && html.Contains("<hr style="),
            "HTML lists (with the ordered start), quotes and rules");
        Check(Regex.IsMatch(html, @"<table style=""border-collapse:collapse[^""]*""><tr><th style=""border:1px solid #[0-9A-F]{6};[^""]*font-weight:bold[^""]*"">H1</th><th[^>]*>H2</th></tr><tr><td style=""border:1px solid[^""]*"">c1</td><td[^>]*>c2</td></tr><tr><td[^>]*>c3</td><td[^>]*>c4</td></tr></table>"),
            "HTML tables keep header cells, rows and borders");
        Check(html.Contains("<pre style=\"font-family:Consolas,Menlo,monospace;") && html.Contains("<code>let x = 1;\nlet y = 2;</code></pre>") && html.Contains("Line one<br>line two"),
            "HTML code blocks keep their lines; soft breaks become <br>");
        var light = (RichCopyStyle)typeof(MdEditor).GetMethod("CopyStyle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, [])!;
        Check(html.Contains($"color:{light.Link}") && light.Link == $"#{ThemeService.Lookup("LinkForeground", AppTheme.Light).R:X2}{ThemeService.Lookup("LinkForeground", AppTheme.Light).G:X2}{ThemeService.Lookup("LinkForeground", AppTheme.Light).B:X2}",
            "copied colours come from the Light palette in either theme");
        Check(rtf.StartsWith("{\\rtf1\\ansi") && rtf.EndsWith("}") && rtf.Contains("{\\b\\fs56 Caf\\u233?") && rtf.Contains("\\u20013?\\u25991?") && rtf.Contains("{\\b bold}") && rtf.Contains("{\\i italic}")
            && rtf.Contains("{\\f1\\highlight3 code}") && rtf.Contains("HYPERLINK \"https://example.com/a?b=1&c=2\"") && rtf.Contains("\\trowd") && rtf.Contains("\\bullet\\tab first item") && rtf.Contains("3.\\tab three")
            && rtf.Count(c => c == '{') == rtf.Count(c => c == '}'), "RTF has headings, bold, italic, code, links, lists and tables, escaped Unicode, and balanced groups");

        // CF_HTML header offsets are byte offsets into the UTF-8 payload.
        var cfBytes = Encoding.UTF8.GetBytes(cf);
        int Header(string name) => int.Parse(Regex.Match(cf, name + @":(\d{10})\r\n").Groups[1].Value);
        var (startHtml, endHtml, startFrag, endFrag) = (Header("StartHTML"), Header("EndHTML"), Header("StartFragment"), Header("EndFragment"));
        string Bytes(int a, int b) => Encoding.UTF8.GetString(cfBytes, a, b - a);
        Check(cf.StartsWith("Version:0.9\r\n") && Bytes(0, startHtml).EndsWith("\r\n") && Bytes(startHtml, startHtml + 6) == "<html>" && endHtml == cfBytes.Length && Bytes(endHtml - 7, endHtml) == "</html>",
            $"CF_HTML: StartHTML={startHtml} and EndHTML={endHtml} bracket the HTML document");
        Check(Bytes(startFrag, endFrag) == html && Bytes(startFrag - 20, startFrag) == "<!--StartFragment-->" && Bytes(endFrag, endFrag + 18) == "<!--EndFragment-->" && cfBytes.Length != cf.Length,
            $"CF_HTML: StartFragment={startFrag} / EndFragment={endFrag} select exactly the fragment (UTF-8 byte offsets, non-ASCII text)");

        // Partial selections copy only the selected part, with its formatting.
        string CopyHtml(PreviewPosition a, PreviewPosition b, out string text) { SetSel(a, b); Copy(); text = LastCopy("Plain"); return LastCopy("Html"); }
        var part = CopyHtml(Pos("Hello", "old"), Pos("Hello", "italic", 3), out var partText);
        Check(partText == "old and ita" && part == "<p style=\"margin:0 0 8px\"><b style=\"font-weight:bold\">old</b> and <i style=\"font-style:italic\">ita</i></p>", $"a selection inside one paragraph keeps partial bold/italic: {part}");
        part = CopyHtml(Pos("Hello", "ink"), Pos("Hello", "gone", 2), out partText);
        Check(partText == "ink go" && part.Contains("<a href=\"https://example.com/a?b=1&amp;c=2\"") && part.Contains(">ink</a>"), "a partly selected link keeps its href");
        part = CopyHtml(Pos("second", "item"), Pos("three", "ree"), out partText);
        Check(part.StartsWith("<ul style=") && part.Split("<li").Length - 1 == 2 && !part.Contains("first") && !part.Contains("four") && part.Contains("<b style=\"font-weight:bold\">item</b></li></ul><ol start=\"3\"") && partText == "\u2022 item\n\n3. th",
            $"a selection across lists copies only the touched items: {partText.Replace("\n", "\\n")}");
        part = CopyHtml(Pos("c1", "c1"), Pos("c2", "c2", 2), out partText);
        Check(Regex.IsMatch(part, @"^<table[^>]*><tr><td[^>]*>c1</td><td[^>]*>c2</td></tr></table>$") && partText == "c1\tc2", "a selection over two cells copies a one-row table");
        part = CopyHtml(Pos("let", "x"), Pos("let", "2"), out partText);
        Check(part.Contains("<code>x = 1;\nlet y = </code></pre>") && partText == "x = 1;\nlet y = ", "a partial code block copy keeps the selected lines as a code block");

        // The selection colour is a palette key and follows the theme.
        var brush = (ISolidColorBrush)typeof(MdEditor).GetField("SelectionBrush", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        Check(brush.Color == ThemeService.Lookup("PreviewSelectionBackground", AppTheme.Dark), "the selection uses PreviewSelectionBackground (Dark)");
        ThemeService.Apply(AppTheme.Light, persist: false); Pump();
        Check(brush.Color == ThemeService.Lookup("PreviewSelectionBackground", AppTheme.Light) && brush.Color != ThemeService.Lookup("PreviewSelectionBackground", AppTheme.Dark), "switching to Light recolours the selection live");
        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            var bg = ThemeService.Lookup("PreviewBackground", theme); var sel = ThemeService.Lookup("PreviewSelectionBackground", theme); var a = sel.A / 255.0;
            var mixed = Color.FromRgb((byte)(sel.R * a + bg.R * (1 - a)), (byte)(sel.G * a + bg.G * (1 - a)), (byte)(sel.B * a + bg.B * (1 - a)));
            var ratio = Contrast(ThemeService.Lookup("TextPrimary", theme), mixed);
            Check(ratio >= 4.5, $"{theme}: selected text stays readable ({ratio:0.0}:1)");
        }
        ThemeService.Apply(AppTheme.Dark, persist: false); Pump();

        // Editor-selection highlight splits runs: the copy keeps inline-code formatting.
        doc.Editor.SelectionStart = source.IndexOf("code`", StringComparison.Ordinal) + 1; doc.Editor.SelectionEnd = doc.Editor.SelectionStart + 2;
        for (var i = 0; i < 4; i++) Pump();
        Check(G<int>("HighlightedRuns") > 0, "the source selection is mirrored in the preview");
        part = CopyHtml(Pos("Hello", "with"), Pos("Hello", " and a"), out partText);
        Check(part.Split("<code").Length == 2 && part.Contains(">code</code>"), $"inline code stays one code span while the source highlight splits it: {part}");
        Settle();

        // Item 8: double-click still edits the block; it clears the preview selection.
        SelectAll();
        void SelectAll() => typeof(MdEditor).GetMethod("SelectAllPreview", flags)!.Invoke(md, []);
        var target = At(Pos("Line", "one"));
        for (var i = 0; i < 2; i++) { w.MouseDown(target, MouseButton.Left); w.MouseUp(target, MouseButton.Left); }
        Pump();
        Check(G<bool>("IsEditingPreview") && !G<bool>("HasPreviewSelection"), "double-clicking a block still opens the preview edit box and clears the selection");
        Key(Avalonia.Input.Key.A, cmd);
        var box = G<TextBox>("PreviewEditBox");
        Check(box.SelectionStart == 0 && box.SelectionEnd == box.Text!.Length && !G<bool>("HasPreviewSelection"), "Ctrl+A in the edit box selects its text, not the preview");
        Key(Avalonia.Input.Key.Escape, RawInputModifiers.None);
        Check(!G<bool>("IsEditingPreview") && doc.Editor.Text == source, "Esc cancels the edit with the source unchanged");

        // Re-rendering resets the selection; Edit > Copy routes to the preview when it has focus.
        SelectAll();
        doc.Editor.Text = source + "\nmore\n";
        for (var i = 0; i < 6; i++) Pump();
        Check(!G<bool>("HasPreviewSelection") && G<PreviewSelection>("Selection") != selection, "re-rendering the preview drops the stale selection");
        selection = G<PreviewSelection>("Selection");
        Settle();
        var p2 = At(Pos("Hello", "Hello"));
        w.MouseDown(p2, MouseButton.Left); w.MouseUp(p2, MouseButton.Left); Pump();
        SelectAll();
        Wait(clipboard.SetTextAsync("old"));
        typeof(MainView).GetMethod("Edit", flags)!.Invoke(view, ["copy"]);
        for (var i = 0; i < 5; i++) Pump();
        clipText = clipboard.TryGetTextAsync(); Wait(clipText);
        Check(clipText.Result!.EndsWith("line two\n\nmore") && !clipText.Result.Contains('#'), "Edit > Copy with the preview focused copies the rendered preview");
        doc.Editor.Focus(); Pump();
        typeof(MainView).GetMethod("Edit", flags)!.Invoke(view, ["select all"]); Pump();
        Check(doc.Editor.SelectionStart == 0 && doc.Editor.SelectionEnd == doc.Editor.Text!.Length, "Edit > Select All with the source focused still selects the source");
        w.Close(); Pump();
    }

    private static double Luminance(Color c)
    {
        static double L(byte v) { var s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * L(c.R) + 0.7152 * L(c.G) + 0.0722 * L(c.B);
    }
    private static double Contrast(Color a, Color b) { var (x, y) = (Luminance(a), Luminance(b)); return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05); }
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}