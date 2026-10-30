using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace Mdhub.Rendering;

// What a rendered preview control or inline stands for, so a selection can be copied as rendered content.
public static class PreviewTags
{
    private sealed class Box(string kind, string? value) { public string Kind = kind; public string? Value = value; }
    private static readonly ConditionalWeakTable<AvaloniaObject, Box> Tags = new();
    public static T Tag<T>(T target, string kind, string? value = null) where T : AvaloniaObject { Tags.AddOrUpdate(target, new Box(kind, value)); return target; }
    public static string? Kind(AvaloniaObject target) => Tags.TryGetValue(target, out var box) ? box.Kind : null;
    public static string? Value(AvaloniaObject target) => Tags.TryGetValue(target, out var box) ? box.Value : null;
    public static void Copy(AvaloniaObject from, AvaloniaObject to) { if (Tags.TryGetValue(from, out var box)) Tags.AddOrUpdate(to, new Box(box.Kind, box.Value)); }
}

// A caret position in the preview: an item (text block or atom such as an image or rule) and a character offset.
public readonly record struct PreviewPosition(int Item, int Offset) : IComparable<PreviewPosition>
{
    public int CompareTo(PreviewPosition other) => Item != other.Item ? Item.CompareTo(other.Item) : Offset.CompareTo(other.Offset);
    public static bool operator <(PreviewPosition a, PreviewPosition b) => a.CompareTo(b) < 0;
    public static bool operator >(PreviewPosition a, PreviewPosition b) => a.CompareTo(b) > 0;
    public static bool operator <=(PreviewPosition a, PreviewPosition b) => a.CompareTo(b) <= 0;
    public static bool operator >=(PreviewPosition a, PreviewPosition b) => a.CompareTo(b) >= 0;
}

// The selectable items of a rendered preview in document order, hit testing and the rendered-content copy.
public sealed class PreviewSelection
{
    public IReadOnlyList<Control> Items { get; }
    private readonly Dictionary<Control, int> _index = [];
    private readonly Control _root;

    public PreviewSelection(Control root)
    {
        _root = root;
        var items = new List<Control>();
        Collect(root, items);
        Items = items;
        for (var i = 0; i < items.Count; i++) _index[items[i]] = i;
    }

    private static void Collect(Control control, List<Control> items)
    {
        switch (control)
        {
            case TextBlock tb when PreviewTags.Kind(tb) != "marker": items.Add(tb); return;
            case TextBlock: return;
            case Image: items.Add(control); return;
            case Border when PreviewTags.Kind(control) == "hr": items.Add(control); return;
            case Panel panel: foreach (var child in panel.Children) Collect(child, items); return;
            case Decorator { Child: { } child }: Collect(child, items); return;
        }
    }

    public static bool IsText(Control item) => item is TextBlock;
    public int Length(int item) => Items[item] is TextBlock tb ? TextLength(tb) : 1;
    public int IndexOf(Control item) => _index.TryGetValue(item, out var i) ? i : -1;
    public PreviewPosition Start => new(0, 0);
    public PreviewPosition End => Items.Count == 0 ? new(0, 0) : new(Items.Count - 1, Length(Items.Count - 1));

    // ---- Text offsets as the text layout counts them ----
    private static int? _lineBreakLength;
    // A LineBreak's length in the text layout (Avalonia currently uses the platform newline); measured once.
    public static int LineBreakLength
    {
        get
        {
            if (_lineBreakLength is { } known) return known;
            var probe = new TextBlock { TextWrapping = TextWrapping.NoWrap };
            probe.Inlines!.Add(new Run("a")); probe.Inlines.Add(new LineBreak()); probe.Inlines.Add(new Run("b"));
            probe.Measure(Size.Infinity);
            var lines = probe.TextLayout.TextLines;
            _lineBreakLength = lines.Count >= 2 ? Math.Max(1, lines[1].FirstTextSourceIndex - 1) : Environment.NewLine.Length;
            return _lineBreakLength.Value;
        }
    }

    public static int TextLength(TextBlock tb) => tb.Inlines is { Count: > 0 } inlines ? Length(inlines) : tb.Text?.Length ?? 0;
    private static int Length(InlineCollection inlines) => inlines.Sum(inline => inline switch
    {
        Run run => run.Text?.Length ?? 0,
        LineBreak => LineBreakLength,
        Span span => Length(span.Inlines),
        _ => 1,
    });

    // ---- Hit testing ----
    public PreviewPosition? HitTest(Point point)
    {
        var texts = Enumerable.Range(0, Items.Count).Where(i => Items[i] is TextBlock && Items[i].IsVisible).ToList();
        if (texts.Count == 0) return null;
        Rect Bounds(int i) => Items[i].TranslatePoint(new Point(0, 0), _root) is { } at ? new Rect(at, Items[i].Bounds.Size) : default;
        foreach (var i in texts)
            if (Bounds(i).Contains(point)) return At(i, point);
        // Same band of lines (table cells side by side, list text beside its marker): nearest horizontally.
        var band = texts.Where(i => Bounds(i) is var r && point.Y >= r.Top && point.Y < r.Bottom)
            .OrderBy(i => Bounds(i) is var r ? (point.X < r.Left ? r.Left - point.X : point.X - r.Right) : 0).ToList();
        if (band.Count > 0) { var r = Bounds(band[0]); return At(band[0], new Point(Math.Clamp(point.X, r.Left, r.Right - 0.5), point.Y)); }
        foreach (var i in texts)
            if (point.Y < Bounds(i).Top) return new PreviewPosition(i, 0);
        var last = texts[^1];
        return new PreviewPosition(last, Length(last));
    }

    private PreviewPosition At(int item, Point point)
    {
        var tb = (TextBlock)Items[item];
        var local = _root.TranslatePoint(point, tb) ?? default;
        var hit = tb.TextLayout.HitTestPoint(new Point(local.X - tb.Padding.Left, local.Y - tb.Padding.Top));
        return new PreviewPosition(item, Math.Clamp(hit.TextPosition, 0, Length(item)));
    }

    // The selected character range of one item, or null when it is outside the selection.
    public (int Start, int End)? RangeOf(int item, PreviewPosition from, PreviewPosition to)
    {
        if (from > to) (from, to) = (to, from);
        if (item < from.Item || item > to.Item) return null;
        var start = item == from.Item ? from.Offset : 0;
        var end = item == to.Item ? to.Offset : Length(item);
        return end > start ? (start, end) : null;
    }

    // Rectangles (in `target` coordinates) covering the selection.
    public List<Rect> Rects(PreviewPosition from, PreviewPosition to, Visual target)
    {
        var rects = new List<Rect>();
        if (from > to) (from, to) = (to, from);
        for (var i = from.Item; i <= to.Item && i < Items.Count; i++)
        {
            if (RangeOf(i, from, to) is not { } range || !Items[i].IsVisible || Items[i].TranslatePoint(new Point(0, 0), target) is not { } at) continue;
            if (Items[i] is TextBlock tb)
                foreach (var r in tb.TextLayout.HitTestTextRange(range.Start, range.End - range.Start))
                    rects.Add(new Rect(at.X + tb.Padding.Left + r.X, at.Y + tb.Padding.Top + r.Y, Math.Max(r.Width, 3), r.Height));
            else rects.Add(new Rect(at, Items[i].Bounds.Size));
        }
        return rects;
    }

    // ---- Copy: the selected part of the rendered tree ----
    public CopyNode Build(PreviewPosition from, PreviewPosition to)
    {
        if (from > to) (from, to) = (to, from);
        var doc = new CopyNode("doc");
        Build(_root, doc, from, to);
        return doc;
    }

    private void Build(Control control, CopyNode parent, PreviewPosition from, PreviewPosition to)
    {
        var kind = PreviewTags.Kind(control);
        if (kind == "marker") return;
        var index = IndexOf(control);
        if (index >= 0)
        {
            if (RangeOf(index, from, to) is not { } range) return;
            if (control is TextBlock tb)
            {
                var node = new CopyNode(kind is null or "html" ? "p" : kind);
                var pos = 0;
                if (tb.Inlines is { Count: > 0 } inlines) Inlines(inlines, node, ref pos, range.Start, range.End, node.Kind == "pre");
                else if (tb.Text is { } text) node.Children.Add(new CopyNode("text") { Text = text[range.Start..range.End] });
                if (node.Children.Count > 0) parent.Children.Add(node);
            }
            else if (control is Image { Tag: string path }) parent.Children.Add(new CopyNode("img") { Text = new Uri(path).AbsoluteUri, Href = "" });
            else if (kind == "hr") parent.Children.Add(new CopyNode("hr"));
            return;
        }
        var children = control switch { Panel p => p.Children.ToList(), Decorator { Child: { } c } => [c], _ => new List<Control>() };
        if (kind is null) { foreach (var child in children) Build(child, parent, from, to); return; }
        var container = new CopyNode(kind) { Start = kind == "ol" && int.TryParse(PreviewTags.Value(control), out var start) ? start : 1, Row = Grid.GetRow(control) };
        foreach (var child in children) Build(child, container, from, to);
        if (container.Children.Count == 0) return;
        if (kind == "table")
        {
            var table = new CopyNode("table");
            foreach (var row in container.Children.GroupBy(c => c.Row).OrderBy(g => g.Key))
            {
                var tr = new CopyNode("tr");
                tr.Children.AddRange(row);
                table.Children.Add(tr);
            }
            container = table;
        }
        parent.Children.Add(container);
    }

    // Adjacent pieces of one run (split by the source-selection highlight) merge back into one node.
    private static void Append(CopyNode into, CopyNode node)
    {
        if (into.Children.Count > 0 && into.Children[^1] is var last && last.Kind == node.Kind && last.Href == node.Href)
        {
            var (a, b) = node.Kind == "text" ? (last, node) : last.Children is [{ Kind: "text" } x] && node.Children is [{ Kind: "text" } y] ? (x, y) : (null, null);
            if (a is not null && b is not null)
            {
                var merged = new CopyNode("text") { Text = a.Text + b.Text };
                if (node.Kind == "text") into.Children[^1] = merged; else last.Children[0] = merged;
                return;
            }
        }
        into.Children.Add(node);
    }

    private static void Inlines(InlineCollection inlines, CopyNode into, ref int pos, int start, int end, bool pre)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    var text = run.Text ?? "";
                    var (s, e) = (Math.Max(start, pos), Math.Min(end, pos + text.Length));
                    if (e > s)
                    {
                        var node = new CopyNode("text") { Text = text[(s - pos)..(e - pos)] };
                        var kind = pre ? null : PreviewTags.Kind(run);
                        if (kind is "code" or "a")
                        {
                            var wrap = new CopyNode(kind) { Href = PreviewTags.Value(run) };
                            wrap.Children.Add(node);
                            node = wrap;
                        }
                        Append(into, node);
                    }
                    pos += text.Length;
                    break;
                case LineBreak:
                    if (pos >= start && pos < end) into.Children.Add(new CopyNode("br"));
                    pos += LineBreakLength;
                    break;
                case Span span:
                    var spanKind = PreviewTags.Kind(span);
                    var target = spanKind is null ? into : new CopyNode(spanKind) { Href = PreviewTags.Value(span) };
                    Inlines(span.Inlines, target, ref pos, start, end, pre);
                    if (target != into && target.Children.Count > 0) into.Children.Add(target);
                    break;
                case InlineUIContainer { Child: Image { Tag: string path } }:
                    if (pos >= start && pos < end) into.Children.Add(new CopyNode("img") { Text = new Uri(path).AbsoluteUri });
                    pos += 1;
                    break;
                default: pos += 1; break;
            }
        }
    }
}