using System.Text;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Mdhub.Rendering;

// The shape of a Markdown document without its text: blocks, inline formatting, links, breaks.
// A preview edit is only applied when the shape stays the same.
public static class MarkdownStructure
{
    public static string Signature(string markdown)
    {
        var sb = new StringBuilder();
        Block(MarkdownPreview.Parse(markdown), sb);
        return sb.ToString();
    }

    public static Func<string, bool> SameAs(string source)
    {
        var expected = Signature(source);
        return edited => Signature(edited) == expected;
    }

    private static void Block(Block block, StringBuilder sb)
    {
        sb.Append(block.GetType().Name);
        switch (block)
        {
            case HeadingBlock h: sb.Append(h.Level); break;
            case ListBlock l: sb.Append(l.IsOrdered).Append(l.BulletType); break;
            case FencedCodeBlock f: sb.Append(f.FencedChar).Append(f.OpeningFencedCharCount).Append(f.Info); break;
            case TableCell c: sb.Append(c.ColumnIndex); break;
        }
        if (block is ContainerBlock container)
        {
            sb.Append('(');
            foreach (var child in container) Block(child, sb);
            sb.Append(')');
        }
        else if (block is LeafBlock { Inline: { } inline })
        {
            sb.Append('{');
            Inline(inline, sb);
            sb.Append('}');
        }
        sb.Append(';');
    }

    private static void Inline(ContainerInline container, StringBuilder sb)
    {
        foreach (var item in container)
        {
            switch (item)
            {
                case LiteralInline: continue; // text (including escapes) is free to change
                case LineBreakInline br: sb.Append(br.IsHard ? "BR" : "br"); break;
                case EmphasisInline em: sb.Append(em.DelimiterChar).Append(em.DelimiterCount); break;
                case LinkInline link: sb.Append(link.IsImage ? "img" : "link").Append('<').Append(link.Url).Append('>'); break;
                default: sb.Append(item.GetType().Name); break;
            }
            if (item is ContainerInline nested) { sb.Append('('); Inline(nested, sb); sb.Append(')'); }
            sb.Append(',');
        }
    }
}
