using System.Security.Cryptography;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Markdig.Syntax;
using Markdig.Extensions.Tables;
using Settings = Mdhub.Services.PreviewSettings;

namespace Mdhub.Rendering;

public static partial class MarkdownPreview
{
    // Theme, typography, and behaviour that change how a block is painted. Included in the
    // per-block key so a missed full-rebuild still replaces every control.
    internal static string RenderFingerprint(string? baseDirectory, bool styleInlineCode)
    {
        var s = Settings.Current;
        var b = Mdhub.Services.EditorBehavior.Current;
        return string.Join("|",
            Mdhub.Services.ThemeService.Current,
            s.BodyFamily ?? "",
            s.BodySize.ToString("G17"),
            s.CodeFamily,
            s.CodeSize.ToString("G17"),
            s.LineHeight.ToString("G17"),
            s.ParagraphSpacing.ToString("G17"),
            s.BulletSpacing.ToString("G17"),
            s.OrderedSpacing.ToString("G17"),
            b.HtmlFormatting,
            b.CodeSyntaxHighlighting,
            styleInlineCode,
            baseDirectory ?? "");
    }

    internal static string BlockKey(Block block, string source, string fingerprint)
    {
        var start = Math.Max(0, block.Span.Start);
        var end = Math.Min(source.Length, Math.Max(start, block.Span.End + 1));
        var kind = BlockKind(block);
        var bytes = Encoding.UTF8.GetBytes(fingerprint + "\n" + kind + "\n");
        var slice = start < end ? Encoding.UTF8.GetBytes(source[start..end]) : [];
        var data = new byte[bytes.Length + slice.Length];
        Buffer.BlockCopy(bytes, 0, data, 0, bytes.Length);
        if (slice.Length > 0) Buffer.BlockCopy(slice, 0, data, bytes.Length, slice.Length);
        return Convert.ToHexString(SHA256.HashData(data));
    }

    static string BlockKind(Block block) => block switch
    {
        HeadingBlock heading => "h" + heading.Level,
        ListBlock list => (list.IsOrdered ? "ol:" + (list.OrderedStart ?? "1") : "ul"),
        FencedCodeBlock fence => "fence:" + (fence.Info ?? ""),
        CodeBlock => "code",
        QuoteBlock => "quote",
        Table => "table",
        ThematicBreakBlock => "hr",
        HtmlBlock => "html",
        ParagraphBlock => "p",
        _ => block.GetType().Name,
    };

    static void FillTopLevels(PreviewRender result, IEnumerable<Block> blocks, string source, string fingerprint)
    {
        foreach (var block in blocks)
        {
            var blockFrom = result.Map.Blocks.Count;
            var segmentFrom = result.Map.Segments.Count;
            var controls = RenderBlock(block).ToList();
            result.Controls.AddRange(controls);
            result.TopLevels.Add(PreviewTopLevel.From(result, BlockKey(block, source, fingerprint), Math.Max(0, block.Span.Start), blockFrom, segmentFrom, controls));
        }
    }

    // Diff previous top-level controls against a new parse and patch host in place.
    // Returns false when the previous tree cannot be reused safely (caller should full-rebuild).
    public static bool TryApplyMapped(PreviewRender previous, IList<Control> host, ContainerBlock document, string source, string? baseDirectory, bool styleInlineCode, out PreviewRender updated)
    {
        updated = previous;
        if (previous.TopLevels.Count == 0) return false;
        if (previous.Controls.Count != host.Count) return false;
        for (var i = 0; i < host.Count; i++)
            if (!ReferenceEquals(previous.Controls[i], host[i])) return false;

        var fingerprint = RenderFingerprint(baseDirectory, styleInlineCode);
        var next = new List<(Block Block, string Key, int Start)>();
        foreach (var block in document)
            next.Add((block, BlockKey(block, source, fingerprint), Math.Max(0, block.Span.Start)));

        var old = previous.TopLevels;
        var prefix = 0;
        var suffix = 0;
        var max = Math.Min(old.Count, next.Count);
        while (prefix < max && old[prefix].Key == next[prefix].Key) prefix++;
        while (suffix < max - prefix && old[old.Count - 1 - suffix].Key == next[next.Count - 1 - suffix].Key) suffix++;

        var middleCount = next.Count - prefix - suffix;
        PreviewRender? middle = null;
        if (middleCount > 0)
        {
            var blocks = new List<Block>(middleCount);
            for (var i = 0; i < middleCount; i++) blocks.Add(next[prefix + i].Block);
            middle = RenderSubset(blocks, source, baseDirectory, styleInlineCode);
        }

        var merged = new PreviewRender(new PreviewSourceMap(source));
        for (var i = 0; i < prefix; i++)
            if (!old[i].TryAppendTo(merged, next[i].Start)) return false;
        if (middle is not null)
        {
            foreach (var top in middle.TopLevels)
                if (!top.TryAppendTo(merged, top.SourceStart)) return false;
        }
        for (var i = 0; i < suffix; i++)
        {
            var ni = next.Count - suffix + i;
            if (!old[old.Count - suffix + i].TryAppendTo(merged, next[ni].Start)) return false;
        }

        var removeAt = 0;
        for (var i = 0; i < prefix; i++) removeAt += old[i].Controls.Count;
        var removeCount = 0;
        for (var i = prefix; i < old.Count - suffix; i++) removeCount += old[i].Controls.Count;
        for (var i = 0; i < removeCount; i++) host.RemoveAt(removeAt);
        if (middle is not null)
        {
            for (var i = 0; i < middle.Controls.Count; i++)
                host.Insert(removeAt + i, middle.Controls[i]);
        }

        if (merged.Controls.Count != host.Count) return false;
        for (var i = 0; i < host.Count; i++)
            if (!ReferenceEquals(merged.Controls[i], host[i])) return false;

        merged.ReusedCount = prefix + suffix;
        merged.ReplacedCount = old.Count - prefix - suffix;
        merged.InsertedCount = middleCount;
        updated = merged;
        return true;
    }

    static PreviewRender RenderSubset(IReadOnlyList<Block> blocks, string source, string? baseDirectory, bool styleInlineCode)
    {
        var previous = (_baseDirectory, _result, _block, _fallback);
        var result = new PreviewRender(new PreviewSourceMap(source));
        var previousInline = _suppressInlineCode;
        _suppressInlineCode = !styleInlineCode;
        (_baseDirectory, _result, _block, _fallback) = (baseDirectory, result, -1, -1);
        try
        {
            FillTopLevels(result, blocks, source, RenderFingerprint(baseDirectory, styleInlineCode));
            return result;
        }
        finally
        {
            (_baseDirectory, _result, _block, _fallback) = previous;
            _suppressInlineCode = previousInline;
        }
    }
}

// One top-level preview block (occasionally several controls, e.g. an image-only paragraph)
// plus the source-map entries that belong to it, so a reused control can be rebased when lines shift.
public sealed class PreviewTopLevel
{
    public required string Key { get; init; }
    public required int SourceStart { get; init; }
    public required List<Control> Controls { get; init; }
    public required List<PreviewSourceMap.BlockEntry> Blocks { get; init; }
    public required List<PreviewSourceMap.Segment> Segments { get; init; }
    public required Dictionary<int, Control> BlockControls { get; init; }
    public required Dictionary<int, (InlineCollection Parent, Run Run)> Runs { get; init; }
    public required HashSet<int> CodeSegments { get; init; }
    public required Dictionary<int, (int Start, int End)> CodeContent { get; init; }

    internal static PreviewTopLevel From(PreviewRender result, string key, int sourceStart, int blockFrom, int segmentFrom, List<Control> controls)
    {
        var blocks = result.Map.Blocks.Skip(blockFrom).ToList();
        var segments = result.Map.Segments.Skip(segmentFrom).ToList();
        var blockIds = blocks.Select(b => b.Id).ToHashSet();
        var segmentIds = segments.Select(s => s.Id).ToHashSet();
        return new PreviewTopLevel
        {
            Key = key,
            SourceStart = sourceStart,
            Controls = controls,
            Blocks = blocks,
            Segments = segments,
            BlockControls = result.Blocks.Where(kv => blockIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            Runs = result.Runs.Where(kv => segmentIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
            CodeSegments = result.CodeSegments.Where(segmentIds.Contains).ToHashSet(),
            CodeContent = result.CodeContent.Where(kv => blockIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value),
        };
    }

    internal bool TryAppendTo(PreviewRender dest, int newSourceStart)
    {
        var delta = newSourceStart - SourceStart;
        var source = dest.Map.Source;
        var idMap = new Dictionary<int, int>(Blocks.Count);
        var newBlocks = new List<PreviewSourceMap.BlockEntry>(Blocks.Count);
        var newBlockControls = new Dictionary<int, Control>(BlockControls.Count);
        foreach (var block in Blocks)
        {
            if (block.FallbackId >= 0 && !idMap.ContainsKey(block.FallbackId)) return false;
            var fallback = block.FallbackId >= 0 ? idMap[block.FallbackId] : -1;
            var newId = dest.Map.AddBlock(block.Start + delta, block.End + delta, fallback, block.IsContainer);
            idMap[block.Id] = newId;
            if (BlockControls.TryGetValue(block.Id, out var control))
                dest.Blocks[newId] = newBlockControls[newId] = control;
            newBlocks.Add(dest.Map.Blocks[newId]);
        }

        var newSegments = new List<PreviewSourceMap.Segment>(Segments.Count);
        var newRuns = new Dictionary<int, (InlineCollection Parent, Run Run)>(Runs.Count);
        var newCodeSegments = new HashSet<int>();
        foreach (var segment in Segments)
        {
            var start = segment.Start + delta;
            if (start < 0 || start + segment.Length > source.Length) return false;
            if (!idMap.TryGetValue(segment.BlockId, out var newBlockId)) return false;
            var text = source.Substring(start, segment.Length);
            var newId = dest.Map.AddSegment(newBlockId, start, text);
            if (newId < 0) return false;
            newSegments.Add(dest.Map.Segments[newId]);
            if (Runs.TryGetValue(segment.Id, out var run))
                dest.Runs[newId] = newRuns[newId] = run;
            if (CodeSegments.Contains(segment.Id))
            {
                dest.CodeSegments.Add(newId);
                newCodeSegments.Add(newId);
            }
        }

        var newCodeContent = new Dictionary<int, (int Start, int End)>(CodeContent.Count);
        foreach (var (id, range) in CodeContent)
        {
            if (!idMap.TryGetValue(id, out var newId)) return false;
            var shifted = (range.Start + delta, range.End + delta);
            dest.CodeContent[newId] = shifted;
            newCodeContent[newId] = shifted;
        }

        dest.TopLevels.Add(new PreviewTopLevel
        {
            Key = Key,
            SourceStart = newSourceStart,
            Controls = Controls,
            Blocks = newBlocks,
            Segments = newSegments,
            BlockControls = newBlockControls,
            Runs = newRuns,
            CodeSegments = newCodeSegments,
            CodeContent = newCodeContent,
        });
        dest.Controls.AddRange(Controls);
        return true;
    }
}