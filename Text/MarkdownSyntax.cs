using Markdig.Syntax;
using Mdhub.Rendering;

namespace Mdhub.Text;

public enum MarkdownMarkerKind { Heading, OrderedList, UnorderedList }
public readonly record struct MarkdownMarker(int Start, int Length, MarkdownMarkerKind Kind);

public static class MarkdownSyntax
{
    public static IReadOnlyList<MarkdownMarker> Markers(string source)
    {
        var markers = new List<MarkdownMarker>();
        void Walk(ContainerBlock container)
        {
            foreach (var block in container)
            {
                var start = block.Span.Start;
                if (start < 0 || start >= source.Length) continue;
                if (block is HeadingBlock && source[start] == '#')
                {
                    var end = start;
                    while (end < source.Length && source[end] == '#') end++;
                    markers.Add(new(start, end - start, MarkdownMarkerKind.Heading));
                }
                if (block is ListItemBlock)
                {
                    if (source[start] is '-' or '*' or '+') markers.Add(new(start, 1, MarkdownMarkerKind.UnorderedList));
                    else
                    {
                        var end = start;
                        while (end < source.Length && char.IsAsciiDigit(source[end])) end++;
                        if (end > start && end < source.Length && source[end] is '.' or ')')
                            markers.Add(new(start, end - start + 1, MarkdownMarkerKind.OrderedList));
                    }
                }
                if (block is ContainerBlock nested) Walk(nested);
            }
        }
        Walk(MarkdownPreview.Parse(source));
        return markers.OrderBy(m => m.Start).ToArray();
    }
}
