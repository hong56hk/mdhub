namespace Mdhub.Text;

// Item 72: move the current/selected lines up or down (Alt+Up/Down) and duplicate them below (Ctrl+D).
// Pure text logic: each edit is one replacement (Start, Length -> Text) so the editor applies it as a single undo
// step, plus where the anchor and caret land afterwards. Line endings stay where they were (each line slot keeps
// its terminator), so CRLF/LF/CR and a last line without a newline are preserved.
public readonly record struct LineEdit(int Start, int Length, string Text, int Anchor, int Caret);

public static class LineEdits
{
    // One logical line: [Start, ContentEnd) is its text, [ContentEnd, End) its terminator ("" on the last line).
    public readonly record struct Line(int Start, int ContentEnd, int End);

    public static List<Line> Lines(string text)
    {
        var lines = new List<Line>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            var end = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? i + 2 : i + 1;
            lines.Add(new Line(start, i, end));
            start = end; i = end - 1;
        }
        lines.Add(new Line(start, text.Length, text.Length));
        return lines;
    }

    static int LineOf(List<Line> lines, int offset)
    {
        for (var k = 0; k < lines.Count - 1; k++) if (offset < lines[k].End) return k;
        return lines.Count - 1;
    }

    // The lines the selection touches; a selection ending at the very start of a line does not include that line.
    public static (int First, int Last) Block(string text, int anchor, int caret)
    {
        var lines = Lines(text);
        int s = Math.Clamp(Math.Min(anchor, caret), 0, text.Length), e = Math.Clamp(Math.Max(anchor, caret), 0, text.Length);
        var first = LineOf(lines, s); var last = LineOf(lines, e);
        if (e > s && last > first && lines[last].Start == e) last--;
        return (first, last);
    }

    // Moves the block one line up (direction -1) or down (+1); null at the top/bottom of the text.
    public static LineEdit? Move(string text, int anchor, int caret, int direction)
    {
        var lines = Lines(text);
        var (first, last) = Block(text, anchor, caret);
        var from = direction < 0 ? first - 1 : first; var to = direction < 0 ? last : last + 1;
        if (from < 0 || to >= lines.Count) return null;
        string Content(int k) => text[lines[k].Start..lines[k].ContentEnd];
        string Term(int k) => text[lines[k].ContentEnd..lines[k].End];
        var order = new List<int>();
        if (direction < 0) { for (var k = first; k <= last; k++) order.Add(k); order.Add(first - 1); }
        else { order.Add(last + 1); for (var k = first; k <= last; k++) order.Add(k); }
        var sb = new System.Text.StringBuilder();
        for (var slot = 0; slot < order.Count; slot++) sb.Append(Content(order[slot])).Append(Term(from + slot));
        var replacement = sb.ToString();
        var result = string.Concat(text.AsSpan(0, lines[from].Start), replacement, text.AsSpan(lines[to].End));
        var moved = Lines(result);
        // Same line/column, one line further; a column past the content (the selection end at a line start) stays put.
        int Map(int offset)
        {
            offset = Math.Clamp(offset, 0, text.Length);
            var k = LineOf(lines, offset);
            if (k < first || k > last)
            {
                // Only the excluded end line (selection ends at its start) can be outside the block.
                var target = k + direction;
                return target >= moved.Count ? result.Length : moved[target].Start;
            }
            var line = moved[k + direction];
            return Math.Min(line.Start + (offset - lines[k].Start), line.ContentEnd);
        }
        return new LineEdit(lines[from].Start, lines[to].End - lines[from].Start, replacement, Map(anchor), Map(caret));
    }

    // Inserts a copy of the block right below it; the selection moves onto the copy with the same columns.
    // Item 68: newline is the document's line ending, used when the last line has no terminator.
    public static LineEdit Duplicate(string text, int anchor, int caret, string? newline = null)
    {
        var lines = Lines(text);
        var (first, last) = Block(text, anchor, caret);
        var block = text[lines[first].Start..lines[last].End];
        var lastTerm = text[lines[last].ContentEnd..lines[last].End];
        int insertAt = lines[last].End, shift;
        string inserted;
        if (lastTerm.Length > 0) { inserted = block; shift = block.Length; }
        else
        {
            // The last line has no newline: separate the copy with the text's own line ending.
            var eol = newline ?? (lines.Count > 1 ? text[lines[0].ContentEnd..lines[0].End] : Environment.NewLine);
            inserted = eol + block; shift = block.Length + eol.Length;
        }
        int Map(int offset) => Math.Clamp(offset, 0, text.Length) + shift;
        return new LineEdit(insertAt, 0, inserted, Map(anchor), Map(caret));
    }
}