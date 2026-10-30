using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;

namespace Mdhub.Rendering;

// Native controls only: no browser, script execution, or network fetching.
public static class MarkdownPreview
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().UsePipeTables().UseEmphasisExtras().Build();
    public static MarkdownDocument Parse(string text) => Markdown.Parse(text, Pipeline);

    [ThreadStatic] private static string? _baseDirectory;

    // Local images (relative to baseDirectory, or absolute) load from disk; remote URLs stay labels.
    public static IReadOnlyList<Control> Render(ContainerBlock document, string? baseDirectory)
    {
        var previous = _baseDirectory;
        _baseDirectory = baseDirectory;
        try { return Render(document).ToList(); }
        finally { _baseDirectory = previous; }
    }

    // Renders and records which control / run shows which source range, for the selection highlight.
    public static PreviewRender RenderMapped(ContainerBlock document, string source, string? baseDirectory)
    {
        var previous = (_baseDirectory, _result, _block, _fallback);
        var result = new PreviewRender(new PreviewSourceMap(source));
        (_baseDirectory, _result, _block, _fallback) = (baseDirectory, result, -1, -1);
        try { result.Controls.AddRange(Render(document)); return result; }
        finally { (_baseDirectory, _result, _block, _fallback) = previous; }
    }

    [ThreadStatic] private static PreviewRender? _result;
    [ThreadStatic] private static int _block, _fallback;

    private static T Mapped<T>(T control, Block block, bool container = false) where T : Control
    {
        if (_result is { } result)
            result.Blocks[result.Map.AddBlock(block.Span.Start, block.Span.End + 1, container ? -1 : _fallback, container)] = control;
        return control;
    }

    private static int MapRun(InlineCollection target, Run run, int sourceStart)
    {
        if (_result is not { } result || _block < 0 || run.Text is not { } text) return -1;
        var id = result.Map.AddSegment(_block, sourceStart, text);
        if (id >= 0) result.Runs[id] = (target, run);
        return id;
    }

    // Content range of a fenced block whose lines are separated only by line breaks (no indentation or > / list markers).
    private static (int Start, int End)? ContiguousLines(string source, Markdig.Helpers.StringLine[] lines, int count)
    {
        for (var i = 1; i < count; i++)
        {
            var gap = source.AsSpan(lines[i - 1].Slice.End + 1, lines[i].Slice.Start - lines[i - 1].Slice.End - 1);
            if (gap is not ("\n" or "\r\n" or "\r")) return null;
        }
        var start = lines[0].Slice.Start;
        var end = lines[count - 1].Slice.End + 1;
        return start >= 0 && end <= source.Length && end >= start ? (start, end) : null;
    }

    // Some spans include padding the content drops (table cells trim spaces): find the text inside the span.
    private static int LocateIn(Markdig.Syntax.SourceSpan span, string? text)
    {
        if (_result is not { } result || string.IsNullOrEmpty(text) || span.Start < 0 || span.End >= result.Map.Source.Length) return -1;
        var source = result.Map.Source;
        if (span.Start + text.Length <= source.Length && string.CompareOrdinal(source, span.Start, text, 0, text.Length) == 0) return span.Start;
        return source.IndexOf(text, span.Start, span.Length, StringComparison.Ordinal);
    }

    public static IEnumerable<Control> Render(ContainerBlock document)
    {
        foreach (var block in document)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    var title = InlineText(heading.Inline, heading);
                    title.FontSize = heading.Level switch { 1 => 28, 2 => 23, 3 => 19, _ => 16 };
                    title.LineHeight = HasInlineImage(title.Inlines!) ? double.NaN : title.FontSize * 1.5;
                    title.FontWeight = FontWeight.SemiBold;
                    title.Margin = new Thickness(0, 10, 0, 4);
                    PreviewTags.Tag(title, $"h{Math.Clamp(heading.Level, 1, 6)}");
                    yield return title;
                    break;
                case ParagraphBlock paragraph when ImageOnly(paragraph.Inline) is { } images:
                    foreach (var image in images) yield return Mapped(image, paragraph);
                    break;
                case ParagraphBlock paragraph:
                    yield return PreviewTags.Tag(InlineText(paragraph.Inline, paragraph), "p");
                    break;
                case CodeBlock code:
                    var codeText = Mapped(PreviewTags.Tag(new TextBlock { FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 13, TextWrapping = TextWrapping.Wrap }, "pre"), code);
                    var codeBlockId = _result is { } codeResult ? codeResult.Map.Blocks.Count - 1 : -1;
                    var lines = code.Lines.Lines ?? [];
                    for (var i = 0; i < code.Lines.Count; i++)
                    {
                        if (i > 0) codeText.Inlines!.Add(new LineBreak());
                        var slice = lines[i].Slice;
                        var lineRun = new Run(slice.ToString());
                        codeText.Inlines!.Add(lineRun);
                        var saved = _block; _block = codeBlockId;
                        MapRun(codeText.Inlines, lineRun, slice.Start);
                        _block = saved;
                    }
                    if (_result is { } fenceResult && code is FencedCodeBlock && code.Lines.Count > 0 && codeBlockId >= 0 && ContiguousLines(fenceResult.Map.Source, lines, code.Lines.Count) is { } fenceContent)
                        fenceResult.CodeContent[codeBlockId] = fenceContent;
                    yield return new Border { Background = Mdhub.Services.ThemeService.Brush("CodeBlockBackground"), CornerRadius = new CornerRadius(5), Padding = new Thickness(14), Child = codeText };
                    break;
                case QuoteBlock quote:
                    yield return PreviewTags.Tag(new Border
                    {
                        BorderBrush = Mdhub.Services.ThemeService.Brush("QuoteBorder"), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(14, 4),
                        Child = Stack(Render(quote))
                    }, "blockquote");
                    break;
                case ListBlock list:
                    var items = PreviewTags.Tag(new StackPanel { Spacing = 8 }, list.IsOrdered ? "ol" : "ul", list.IsOrdered ? list.OrderedStart ?? "1" : null);
                    var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var row = PreviewTags.Tag(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") }, "li");
                        row.Children.Add(PreviewTags.Tag(new TextBlock { Text = list.IsOrdered ? $"{number++}." : "•", Margin = new Thickness(0, 0, 10, 0) }, "marker"));
                        var content = Stack(Render(item));
                        Grid.SetColumn(content, 1);
                        row.Children.Add(content);
                        items.Children.Add(row);
                    }
                    yield return items;
                    break;
                case Table table:
                    var grid = Mapped(PreviewTags.Tag(new Grid(), "table"), table, container: true);
                    var outerFallback = _fallback;
                    if (_result is { } tableResult) _fallback = tableResult.Map.Blocks.Count - 1;
                    var columnCount = table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
                    for (var c = 0; c < columnCount; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
                    var rowIndex = 0;
                    foreach (var row in table.OfType<TableRow>())
                    {
                        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                        var col = 0;
                        foreach (var cell in row.OfType<TableCell>())
                        {
                            var border = PreviewTags.Tag(new Border
                            {
                                BorderBrush = Mdhub.Services.ThemeService.Brush("TableBorder"), BorderThickness = new Thickness(0.5), Padding = new Thickness(10),
                                Background = row.IsHeader ? Mdhub.Services.ThemeService.Brush("TableHeaderBackground") : Brushes.Transparent,
                                Child = Stack(Render(cell))
                            }, row.IsHeader ? "th" : "td");
                            Grid.SetRow(border, rowIndex); Grid.SetColumn(border, col++);
                            grid.Children.Add(border);
                        }
                        rowIndex++;
                    }
                    _fallback = outerFallback;
                    yield return grid;
                    break;
                case ThematicBreakBlock:
                    yield return Mapped(PreviewTags.Tag(new Border { Height = 1, Background = Mdhub.Services.ThemeService.Brush("RuleForeground"), Margin = new Thickness(0, 8) }, "hr"), block);
                    break;
                case HtmlBlock html:
                    yield return Mapped(PreviewTags.Tag(new TextBlock { Text = html.Lines.ToString(), TextWrapping = TextWrapping.Wrap }, "html"), html);
                    break;
                case ContainerBlock container:
                    yield return Stack(Render(container));
                    break;
            }
        }
    }

    private static StackPanel Stack(IEnumerable<Control> controls)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }

    // A paragraph of nothing but loadable local images renders them as blocks scaled to the pane width.
    private static List<Control>? ImageOnly(ContainerInline? inline)
    {
        if (inline is null) return null;
        var images = new List<Control>();
        foreach (var item in inline)
        {
            if (item is LinkInline { IsImage: true } link && LoadImage(link) is { } image) images.Add(image);
            else if (item is LineBreakInline || item is LiteralInline literal && literal.Content.IsEmptyOrWhitespace()) continue;
            else return null;
        }
        return images.Count > 0 ? images : null;
    }

    internal static string? ResolveLocalImage(string? url, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme != Uri.UriSchemeFile && !Path.IsPathRooted(url)) return null;
        string path;
        try
        {
            path = uri is { IsFile: true } ? uri.LocalPath : Uri.UnescapeDataString(url);
            if (!Path.IsPathRooted(path))
            {
                if (baseDirectory is null) return null;
                path = Path.GetFullPath(Path.Combine(baseDirectory, path));
            }
        }
        catch (Exception) { return null; }
        return File.Exists(path) ? path : null;
    }

    private static Image? LoadImage(LinkInline link)
    {
        if (ResolveLocalImage(link.Url, _baseDirectory) is not { } path) return null;
        try
        {
            using var stream = File.OpenRead(path);
            return new Image
            {
                Source = new Bitmap(stream), Tag = path,
                Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Margin = new Thickness(0, 4)
            };
        }
        catch (Exception) { return null; }
    }

    private static TextBlock InlineText(ContainerInline? inline, LeafBlock owner)
    {
        var text = Mapped(new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 24 }, owner);
        var saved = _block;
        _block = _result is { } result ? result.Map.Blocks.Count - 1 : -1;
        try { if (inline is not null) AddInlines(text.Inlines!, inline); }
        finally { _block = saved; }
        // A fixed text line height clips embedded controls, notably images after list labels.
        if (HasInlineImage(text.Inlines!)) text.LineHeight = double.NaN;
        return text;
    }

    private static bool HasInlineImage(InlineCollection inlines) => inlines.Any(inline =>
        inline is InlineUIContainer { Child: Image } || inline is Span span && HasInlineImage(span.Inlines));

    private static void AddInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var item in container)
        {
            switch (item)
            {
                case LiteralInline literal:
                    var literalRun = new Run(literal.Content.ToString());
                    target.Add(literalRun);
                    MapRun(target, literalRun, LocateIn(literal.Span, literalRun.Text));
                    break;
                case CodeInline code:
                    var codeRun = new Run(code.Content) { FontFamily = new FontFamily("Consolas, Menlo, monospace"), Background = Mdhub.Services.ThemeService.Brush("InlineCodeBackground"), Foreground = Mdhub.Services.ThemeService.Brush("InlineCodeForeground") };
                    PreviewTags.Tag(codeRun, "code");
                    target.Add(codeRun);
                    if (_result is { } r && code.Content.Length > 0 && code.Span.Start >= 0 && code.Span.End < r.Map.Source.Length)
                    {
                        var at = r.Map.Source.IndexOf(code.Content, code.Span.Start, code.Span.Length, StringComparison.Ordinal);
                        if (at >= 0 && MapRun(target, codeRun, at) is var codeId and >= 0) r.CodeSegments.Add(codeId);
                    }
                    break;
                case LineBreakInline: target.Add(new LineBreak()); break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~') PreviewTags.Tag(span, "del").TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount >= 2) PreviewTags.Tag(span, "strong").FontWeight = FontWeight.Bold;
                    else PreviewTags.Tag(span, "em").FontStyle = FontStyle.Italic;
                    AddInlines(span.Inlines, emphasis); target.Add(span);
                    break;
                case LinkInline { IsImage: true } picture when LoadImage(picture) is { } inlineImage:
                    target.Add(new InlineUIContainer(inlineImage));
                    break;
                case LinkInline link:
                    var linkSpan = PreviewTags.Tag(new Span { Foreground = Mdhub.Services.ThemeService.Brush("LinkForeground") }, "a", link.Url);
                    if (link.IsImage) linkSpan.Inlines.Add(new Run("[Image: "));
                    AddInlines(linkSpan.Inlines, link);
                    if (link.IsImage) linkSpan.Inlines.Add(new Run("]"));
                    target.Add(linkSpan);
                    break;
                case AutolinkInline auto: target.Add(PreviewTags.Tag(new Run(auto.Url) { Foreground = Mdhub.Services.ThemeService.Brush("LinkForeground") }, "a", auto.IsEmail ? "mailto:" + auto.Url : auto.Url)); break;
                case HtmlInline html: target.Add(new Run(html.Tag)); break;
                case ContainerInline nested: AddInlines(target, nested); break;
            }
        }
    }
}

public sealed class PreviewRender(PreviewSourceMap map)
{
    public List<Control> Controls { get; } = [];
    public PreviewSourceMap Map { get; } = map;
    public Dictionary<int, Control> Blocks { get; } = [];
    public Dictionary<int, (InlineCollection Parent, Run Run)> Runs { get; } = [];
    // Segments that are inline code (edited without Markdown escaping).
    public HashSet<int> CodeSegments { get; } = [];
    // Fenced code blocks whose plain text can be edited: block id -> content range.
    public Dictionary<int, (int Start, int End)> CodeContent { get; } = [];
}
