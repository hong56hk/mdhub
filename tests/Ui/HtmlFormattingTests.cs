using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Rendering;
using Mdhub.Services;
using Mdhub.Views;

internal static class HtmlFormattingTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        void Pump() { using var timeout = new CancellationTokenSource(300); Dispatcher.UIThread.MainLoop(timeout.Token); }
        EditorBehavior.Import(null);
        Check(EditorBehavior.Current.HtmlFormatting, "HTML defaults on, including older sessions");
        var saved = SessionStore.Deserialize(SessionStore.Serialize(new SessionData { EditorBehavior = new() { HtmlFormatting = false } }));
        Check(saved?.EditorBehavior?.HtmlFormatting == false, "Disabled HTML setting round trips through session JSON");
        Control Render(string source, string? directory = null) { var root = new StackPanel(); foreach (var c in MarkdownPreview.Render(MarkdownPreview.Parse(source), directory)) root.Children.Add(c); return root; }
        IEnumerable<Control> Tree(Control c)
        {
            yield return c;
            if (c is Panel p) foreach (var child in p.Children) foreach (var item in Tree(child)) yield return item;
            if (c is Decorator { Child: { } child2 }) foreach (var item in Tree(child2)) yield return item;
        }
        IEnumerable<Run> Runs(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run run) yield return run;
                if (inline is Span span) foreach (var nested in Runs(span.Inlines)) yield return nested;
            }
        }
        List<Run> AllRuns(Control c) => Tree(c).OfType<TextBlock>().SelectMany(t => Runs(t.Inlines!)).ToList();
        string Text(Control c) => string.Concat(Tree(c).OfType<TextBlock>().Select(t => t.Inlines is { Count: > 0 } ? string.Concat(Runs(t.Inlines).Select(r => r.Text)) : t.Text));

        var inline = Render("This **word** is bold. This <em>word</em> is italic. <u>under</u> <sup>2</sup> <sub>3</sub>");
        Check(AllRuns(inline).Any(r => r.Text == "word" && r.FontWeight == FontWeight.Bold), "Markdown bold still works beside HTML");
        Check(AllRuns(inline).Any(r => r.Text == "word" && r.FontStyle == FontStyle.Italic), "Inline HTML emphasis renders");
        Check(AllRuns(inline).Any(r => r.Text == "under" && r.TextDecorations == TextDecorations.Underline), "Underline renders");
        Check(AllRuns(inline).Any(r => r.Text == "2" && r.BaselineAlignment == BaselineAlignment.Superscript), "Superscript renders");
        Check(AllRuns(inline).Any(r => r.Text == "3" && r.BaselineAlignment == BaselineAlignment.Subscript), "Subscript renders");
        var block = Render("<div style='text-align: center; color: red'><p>italic and **bold** <strong>real bold</strong></p></div>");
        Check(Text(block).Contains("**bold**") && AllRuns(block).Any(r => r.Text == "real bold" && r.FontWeight == FontWeight.Bold), "Markdown stays literal inside block HTML; HTML bold works");
        Check(Tree(block).OfType<TextBlock>().All(t => t.TextAlignment == TextAlignment.Center), "Alignment inherits through containers");
        Check(AllRuns(block).All(r => r.Foreground is ISolidColorBrush b && b.Color == Colors.Red), "Color inherits through containers");
        var nested = Render("<div><ol start='3'><li>One<ul><li>Nested</li></ul></li><li>Two</li></ol></div>");
        Check(Text(nested).Contains("3.One•Nested4.Two"), "Nested HTML lists and ordered start render");
        var table = Render("<table><caption>Title</caption><thead><tr><th>A</th><th>B</th></tr></thead><tbody><tr><td colspan='2'>wide</td></tr></tbody></table>");
        Check(Text(table) == "TitleABwide", "Table caption, sections, headings and cells render");
        Check(Tree(table).OfType<Border>().Any(b => Grid.GetColumnSpan(b) == 2), "Table colspan is respected");
        Check(AllRuns(table).Where(r => r.Text is "A" or "B").All(r => r.FontWeight == FontWeight.Bold), "Table headings are bold");
        var optional = Render("<ul><li>One<li>Two</ul>");
        Check(Text(optional) == "•One•Two", "Optional list end tags are tolerated");
        var escaped = Render("`<b>code</b>` and &lt;b&gt;literal&lt;/b&gt;\n\n```html\n<strong>sample</strong>\n```");
        Check(Text(escaped).Contains("<b>code</b>") && Text(escaped).Contains("<strong>sample</strong>"), "Code spans and fences stay literal");
        Check(Text(escaped).Contains("<b>literal</b>"), "Escaped tags stay literal");
        Check(Text(Render("<p title='a > b'>A &amp; B &#x20; C</p>")) == "A & B C", "Quoted greater-than and HTML entities are handled");
        Check(Text(Render("<div><strong>unfinished")) == "unfinished", "Incomplete HTML remains renderable");
        Check(Text(Render("<p>before<img src='https://example.com/test.png' alt='remote'>after</p>")) == "beforeremoteafter", "Remote image uses alt text without network requests");
        var imagePath = Path.Combine(Path.GetTempPath(), $"mdhub-html-{Guid.NewGuid():N}.png");
        try
        {
            using (var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(40, 20))) bitmap.Save(imagePath, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            var imageBlock = Render($"<p><img src='{Path.GetFileName(imagePath)}' width='24' height='12'></p>", Path.GetDirectoryName(imagePath));
            var image = Tree(imageBlock).OfType<TextBlock>().SelectMany(t => t.Inlines!.OfType<InlineUIContainer>()).Select(i => i.Child).OfType<Image>().Single();
            Check(image.Width == 24 && image.Height == 12 && image.Source is not null, "Relative local HTML image and numeric dimensions render");
        }
        finally { File.Delete(imagePath); }
        Check(Text(Render("<pre>  a\n  b</pre>")) == "  a\n  b", "Preformatted HTML preserves whitespace");
        Check(Text(Render("<p>before<!-- hidden -->after</p>")) == "beforeafter", "HTML comments are hidden");
        var mixed = Render("Hello <em>italic</em> and **bold**.");
        var selection = new PreviewSelection(mixed);
        var copy = selection.Build(selection.Start, selection.End);
        Check(copy.Children.Count > 0, "Mixed HTML and Markdown remains copyable");
        var breaks = Render("First<br>Second");
        Check(Tree(breaks).OfType<TextBlock>().Any(t => t.Inlines!.OfType<LineBreak>().Any()), "HTML line break renders");
        EditorBehavior.Update(d => d with { HtmlFormatting = false }, false);
        Check(Text(Render("<div>**literal**</div>")).Contains("<div>**literal**</div>"), "Disabled block HTML appears as source");
        Check(Text(Render("hello <em>world</em>")).Contains("<em>world</em>"), "Disabled inline HTML appears as source");
        EditorBehavior.Update(d => d with { HtmlFormatting = true }, false);

        var w = new MainWindow { Width = 1500, Height = 850 };
        try
        {
            w.Show(); Pump();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            var editor = w.GetVisualDescendants().OfType<TextEditor>().Single();
            var preview = w.GetVisualDescendants().OfType<MdEditor>().Single();
            var formatter = w.GetVisualDescendants().OfType<TextFormatter>().Single();
            var htmlBar = formatter.FindControl<StackPanel>("HtmlFormatBar")!;
            NoteDocument Open(string text, string name) => (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, text, name, null])!;
            var doc = Open("<p>hello</p>", "html.md"); Pump();
            Check(htmlBar.IsVisible, "HTML toolbar is visible by default");
            foreach (var (action, expected) in new[]
            {
                ("html-underline", "before <u>Hello</u> after"),
                ("html-sup", "before <sup>Hello</sup> after"),
                ("html-sub", "before <sub>Hello</sub> after"),
                ("html-align-left", "before \n\n<div style=\"text-align: left;\">\nHello\n</div>\n\n after"),
                ("html-align-center", "before \n\n<div style=\"text-align: center;\">\nHello\n</div>\n\n after"),
                ("html-align-right", "before \n\n<div style=\"text-align: right;\">\nHello\n</div>\n\n after")
            })
            {
                doc.Editor.Text = "before Hello after"; doc.Editor.Focus();
                doc.Editor.SelectionStart = 7; doc.Editor.SelectionEnd = 12;
                var button = htmlBar.Children.OfType<Button>().Single(b => b.Tag as string == action);
                button.Focus(); Pump();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(doc.Text == expected, action + " applies to the selection after toolbar focus");
                doc.Editor.Undo();
                Check(doc.Text == "before Hello after", action + " is one undo step");
            }
            doc.Editor.Text = "before Hello after"; doc.Editor.Focus();
            doc.Editor.SelectionStart = 12; doc.Editor.SelectionEnd = 7;
            var colorButton = htmlBar.Children.OfType<Button>().Single(b =>
                b.Content is Grid grid && grid.Children.Any(c => c.Name == "HtmlTextColorIcon"));
            colorButton.Focus();
            var colorFlyout = (Flyout)colorButton.Flyout!;
            colorFlyout.ShowAt(colorButton); Pump();
            var colorPanel = (StackPanel)colorFlyout.Content!;
            var colorInput = colorPanel.Children.OfType<TextBox>().Single();
            colorInput.Focus(); colorInput.Text = "#1565c0";
            var applyColor = colorPanel.Children.OfType<Button>().Single();
            applyColor.Focus();
            applyColor.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(doc.Text == "before <span style=\"color: #1565C0;\">Hello</span> after", "Color flyout preserves a reversed editor selection while editing the color");
            doc.Editor.Undo();
            Check(doc.Text == "before Hello after", "Color selection formatting is one undo step");
            doc.Editor.Text = "<p>hello</p>"; doc.Editor.Focus();
            doc.Editor.SelectionStart = 3; doc.Editor.SelectionEnd = 8;
            editor.Format("bold");
            Check(doc.Text == "<p><strong>hello</strong></p>", "Bold toolbar uses HTML in an HTML block");
            doc.Editor.Undo(); Check(doc.Text == "<p>hello</p>", "HTML formatting is one undo step");
            doc.Editor.Text = "hello"; doc.Editor.SelectAll(); editor.Format("bold");
            Check(doc.Text == "**hello**", "Bold toolbar keeps Markdown outside HTML");
            doc.Editor.Text = "hello"; doc.Editor.SelectionStart = doc.Editor.SelectionEnd = 2;
            editor.Format("html-align-center");
            Check(doc.Text == "<div style=\"text-align: center;\">\nhello\n</div>", "Center button wraps the current paragraph");
            editor.Format("html-align-right");
            Check(doc.Text == "<div style=\"text-align: right;\">\nhello\n</div>", "Changing alignment updates the container instead of nesting conflicting styles");
            doc.Editor.Text = "line one\nline two"; doc.Editor.SelectionStart = doc.Editor.SelectionEnd = 12;
            editor.Format("html-align-center");
            Check(doc.Text == "<div style=\"text-align: center;\">\nline one\nline two\n</div>", "Alignment includes soft-wrapped paragraph lines");
            Pump();
            var checkbox = EditorFontsFlyout.BuildHtml();
            checkbox.IsChecked = false; Pump();
            Check(!EditorBehavior.Current.HtmlFormatting && !htmlBar.IsVisible, "Settings checkbox hides HTML controls immediately");
            var content = preview.FindControl<StackPanel>("PreviewContent")!;
            Check(Text(content).Contains("<div"), "Settings checkbox refreshes the open preview immediately");
            checkbox.IsChecked = true; Pump();
            Check(htmlBar.IsVisible && !Text(content).Contains("<div"), "Re-enabling restores controls and HTML preview");
            doc = Open("<p>**literal** <strong>bold</strong></p>", "page.HTML"); Pump();
            Check(doc.IsHtml && doc.HasPreview && !doc.IsMarkdown && preview.IsVisible, "HTML file opens with the limited preview");
            Check(Text(content).Contains("**literal** bold"), "HTML file skips Markdown parsing");
            doc.Editor.Text = "items"; doc.Editor.SelectAll(); editor.Format("ordered");
            Check(doc.Text == "<ol>\n  <li>items</li>\n</ol>", "HTML files use HTML ordered lists");
            doc.Editor.Text = "color"; doc.Editor.SelectAll(); editor.Format("html-color:#1565c0");
            Check(doc.Text == "<span style=\"color: #1565c0;\">color</span>", "Text color inserts a supported style");
            doc.Editor.Text = "<p>**literal** <strong>bold</strong></p>"; Pump();
            formatter.FindControl<Button>("EditorOnlyButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(!preview.IsVisible, "HTML preview toggle works");
            formatter.FindControl<Button>("TogglePreviewButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(preview.IsVisible, "HTML preview can be restored");
            Check(typeof(MdEditor).GetProperty("Selection", flags)!.GetValue(preview) is PreviewSelection, "HTML file preview supports selection and copy");
            checkbox.IsChecked = false; Pump();
            Check(Text(content).Contains("<strong>bold</strong>"), "Disabled HTML file preview shows source");
            var unchanged = doc.Text; doc.Editor.SelectAll(); editor.Format("html-underline");
            Check(doc.Text == unchanged, "Disabled HTML formatting cannot be invoked programmatically");
            checkbox.IsChecked = true;
            doc = Open("# HTML formatting\n\nMarkdown **bold** with <u>underline</u>, H<sub>2</sub>O and x<sup>2</sup>.\n\n<div style=\"text-align: center; color: #42a5f5;\">\n<h2>Centered HTML</h2>\n<p><strong>HTML bold</strong> and **literal Markdown**</p>\n</div>\n\n<table>\n<tr><th>Feature</th><th>Supported</th></tr>\n<tr><td>Tables and lists</td><td>Yes</td></tr>\n<tr><td>Alignment and colors</td><td>Yes</td></tr>\n</table>\n\n<ul><li>First item</li><li>Second item<ol><li>Nested list</li></ol></li></ul>", "HTML example.md");
            Pump(); Pump();
            Directory.CreateDirectory("artifacts");
            using var frame = w.CaptureRenderedFrame();
            frame?.Save(Path.GetFullPath("artifacts/html-formatting-preview.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { w.Close(); EditorBehavior.Import(null); }
        Console.WriteLine($"{count} HTML checks passed.");
    }
}
