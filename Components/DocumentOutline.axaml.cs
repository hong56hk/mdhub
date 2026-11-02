using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Mdhub.Rendering;

namespace Mdhub.Components;

public partial class DocumentOutline : UserControl
{
    public event Action? CloseRequested;
    public event Action<int>? HeadingRequested;
    private object? _document;
    private string? _text;
    private bool _updating;

    public DocumentOutline() => InitializeComponent();

    public void ShowDocument(object? document, string text)
    {
        if (ReferenceEquals(document, _document) && text == _text) return;
        var collapsed = ReferenceEquals(document, _document)
            ? Items(Headings.Items).Where(i => !i.IsExpanded).Select(i => (int)i.Tag!).ToHashSet()
            : [];
        _document = document;
        _text = text;
        _updating = true;
        try
        {
            Headings.Items.Clear();
            var parents = new Stack<(int Level, TreeViewItem Item)>();
            foreach (var heading in MarkdownPreview.Parse(text).Descendants<HeadingBlock>())
            {
                var title = heading.Inline is { } inline ? PlainText(inline) : heading.Lines.ToString();
                var label = new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis };
                var item = new TreeViewItem { Header = label, Tag = heading.Line + 1, IsExpanded = !collapsed.Contains(heading.Line + 1) };
                ToolTip.SetTip(item, title);
                while (parents.Count > 0 && parents.Peek().Level >= heading.Level) parents.Pop();
                if (parents.Count == 0) Headings.Items.Add(item);
                else parents.Peek().Item.Items.Add(item);
                parents.Push((heading.Level, item));
            }
            EmptyMessage.IsVisible = Headings.Items.Count == 0;
        }
        finally { _updating = false; }
    }

    private static IEnumerable<TreeViewItem> Items(ItemCollection items)
    {
        foreach (var item in items.OfType<TreeViewItem>())
        {
            yield return item;
            foreach (var child in Items(item.Items)) yield return child;
        }
    }

    private static string PlainText(ContainerInline inline)
    {
        var text = new System.Text.StringBuilder();
        foreach (var child in inline)
            text.Append(child switch
            {
                LiteralInline literal => literal.Content.ToString(),
                CodeInline code => code.Content,
                LineBreakInline => " ",
                HtmlEntityInline entity => entity.Transcoded.ToString(),
                ContainerInline container => PlainText(container),
                _ => ""
            });
        return text.ToString();
    }

    private void Heading_Selected(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updating && Headings.SelectedItem is TreeViewItem { Tag: int line }) HeadingRequested?.Invoke(line);
    }
    private void Close_Click(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
