using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;

namespace Mdhub.Rendering;

// Native controls only: no browser, script execution, or network fetching.
public static class MarkdownPreview
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().Build();
    public static MarkdownDocument Parse(string text) => Markdown.Parse(text, Pipeline);

    public static IEnumerable<Control> Render(ContainerBlock document)
    {
        foreach (var block in document)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    var title = InlineText(heading.Inline);
                    title.FontSize = heading.Level switch { 1 => 28, 2 => 23, 3 => 19, _ => 16 };
                    title.LineHeight = title.FontSize * 1.5;
                    title.FontWeight = FontWeight.SemiBold;
                    title.Margin = new Thickness(0, 10, 0, 4);
                    yield return title;
                    break;
                case ParagraphBlock paragraph:
                    yield return InlineText(paragraph.Inline);
                    break;
                case CodeBlock code:
                    yield return new Border
                    {
                        Background = Brush.Parse("#1B2028"), CornerRadius = new CornerRadius(5), Padding = new Thickness(14),
                        Child = new SelectableTextBlock { Text = code.Lines.ToString(), FontFamily = new FontFamily("Consolas, Menlo, monospace"), FontSize = 13, TextWrapping = TextWrapping.Wrap }
                    };
                    break;
                case QuoteBlock quote:
                    yield return new Border
                    {
                        BorderBrush = Brush.Parse("#7295D4"), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(14, 4),
                        Child = Stack(Render(quote))
                    };
                    break;
                case ListBlock list:
                    var items = new StackPanel { Spacing = 8 };
                    var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                        row.Children.Add(new TextBlock { Text = list.IsOrdered ? $"{number++}." : "•", Margin = new Thickness(0, 0, 10, 0) });
                        var content = Stack(Render(item));
                        Grid.SetColumn(content, 1);
                        row.Children.Add(content);
                        items.Children.Add(row);
                    }
                    yield return items;
                    break;
                case Table table:
                    var grid = new Grid();
                    var columnCount = table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
                    for (var c = 0; c < columnCount; c++) grid.ColumnDefinitions.Add(new ColumnDefinition());
                    var rowIndex = 0;
                    foreach (var row in table.OfType<TableRow>())
                    {
                        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                        var col = 0;
                        foreach (var cell in row.OfType<TableCell>())
                        {
                            var border = new Border
                            {
                                BorderBrush = Brush.Parse("#363D48"), BorderThickness = new Thickness(0.5), Padding = new Thickness(10),
                                Background = row.IsHeader ? Brush.Parse("#242B36") : Brushes.Transparent,
                                Child = Stack(Render(cell))
                            };
                            Grid.SetRow(border, rowIndex); Grid.SetColumn(border, col++);
                            grid.Children.Add(border);
                        }
                        rowIndex++;
                    }
                    yield return grid;
                    break;
                case ThematicBreakBlock:
                    yield return new Border { Height = 1, Background = Brush.Parse("#363D48"), Margin = new Thickness(0, 8) };
                    break;
                case HtmlBlock html:
                    yield return new SelectableTextBlock { Text = html.Lines.ToString(), TextWrapping = TextWrapping.Wrap };
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

    private static TextBlock InlineText(ContainerInline? inline)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 15, LineHeight = 24 };
        if (inline is not null) AddInlines(text.Inlines!, inline);
        return text;
    }

    private static void AddInlines(InlineCollection target, ContainerInline container)
    {
        foreach (var item in container)
        {
            switch (item)
            {
                case LiteralInline literal: target.Add(new Run(literal.Content.ToString())); break;
                case CodeInline code:
                    target.Add(new Run(code.Content) { FontFamily = new FontFamily("Consolas, Menlo, monospace"), Background = Brush.Parse("#29313C"), Foreground = Brush.Parse("#B8CFF4") });
                    break;
                case LineBreakInline: target.Add(new LineBreak()); break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar == '~') span.TextDecorations = TextDecorations.Strikethrough;
                    else if (emphasis.DelimiterCount >= 2) span.FontWeight = FontWeight.Bold;
                    else span.FontStyle = FontStyle.Italic;
                    AddInlines(span.Inlines, emphasis); target.Add(span);
                    break;
                case LinkInline link:
                    var linkSpan = new Span { Foreground = Brush.Parse("#8DADE8") };
                    if (link.IsImage) linkSpan.Inlines.Add(new Run("[Image: "));
                    AddInlines(linkSpan.Inlines, link);
                    if (link.IsImage) linkSpan.Inlines.Add(new Run("]"));
                    target.Add(linkSpan);
                    break;
                case AutolinkInline auto: target.Add(new Run(auto.Url) { Foreground = Brush.Parse("#8DADE8") }); break;
                case HtmlInline html: target.Add(new Run(html.Tag)); break;
                case ContainerInline nested: AddInlines(target, nested); break;
            }
        }
    }
}
