using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Mdhub.Services;
using Mdhub.Text;
using Node = Mdhub.Text.BasicHtml.Node;

namespace Mdhub.Rendering;

// Only native formatting controls: no CSS engine, scripts, event handlers or network loads.
internal static class HtmlPreview
{
    private sealed record Style(TextAlignment Align = TextAlignment.Left, IBrush? Color = null,
        bool Bold = false, bool Italic = false, string? Decoration = null, string? Baseline = null, bool Code = false, bool Pre = false);

    public static Control Render(string html, string? directory) => Blocks(BasicHtml.Parse(html).Children, new(), directory);

    public static void AddInlines(InlineCollection target, string html, string? directory)
    {
        var whitespace = true;
        Inline(target, BasicHtml.Parse(html).Children, new(), directory, ref whitespace);
    }

    private static Style Styled(Node n, Style inherited)
    {
        var s = inherited with
        {
            Bold = inherited.Bold || n.Name is "b" or "strong" or "th" || n.Name.Length == 2 && n.Name[0] == 'h' && char.IsDigit(n.Name[1]),
            Italic = inherited.Italic || n.Name is "i" or "em",
            Decoration = n.Name == "u" ? "underline" : n.Name is "s" or "del" ? "line-through" : inherited.Decoration,
            Baseline = n.Name is "sup" or "sub" ? n.Name : inherited.Baseline,
            Code = inherited.Code || n.Name is "code" or "pre",
            Pre = inherited.Pre || n.Name == "pre"
        };
        var declarations = (n.Attr("style") ?? "").Split(';');
        foreach (var declaration in declarations.Prepend("text-align:" + n.Attr("align")))
        {
            var parts = declaration.Split(':', 2);
            if (parts.Length != 2) continue;
            var value = parts[1].Trim();
            switch (parts[0].Trim().ToLowerInvariant())
            {
                case "text-align":
                    s = s with { Align = value.ToLowerInvariant() switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, "left" => TextAlignment.Left, _ => s.Align } }; break;
                case "color" when Color.TryParse(value, out var color): s = s with { Color = new SolidColorBrush(color) }; break;
            }
        }
        return s;
    }

    private static TextBlock Text(Style style) => new()
    {
        TextWrapping = TextWrapping.Wrap, FontFamily = MarkdownPreview.EffectiveBodyFamily,
        FontSize = PreviewSettings.Current.BodySize, TextAlignment = style.Align,
        Foreground = style.Color ?? ThemeService.Brush("TextPrimary")
    };

    private static StackPanel Blocks(IEnumerable<Node> nodes, Style style, string? directory)
    {
        var panel = new StackPanel { Spacing = PreviewSettings.NestedSpacing };
        var pending = new List<Node>();
        void Flush()
        {
            if (pending.Count == 0) return;
            var text = Text(style);
            var whitespace = true;
            Inline(text.Inlines!, pending, style, directory, ref whitespace);
            if (!ContainsImage(text.Inlines!)) text.LineHeight = PreviewSettings.BodyLineHeight;
            if (text.Inlines!.Count > 0) panel.Children.Add(PreviewTags.Tag(text, "html"));
            pending.Clear();
        }
        foreach (var n in nodes)
        {
            if (!BasicHtml.IsBlock(n.Name)) { pending.Add(n); continue; }
            Flush();
            var s = Styled(n, style);
            Control control;
            if (n.Name is "ol" or "ul")
            {
                var list = new StackPanel { Spacing = n.Name == "ol" ? PreviewSettings.Current.OrderedSpacing : PreviewSettings.Current.BulletSpacing };
                var number = int.TryParse(n.Attr("start"), out var start) ? start : 1;
                foreach (var item in n.Children.Where(c => c.Name == "li"))
                {
                    if (int.TryParse(item.Attr("value"), out var value)) number = value;
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
                    var marker = MarkdownPreview.ListMarker(n.Name == "ol" ? $"{number++}." : "•", n.Name == "ol");
                    PreviewTags.Tag(marker, "marker");
                    marker.Foreground = s.Color ?? ThemeService.Brush("TextPrimary");
                    PreviewTags.Tag(row, "li");
                    row.Children.Add(marker);
                    var content = Blocks(item.Children, Styled(item, s), directory);
                    Grid.SetColumn(content, 1); row.Children.Add(content); list.Children.Add(row);
                }
                control = PreviewTags.Tag(list, n.Name, n.Attr("start"));
            }
            else if (n.Name == "table") control = Table(n, s, directory);
            else if (n.Name == "hr") control = PreviewTags.Tag(new Border { Height = 1, Background = ThemeService.Brush("RuleForeground"), Margin = new Thickness(0, 8) }, "hr");
            else
            {
                control = Blocks(n.Children, s, directory);
                if (n.Name is "blockquote" or "pre") control = new Border
                {
                    Child = control, Padding = new Thickness(12, 6),
                    BorderThickness = new Thickness(n.Name == "blockquote" ? 3 : 0, 0, 0, 0), BorderBrush = ThemeService.Brush("QuoteBorder"),
                    Background = n.Name == "pre" ? ThemeService.Brush("CodeBlockBackground") : Brushes.Transparent
                };
                if (n.Name.Length == 2 && n.Name[0] == 'h' && n.Name[1] is >= '1' and <= '6')
                    foreach (var text in DescendantText(control))
                    {
                        text.FontSize = PreviewSettings.Current.BodySize * (n.Name[1] switch { '1' => 28, '2' => 23, '3' => 19, _ => 16 }) / 15;
                        text.LineHeight = text.FontSize * PreviewSettings.Current.LineHeight;
                    }
            }
            panel.Children.Add(control);
        }
        Flush();
        return panel;
    }

    private static IEnumerable<TextBlock> DescendantText(Control c)
    {
        if (c is TextBlock t) yield return t;
        if (c is Panel p) foreach (var child in p.Children) foreach (var text in DescendantText(child)) yield return text;
    }

    private static Control Table(Node table, Style style, string? directory)
    {
        var panel = new StackPanel { Spacing = PreviewSettings.NestedSpacing };
        foreach (var caption in table.Children.Where(n => n.Name == "caption")) panel.Children.Add(Blocks(caption.Children, Styled(caption, style), directory));
        var rows = table.Children.SelectMany(n => n.Name is "thead" or "tbody" or "tfoot" ? n.Children : [n]).Where(n => n.Name == "tr").ToList();
        var grid = new Grid();
        var occupied = new HashSet<(int Row, int Column)>();
        var rowIndex = 0;
        foreach (var row in rows)
        {
            while (grid.RowDefinitions.Count <= rowIndex) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var column = 0;
            foreach (var cell in row.Children.Where(n => n.Name is "td" or "th"))
            {
                var columns = SpanSize(cell.Attr("colspan"));
                var rowSpan = SpanSize(cell.Attr("rowspan"));
                while (Enumerable.Range(column, columns).Any(c => occupied.Contains((rowIndex, c)))) column++;
                while (grid.ColumnDefinitions.Count < column + columns) grid.ColumnDefinitions.Add(new ColumnDefinition());
                while (grid.RowDefinitions.Count < rowIndex + rowSpan) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var border = new Border
                {
                    BorderBrush = ThemeService.Brush("TableBorder"), BorderThickness = new Thickness(0.5), Padding = new Thickness(10),
                    Background = cell.Name == "th" ? ThemeService.Brush("TableHeaderBackground") : Brushes.Transparent,
                    Child = Blocks(cell.Children, Styled(cell, Styled(row, style)), directory)
                };
                PreviewTags.Tag(border, cell.Name);
                Grid.SetRow(border, rowIndex); Grid.SetColumn(border, column);
                Grid.SetColumnSpan(border, columns); Grid.SetRowSpan(border, rowSpan);
                for (var r = rowIndex; r < rowIndex + rowSpan; r++) for (var c = column; c < column + columns; c++) occupied.Add((r, c));
                grid.Children.Add(border); column += columns;
            }
            rowIndex++;
        }
        PreviewTags.Tag(grid, "table");
        panel.Children.Add(grid);
        return panel;
    }

    private static int SpanSize(string? value) => int.TryParse(value, out var n) ? Math.Clamp(n, 1, 100) : 1;

    private static void Inline(InlineCollection target, IEnumerable<Node> nodes, Style style, string? directory, ref bool whitespace)
    {
        foreach (var n in nodes)
        {
            var s = Styled(n, style);
            if (n.Name == "#text")
            {
                var value = s.Pre ? n.Text : Regex.Replace(n.Text, @"[ \t\r\n\f]+", " ");
                if (!s.Pre && whitespace) value = value.TrimStart(' ');
                if (value.Length == 0) continue;
                whitespace = value.EndsWith(' ');
                var run = new Run(value)
                {
                    Foreground = s.Color ?? ThemeService.Brush("TextPrimary"),
                    TextDecorations = s.Decoration == "underline" ? TextDecorations.Underline : s.Decoration == "line-through" ? TextDecorations.Strikethrough : null,
                    BaselineAlignment = s.Baseline == "sup" ? BaselineAlignment.Superscript : s.Baseline == "sub" ? BaselineAlignment.Subscript : BaselineAlignment.Baseline
                };
                if (s.Bold) run.FontWeight = FontWeight.Bold;
                if (s.Italic) run.FontStyle = FontStyle.Italic;
                if (s.Baseline is not null) run.FontSize = PreviewSettings.Current.BodySize * 0.75;
                if (s.Code) { run.FontFamily = new FontFamily(PreviewSettings.CodeFamilyList); run.FontSize = PreviewSettings.Current.CodeSize; }
                target.Add(run);
            }
            else if (n.Name == "br") { target.Add(new LineBreak()); whitespace = true; }
            else if (n.Name == "img")
            {
                if (Image(n, directory) is { } image) target.Add(new InlineUIContainer(image));
                else target.Add(new Run(n.Attr("alt") ?? "[Image]"));
                whitespace = false;
            }
            else
            {
                if (n.Name == "a")
                {
                    var span = PreviewTags.Tag(new Span(), "a", n.Attr("href"));
                    Inline(span.Inlines, n.Children, s with { Color = s.Color ?? ThemeService.Brush("LinkForeground") }, directory, ref whitespace);
                    target.Add(span);
                }
                else
                {
                    var kind = n.Name switch { "b" or "strong" => "strong", "i" or "em" => "em", "s" or "del" => "del", "code" => "code", _ => null };
                    if (kind is null) Inline(target, n.Children, s, directory, ref whitespace);
                    else
                    {
                        var span = PreviewTags.Tag(new Span(), kind);
                        Inline(span.Inlines, n.Children, s, directory, ref whitespace);
                        target.Add(span);
                    }
                }
            }
        }
    }

    private static bool ContainsImage(InlineCollection inlines) => inlines.Any(i => i is InlineUIContainer || i is Span span && ContainsImage(span.Inlines));

    private static Image? Image(Node n, string? directory)
    {
        if (MarkdownPreview.ResolveLocalImage(n.Attr("src"), directory) is not { } path) return null;
        try
        {
            using var stream = File.OpenRead(path);
            var image = new Image { Source = new Bitmap(stream), Tag = path, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left };
            if (double.TryParse(n.Attr("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var width) && double.IsFinite(width) && width > 0) image.Width = Math.Min(width, 4096);
            if (double.TryParse(n.Attr("height"), NumberStyles.Float, CultureInfo.InvariantCulture, out var height) && double.IsFinite(height) && height > 0) image.Height = Math.Min(height, 4096);
            return image;
        }
        catch (Exception) { return null; }
    }
}
