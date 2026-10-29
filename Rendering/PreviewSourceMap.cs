namespace Mdhub.Rendering;

// Maps an editor selection (source offsets) to preview pieces. Blocks are rendered elements with a source span;
// segments are rendered text runs whose characters match the source one-to-one. Pure, so it is unit tested.
public sealed class PreviewSourceMap(string source)
{
    public readonly record struct BlockEntry(int Id, int Start, int End, int FallbackId, bool IsContainer);
    public readonly record struct Segment(int Id, int BlockId, int Start, int Length);
    // Exact: highlight Length characters of segment SegmentId from Offset. Otherwise highlight block BlockId whole.
    public readonly record struct Highlight(int BlockId, int SegmentId, int Offset, int Length, bool WholeBlock);

    private readonly List<BlockEntry> _blocks = [];
    private readonly List<Segment> _segments = [];
    public string Source { get; } = source;
    public IReadOnlyList<BlockEntry> Blocks => _blocks;
    public IReadOnlyList<Segment> Segments => _segments;

    // End is exclusive. A container (a table) is highlighted whole when its markup is selected.
    public int AddBlock(int start, int end, int fallbackId = -1, bool isContainer = false)
    {
        var id = _blocks.Count;
        _blocks.Add(new BlockEntry(id, Math.Max(0, start), Math.Min(Source.Length, Math.Max(start, end)), fallbackId, isContainer));
        return id;
    }

    // Returns the segment id, or -1 when the rendered text is not a verbatim copy of the source range.
    public int AddSegment(int blockId, int start, string renderedText)
    {
        if (renderedText.Length == 0 || start < 0 || start + renderedText.Length > Source.Length
            || string.CompareOrdinal(Source, start, renderedText, 0, renderedText.Length) != 0) return -1;
        var id = _segments.Count;
        _segments.Add(new Segment(id, blockId, start, renderedText.Length));
        return id;
    }

    // The rendered block for a source line [lineStart, lineEnd): a containing table wins over its cells;
    // otherwise the first leaf block overlapping the line. -1 for blank lines between blocks.
    public int BlockForLine(int lineStart, int lineEnd)
    {
        var end = Math.Max(lineEnd, lineStart + 1);
        bool Hits(BlockEntry b) => b.Start < end && b.End > lineStart;
        foreach (var b in _blocks) if (b.IsContainer && Hits(b)) return b.Id;
        foreach (var b in _blocks) if (!b.IsContainer && Hits(b)) return b.Id;
        return -1;
    }

    public IReadOnlyList<Highlight> Map(int selectionStart, int selectionEnd)
    {
        var s = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, Source.Length);
        var e = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, Source.Length);
        var result = new List<Highlight>();
        if (s == e) return result;
        var wholeContainers = new HashSet<int>();
        foreach (var container in _blocks.Where(b => b.IsContainer && b.Start < e && b.End > s))
            if (!Covered(Math.Max(s, container.Start), Math.Min(e, container.End), _segments))
            {
                wholeContainers.Add(container.Id);
                result.Add(new Highlight(container.Id, -1, 0, 0, true));
            }
        var wholeAdded = new HashSet<int>(wholeContainers);
        foreach (var block in _blocks.Where(b => !b.IsContainer && b.Start < e && b.End > s))
        {
            if (block.FallbackId >= 0 && wholeContainers.Contains(block.FallbackId)) continue;
            var ps = Math.Max(s, block.Start);
            var pe = Math.Min(e, block.End);
            var segments = _segments.Where(g => g.BlockId == block.Id && g.Start < pe && g.Start + g.Length > ps).ToList();
            if (segments.Count > 0 && Covered(ps, pe, segments))
            {
                foreach (var g in segments)
                {
                    var from = Math.Max(ps, g.Start);
                    var to = Math.Min(pe, g.Start + g.Length);
                    result.Add(new Highlight(block.Id, g.Id, from - g.Start, to - from, false));
                }
                continue;
            }
            var whole = block.FallbackId >= 0 ? block.FallbackId : block.Id;
            if (wholeAdded.Add(whole)) result.Add(new Highlight(whole, -1, 0, 0, true));
        }
        return result;
    }

    // Every character in [start, end) is rendered text or plain whitespace between pieces (line breaks, spaces).
    private bool Covered(int start, int end, IEnumerable<Segment> segments)
    {
        var covered = new bool[end - start];
        foreach (var g in segments)
            for (var i = Math.Max(start, g.Start); i < Math.Min(end, g.Start + g.Length); i++) covered[i - start] = true;
        for (var i = 0; i < covered.Length; i++)
            if (!covered[i] && Source[start + i] is not (' ' or '\t' or '\r' or '\n')) return false;
        return true;
    }
}