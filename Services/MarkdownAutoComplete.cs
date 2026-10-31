using System.Text;
using System.Text.RegularExpressions;
using Mdhub.Text;

namespace Mdhub.Services;

// Item 71: Markdown list and table auto-complete, UI-free. Every rule returns one CaretEdit (one replacement on the
// original text plus the new caret) or null for the editor's default behaviour. Nothing applies inside fenced code.
public static partial class MarkdownAutoComplete
{
    public sealed record Options(bool Lists = true, bool Tables = true, bool Align = true, bool InsertSpaces = true, int TabSize = 4);

    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>[-*+])(?<gap>[ \t]+)(?<task>\[[ xX]\][ \t]+)?")]
    private static partial Regex BulletRegex();
    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<number>\d{1,9})(?<delim>[.)])(?<gap>[ \t]+)")]
    private static partial Regex NumberRegex();

    // ---- Lines ----
    public readonly record struct Line(int Start, int End, int Next)
    {
        public int Length => End - Start;
    }
    public static Line LineAt(string text, int index)
    {
        var start = TabEditing.LineStart(text, Math.Clamp(index, 0, text.Length));
        var end = TabEditing.LineEnd(text, start);
        var next = end;
        if (next < text.Length && text[next] == '\r') next++;
        if (next < text.Length && text[next] == '\n') next++;
        return new Line(start, end, next);
    }
    private static Line? NextLine(string text, Line line) => line.Next > line.End ? LineAt(text, line.Next) : null;
    private static Line? PreviousLine(string text, Line line) => line.Start == 0 ? null : LineAt(text, line.Start - 1);
    private static string Str(string text, Line l) => text.Substring(l.Start, l.Length);
    public static string NewLineOf(string text) => text.Contains("\r\n") ? "\r\n" : "\n";

    // ---- Fenced code ----
    public static bool InFence(string text, int index)
    {
        var target = TabEditing.LineStart(text, Math.Clamp(index, 0, text.Length));
        char fence = '\0'; var fenceLength = 0;
        for (var i = 0; i < target;)
        {
            var l = LineAt(text, i);
            var s = Str(text, l);
            var t = s.TrimStart(' ');
            if (s.Length - t.Length <= 3 && t.Length >= 3 && (t[0] == '`' || t[0] == '~'))
            {
                var n = 0; while (n < t.Length && t[n] == t[0]) n++;
                if (n >= 3)
                {
                    if (fence == '\0') { fence = t[0]; fenceLength = n; }
                    else if (t[0] == fence && n >= fenceLength && t[n..].Trim().Length == 0) fence = '\0';
                }
            }
            if (l.Next <= i) break;
            i = l.Next;
        }
        return fence != '\0';
    }

    // ---- List items ----
    public sealed record ListItem(string Indent, string Marker, string Gap, string Task, int? Number, char Delim, int PrefixLength)
    {
        public bool Ordered => Number is not null;
    }
    public static ListItem? ParseItem(string line)
    {
        var m = NumberRegex().Match(line);
        if (m.Success)
            return new ListItem(m.Groups["indent"].Value, m.Groups["number"].Value + m.Groups["delim"].Value, m.Groups["gap"].Value, "",
                int.Parse(m.Groups["number"].Value), m.Groups["delim"].Value[0], m.Length);
        m = BulletRegex().Match(line);
        if (m.Success && !IsThematicBreak(line))
            return new ListItem(m.Groups["indent"].Value, m.Groups["marker"].Value, m.Groups["gap"].Value, m.Groups["task"].Value, null, '\0', m.Length);
        return null;
    }
    private static bool IsThematicBreak(string line)
    {
        var t = line.Trim();
        return t.Length >= 3 && (t[0] is '-' or '*' or '_') && t.All(c => c == t[0] || c == ' ');
    }
    private static int IndentWidth(string indent, int tabSize) => TabEditing.Column(indent, indent.Length, tabSize);
    private static string OutdentOnce(string indent, int tabSize) => indent[TabEditing.OutdentLength(indent, 0, tabSize)..];

    // ---- Tables ----
    public static bool IsTableRow(string line) { var t = line.Trim(); return t.Length > 1 && t[0] == '|'; }
    public static bool IsDelimiterRow(string line) => IsTableRow(line) && Cells(line).All(c => Regex.IsMatch(c.Trim(), @"^:?-+:?$"));
    // Cells between the outer pipes (escaped \| stays in its cell; pipes inside `code` are kept).
    public static List<string> Cells(string line)
    {
        var t = line.Trim();
        var cells = new List<string>();
        var sb = new StringBuilder();
        var code = false;
        for (var i = 1; i < t.Length; i++)
        {
            var c = t[i];
            if (c == '\\' && i + 1 < t.Length) { sb.Append(c).Append(t[++i]); continue; }
            if (c == '`') code = !code;
            if (c == '|' && !code) { cells.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        if (sb.ToString().Trim().Length > 0) cells.Add(sb.ToString());
        return cells;
    }
    // Offsets (in the line) of the pipes that separate cells.
    private static List<int> Pipes(string line)
    {
        var list = new List<int>(); var code = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\') { i++; continue; }
            if (line[i] == '`') code = !code;
            if (line[i] == '|' && !code) list.Add(i);
        }
        return list;
    }

    // East Asian wide / fullwidth characters take two columns; combining marks none.
    public static int DisplayWidth(string s)
    {
        var w = 0;
        foreach (var r in s.EnumerateRunes())
        {
            var cat = Rune.GetUnicodeCategory(r);
            if (cat is System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.EnclosingMark or System.Globalization.UnicodeCategory.Format) continue;
            w += IsWide(r.Value) ? 2 : 1;
        }
        return w;
    }
    public static bool IsWide(int c) =>
        c is >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0x303E or >= 0x3041 and <= 0x33FF or >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF
        or >= 0xA000 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF or >= 0xFE30 and <= 0xFE4F or >= 0xFF00 and <= 0xFF60
        or >= 0xFFE0 and <= 0xFFE6 or >= 0x1F300 and <= 0x1F64F or >= 0x1F900 and <= 0x1F9FF or >= 0x20000 and <= 0x3FFFD;

    private static string EmptyRow(int cells) => "|" + string.Concat(Enumerable.Repeat("  |", Math.Max(1, cells)));

    // ---- Enter ----
    public static CaretEdit? Enter(string text, int caret, Options o, string? newLine = null)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        if (InFence(text, caret)) return null;
        var nl = newLine ?? NewLineOf(text);
        var line = LineAt(text, caret);
        var s = Str(text, line);
        if (o.Tables && IsTableRow(s) && TableEnter(text, line, s, nl) is { } t) return t;
        if (o.Lists && ParseItem(s) is { } item && caret - line.Start >= item.PrefixLength)
        {
            var content = s[item.PrefixLength..];
            if (content.Trim().Length == 0)
            {
                // Empty item: outdent one level, or end the list (marker removed, the line left empty).
                if (item.Indent.Length > 0)
                {
                    var indent = OutdentOnce(item.Indent, o.TabSize);
                    var marker = item.Ordered ? NumberAt(text, line, indent, item) + item.Delim.ToString() : item.Marker;
                    var repl = indent + marker + item.Gap + (item.Task.Length > 0 ? "[ ] " : "");
                    return Renumbered(text, new CaretEdit(line.Start, line.End, repl, line.Start + repl.Length, line.Start + repl.Length), o);
                }
                return new CaretEdit(line.Start, line.End, "", line.Start, line.Start);
            }
            var nextMarker = item.Ordered ? (item.Number + 1) + item.Delim.ToString() : item.Marker;
            var gap = item.Gap.Contains('\t') ? item.Gap : " ";
            var insert = nl + item.Indent + nextMarker + gap + (item.Task.Length > 0 ? "[ ] " : "");
            var at = caret;
            // Trailing spaces before the caret stay; the rest of the line moves to the new item.
            var edit = new CaretEdit(at, at, insert, at + insert.Length, at + insert.Length);
            return item.Ordered ? Renumbered(text, edit, o) : edit;
        }
        return null;
    }

    // The number an item gets at `indent`: one more than the previous sibling at that indent, else 1.
    private static int NumberAt(string text, Line line, string indent, ListItem item)
    {
        for (var p = PreviousLine(text, line); p is { } prev; p = PreviousLine(text, prev))
        {
            var ps = Str(text, prev);
            if (ps.Trim().Length == 0) break;
            if (ParseItem(ps) is { } it)
            {
                if (it.Indent == indent) return it.Ordered && it.Delim == item.Delim ? it.Number!.Value + 1 : 1;
                if (it.Indent.Length < indent.Length) return 1;
            }
            else if (ps.Length - ps.TrimStart().Length < indent.Length + 1 && indent.Length == 0) break;
        }
        return 1;
    }

    private static CaretEdit? TableEnter(string text, Line line, string s, string nl)
    {
        var cells = Math.Max(1, Cells(s).Count);
        var prev = PreviousLine(text, line);
        var next = NextLine(text, line);
        var isHeader = (prev is null || !IsTableRow(Str(text, prev.Value))) && (next is null || !IsDelimiterRow(Str(text, next.Value)));
        if (IsDelimiterRow(s) && prev is { } hp && IsTableRow(Str(text, hp))) isHeader = false;
        if (isHeader && !IsDelimiterRow(s))
        {
            var delim = "|" + string.Concat(Enumerable.Repeat(" --- |", cells));
            var insert = nl + delim + nl + EmptyRow(cells);
            var caretAt = line.End + nl.Length + delim.Length + nl.Length + 2;
            return new CaretEdit(line.End, line.End, insert, caretAt, caretAt);
        }
        var row = nl + EmptyRow(cells);
        var c = line.End + nl.Length + 2;
        return new CaretEdit(line.End, line.End, row, c, c);
    }

    // ---- Tab / Shift+Tab ----
    public static CaretEdit? Tab(string text, int selectionStart, int selectionEnd, bool shift, Options o, string? newLine = null)
    {
        var start = Math.Min(selectionStart, selectionEnd); var end = Math.Max(selectionStart, selectionEnd);
        if (text.AsSpan(start, end - start).IndexOf('\n') >= 0 || InFence(text, start)) return null;
        var line = LineAt(text, start);
        var s = Str(text, line);
        if (o.Tables && IsTableRow(s)) return CellMove(text, line, s, start, shift, newLine ?? NewLineOf(text));
        if (o.Lists && ParseItem(s) is { } item)
        {
            string indent;
            if (shift) { if (item.Indent.Length == 0) return null; indent = OutdentOnce(item.Indent, o.TabSize); }
            else indent = TabEditing.Unit(o.InsertSpaces, o.TabSize) + item.Indent;
            var marker = item.Marker;
            if (item.Ordered) marker = (shift ? NumberAt(text, line, indent, item) : FirstNumberBelow(text, line, indent, item)) + item.Delim.ToString();
            var head = indent + marker + item.Gap;
            var repl = head + s[(item.Indent.Length + item.Marker.Length + item.Gap.Length)..];
            var delta = repl.Length - s.Length;
            var caretInLine = start - line.Start;
            var oldHead = item.Indent.Length + item.Marker.Length + item.Gap.Length;
            var newCaret = line.Start + (caretInLine >= oldHead ? caretInLine + delta : Math.Min(caretInLine, head.Length));
            var newEnd = end == start ? newCaret : Math.Clamp(end + delta, line.Start, line.Start + repl.Length);
            return Renumbered(text, new CaretEdit(line.Start, line.End, repl, newCaret, newEnd), o);
        }
        return null;
    }

    // An indented numbered item starts a sub-list at 1 (or continues a sub-list right above it).
    private static int FirstNumberBelow(string text, Line line, string indent, ListItem item) => NumberAt(text, line, indent, item);

    private static CaretEdit CellMove(string text, Line line, string s, int caret, bool back, string newline)
    {
        var col = caret - line.Start;
        var pipes = Pipes(s);
        CaretEdit At(int pos) => new(pos, pos, "", pos, pos);
        int CellStart(Line l, string ls, int pipeIndex) { var p = Pipes(ls)[pipeIndex] + 1; if (p < ls.Length && ls[p] == ' ') p++; return l.Start + Math.Min(p, ls.Length); }
        if (!back)
        {
            var nextPipe = pipes.FindIndex(p => p >= col);
            if (nextPipe >= 0 && nextPipe < pipes.Count - 1) return At(CellStart(line, s, nextPipe));
            // Last cell: the next body row's first cell, or a new empty row.
            for (var n = NextLine(text, line); n is { } nl2; n = NextLine(text, nl2))
            {
                var ns = Str(text, nl2);
                if (!IsTableRow(ns)) break;
                if (IsDelimiterRow(ns)) continue;
                if (Pipes(ns).Count > 1) return At(CellStart(nl2, ns, 0));
            }
            var row = newline + EmptyRow(Math.Max(1, pipes.Count - 1));
            var c = line.End + newline.Length + 2;
            return new CaretEdit(line.End, line.End, row, c, c);
        }
        var current = pipes.FindLastIndex(p => p < col);
        if (current > 0) return At(CellStart(line, s, current - 1));
        for (var p = PreviousLine(text, line); p is { } pl; p = PreviousLine(text, pl))
        {
            var ps = Str(text, pl);
            if (!IsTableRow(ps)) break;
            if (IsDelimiterRow(ps)) continue;
            var pp = Pipes(ps);
            if (pp.Count > 1) return At(CellStart(pl, ps, pp.Count - 2));
        }
        return At(caret);
    }

    // ---- Backspace ----
    public static CaretEdit? Backspace(string text, int caret, Options o)
    {
        if (!o.Lists || caret <= 0 || InFence(text, caret)) return null;
        var line = LineAt(text, caret);
        var s = Str(text, line);
        if (ParseItem(s) is not { } item || caret - line.Start != item.PrefixLength || s[item.PrefixLength..].Trim().Length > 0) return null;
        var keep = item.Indent;
        return Renumbered(text, new CaretEdit(line.Start, line.End, keep, line.Start + keep.Length, line.Start + keep.Length), o);
    }

    // ---- Renumbering ----
    // Applies `edit`, then renumbers the ordered lists it touches; returns one combined edit on the original text.
    public static CaretEdit Renumbered(string text, CaretEdit edit, Options o)
    {
        var after = text[..edit.Start] + edit.Replacement + text[edit.End..];
        var (result, changes) = RenumberWithChanges(after, edit.Start, edit.Start + edit.Replacement.Length);
        if (changes.Count == 0) return edit;
        int Map(int pos) => pos + changes.Where(c => c.Pos < pos).Sum(c => c.Delta);
        var p = 0; while (p < text.Length && p < result.Length && text[p] == result[p]) p++;
        var sfx = 0; while (sfx < text.Length - p && sfx < result.Length - p && text[text.Length - 1 - sfx] == result[result.Length - 1 - sfx]) sfx++;
        return new CaretEdit(p, text.Length - sfx, result.Substring(p, result.Length - sfx - p), Map(edit.SelectionStart), Map(edit.SelectionEnd));
    }

    // After a deletion: renumber the ordered list around the caret (null when nothing changes).
    public static CaretEdit? RenumberAt(string text, int caret, Options o)
    {
        if (!o.Lists || InFence(text, caret)) return null;
        var (result, changes) = RenumberWithChanges(text, caret, caret);
        if (changes.Count == 0) return null;
        var p = 0; while (p < text.Length && p < result.Length && text[p] == result[p]) p++;
        var sfx = 0; while (sfx < text.Length - p && sfx < result.Length - p && text[text.Length - 1 - sfx] == result[result.Length - 1 - sfx]) sfx++;
        var c = caret + changes.Where(x => x.Pos < caret).Sum(x => x.Delta);
        return new CaretEdit(p, text.Length - sfx, result.Substring(p, result.Length - sfx - p), c, c);
    }

    public static string Renumber(string text, int from, int to) => RenumberWithChanges(text, from, to).Text;

    // Renumbers the ordered lists overlapping [from, to]: within a list (same indent and delimiter, until a blank line
    // or a shallower item) numbers run on from its first item. Changes = (position, length delta) of each fixed number.
    public static (string Text, List<(int Pos, int Delta)> Changes) RenumberWithChanges(string text, int from, int to)
    {
        var lines = new List<Line>();
        for (var i = 0; ;)
        {
            var l = LineAt(text, i); lines.Add(l);
            if (l.Next <= l.End) break;
            i = l.Next;
        }
        var first = lines.FindIndex(l => l.End >= from);
        var last = lines.FindLastIndex(l => l.Start <= to);
        var changes = new List<(int, int)>();
        if (first < 0 || last < 0) return (text, changes);
        while (first > 0 && Str(text, lines[first - 1]).Trim().Length > 0) first--;
        while (last < lines.Count - 1 && Str(text, lines[last + 1]).Trim().Length > 0) last++;
        var counters = new Dictionary<string, (int Next, char Delim)>();
        var sb = new StringBuilder(); var cursor = 0;
        for (var i = first; i <= last; i++)
        {
            var ls = Str(text, lines[i]);
            if (ParseItem(ls) is not { } it || InFence(text, lines[i].Start)) continue;
            foreach (var k in counters.Keys.Where(k => k.Length > it.Indent.Length).ToList()) counters.Remove(k);
            if (!it.Ordered) { counters.Remove(it.Indent); continue; }
            if (counters.TryGetValue(it.Indent, out var c) && c.Delim == it.Delim)
            {
                if (it.Number != c.Next)
                {
                    var numStart = lines[i].Start + it.Indent.Length;
                    var oldLen = it.Marker.Length - 1;
                    var digits = c.Next.ToString();
                    sb.Append(text, cursor, numStart - cursor).Append(digits);
                    cursor = numStart + oldLen;
                    changes.Add((numStart, digits.Length - oldLen));
                }
                counters[it.Indent] = (c.Next + 1, it.Delim);
            }
            else counters[it.Indent] = (it.Number!.Value + 1, it.Delim);
        }
        if (changes.Count == 0) return (text, changes);
        sb.Append(text, cursor, text.Length - cursor);
        return (sb.ToString(), changes);
    }

    // ---- Table alignment ----
    // The table block containing `index` (start of its first line .. end of its last line), or null.
    public static (int Start, int End)? TableAt(string text, int index)
    {
        if (InFence(text, index)) return null;
        var line = LineAt(text, index);
        if (!IsTableRow(Str(text, line))) return null;
        var first = line; var last = line;
        while (PreviousLine(text, first) is { } p && IsTableRow(Str(text, p))) first = p;
        while (NextLine(text, last) is { } n && IsTableRow(Str(text, n))) last = n;
        return (first.Start, last.End);
    }

    public static string AlignRows(IReadOnlyList<string> rows)
    {
        var parsed = rows.Select(r => (Indent: r[..(r.Length - r.TrimStart().Length)], Cells: Cells(r).Select(c => c.Trim()).ToList(), Delim: IsDelimiterRow(r))).ToList();
        var columns = parsed.Max(r => r.Cells.Count);
        var widths = new int[columns];
        foreach (var r in parsed)
            for (var c = 0; c < r.Cells.Count; c++)
                widths[c] = Math.Max(widths[c], r.Delim ? 3 : DisplayWidth(r.Cells[c]));
        for (var c = 0; c < columns; c++) widths[c] = Math.Max(widths[c], 3);
        var sb = new StringBuilder();
        for (var i = 0; i < parsed.Count; i++)
        {
            var r = parsed[i];
            if (i > 0) sb.Append('\n');
            sb.Append(r.Indent).Append('|');
            for (var c = 0; c < columns; c++)
            {
                var cell = c < r.Cells.Count ? r.Cells[c] : "";
                if (r.Delim)
                {
                    var left = cell.StartsWith(':'); var right = cell.Length > 1 && cell.EndsWith(':');
                    var dashes = new string('-', widths[c] - (left ? 1 : 0) - (right ? 1 : 0));
                    sb.Append(' ').Append(left ? ":" : "").Append(dashes).Append(right ? ":" : "").Append(" |");
                }
                else sb.Append(' ').Append(cell).Append(' ', widths[c] - DisplayWidth(cell)).Append(" |");
            }
        }
        return sb.ToString();
    }

    // Aligns the table at `index`; the caret (outside the table) is mapped through the change.
    public static CaretEdit? AlignTable(string text, int index, int caret, Options o)
    {
        if (!o.Tables || !o.Align || TableAt(text, index) is not { } range) return null;
        var nl = NewLineOf(text);
        var rows = text[range.Start..range.End].Split('\n').Select(r => r.TrimEnd('\r')).ToList();
        if (rows.Count < 2 || !IsDelimiterRow(rows[1])) return null;
        var aligned = AlignRows(rows).Replace("\n", nl);
        if (aligned == text[range.Start..range.End]) return null;
        var delta = aligned.Length - (range.End - range.Start);
        var c = caret >= range.End ? caret + delta : caret <= range.Start ? caret : Math.Min(caret, range.Start + aligned.Length);
        return new CaretEdit(range.Start, range.End, aligned, c, c);
    }
}
