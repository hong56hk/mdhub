namespace Mdhub.Text;

// Item 70: what Tab / Shift+Tab do to the editor text, UI-free. One result = one replacement (one undo step).
public readonly record struct CaretEdit(int Start, int End, string Replacement, int SelectionStart, int SelectionEnd);

public static class TabEditing
{
    public const int MinSize = 1, MaxSize = 8, DefaultSize = 4;
    public static int Clamp(int size) => Math.Clamp(size, MinSize, MaxSize);

    public static int LineStart(string text, int index) { while (index > 0 && text[index - 1] != '\n') index--; return index; }
    public static int LineEnd(string text, int index) { while (index < text.Length && text[index] is not ('\n' or '\r')) index++; return index; }

    // Visual column of `index` (tabs advance to the next multiple of tabSize).
    public static int Column(string text, int index, int tabSize)
    {
        var col = 0;
        for (var i = LineStart(text, index); i < index; i++) col = text[i] == '\t' ? (col / tabSize + 1) * tabSize : col + 1;
        return col;
    }

    // One indent level: a tab, or tabSize spaces.
    public static string Unit(bool insertSpaces, int tabSize) => insertSpaces ? new string(' ', Clamp(tabSize)) : "\t";

    // Leading characters one outdent removes: a single tab, or up to tabSize spaces (a tab after them ends the level).
    public static int OutdentLength(string text, int lineStart, int tabSize)
    {
        if (lineStart < text.Length && text[lineStart] == '\t') return 1;
        var n = 0;
        while (n < tabSize && lineStart + n < text.Length && text[lineStart + n] == ' ') n++;
        if (n < tabSize && lineStart + n < text.Length && text[lineStart + n] == '\t') n++;
        return n;
    }

    public static CaretEdit? Apply(string text, int selectionStart, int selectionEnd, bool outdent, bool insertSpaces, int tabSize)
    {
        tabSize = Clamp(tabSize);
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, text.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), 0, text.Length);
        var firstLine = LineStart(text, start);
        var multiLine = text.AsSpan(start, end - start).IndexOf('\n') >= 0;
        if (!outdent && !multiLine)
        {
            // Plain Tab: insert at the caret (replacing a one-line selection); spaces fill to the next tab stop.
            var insert = insertSpaces ? new string(' ', tabSize - Column(text, start, tabSize) % tabSize) : "\t";
            return new CaretEdit(start, end, insert, start + insert.Length, start + insert.Length);
        }
        // Block indent / outdent over every touched line (a selection ending at a line's start leaves that line alone).
        var lastAnchor = multiLine && end > start && text[end - 1] == '\n' ? end - 1 : end;
        var blockEnd = LineEnd(text, LineStart(text, lastAnchor));
        var unit = Unit(insertSpaces, tabSize);
        var sb = new System.Text.StringBuilder();
        int newStart = start, newEnd = end, i = firstLine, acc = 0;
        var changed = false;
        while (true)
        {
            var lineEnd = LineEnd(text, i);
            int delta;
            if (outdent)
            {
                var n = OutdentLength(text, i, tabSize);
                if (n > 0) changed = true;
                sb.Append(text, i + n, lineEnd - i - n);
                delta = -n;
            }
            else if (lineEnd == i) delta = 0; // blank lines in a block indent stay empty
            else { sb.Append(unit).Append(text, i, lineEnd - i); delta = unit.Length; changed = true; }
            int Shift(int pos) => pos + acc + (delta >= 0 ? delta : -Math.Min(-delta, pos - i));
            if (start >= i && start <= lineEnd) newStart = Shift(start);
            if (end >= i && end <= lineEnd) newEnd = Shift(end);
            acc += delta;
            if (lineEnd >= blockEnd) break;
            var next = lineEnd;
            if (next < text.Length && text[next] == '\r') next++;
            if (next < text.Length && text[next] == '\n') next++;
            sb.Append(text, lineEnd, next - lineEnd);
            if (end >= lineEnd && end < next) newEnd = end + acc;
            i = next;
        }
        if (end > blockEnd) newEnd = end + acc;
        if (!changed) return null;
        return new CaretEdit(firstLine, blockEnd, sb.ToString(), newStart, newEnd);
    }
}
