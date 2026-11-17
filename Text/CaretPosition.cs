namespace Mdhub.Text;

// Status-bar caret position, following VS Code: 1-based line and column (UTF-16 characters on the
// logical line), and the selection length with each line break (\r\n, \n or \r) counted as 1.
public static class CaretPosition
{
    private static string? _indexed;
    private static int[] _lineStarts = [0];

    static void EnsureLineStarts(string text)
    {
        if (ReferenceEquals(_indexed, text)) return;
        var starts = new List<int>(Math.Max(8, text.Length / 32)) { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                starts.Add(i + 1);
            }
            else if (text[i] == '\n') starts.Add(i + 1);
        }
        _lineStarts = starts.ToArray();
        _indexed = text;
    }

    public static (int Line, int Column, int Selected) Measure(string text, int selectionStart, int selectionEnd)
    {
        var caret = Math.Clamp(selectionEnd, 0, text.Length);
        var anchor = Math.Clamp(selectionStart, 0, text.Length);
        EnsureLineStarts(text);
        var starts = _lineStarts;
        var lo = 0;
        var hi = starts.Length - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (starts[mid] <= caret) lo = mid + 1;
            else hi = mid - 1;
        }
        var lineStart = starts[hi];
        var line = hi + 1;
        // A caret between \r and \n is still on the line the \r ends (the \n is not a start).
        var (from, to) = anchor < caret ? (anchor, caret) : (caret, anchor);
        var selected = to - from;
        for (var i = from; i + 1 < to; i++) if (text[i] == '\r' && text[i + 1] == '\n') selected--;
        return (line, caret - lineStart + 1, selected);
    }

    // Plain digits like VS Code, independent of the current culture.
    public static string Format(string text, int selectionStart, int selectionEnd)
    {
        var (line, column, selected) = Measure(text, selectionStart, selectionEnd);
        return Mdhub.Localization.Loc.F("CaretPos", line, column) + (selected > 0 ? Mdhub.Localization.Loc.F("CaretSelected", selected) : "");
    }
}