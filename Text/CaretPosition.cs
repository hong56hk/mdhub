namespace Mdhub.Text;

// Status-bar caret position, following VS Code: 1-based line and column (UTF-16 characters on the
// logical line), and the selection length with each line break (\r\n, \n or \r) counted as 1.
public static class CaretPosition
{
    public static (int Line, int Column, int Selected) Measure(string text, int selectionStart, int selectionEnd)
    {
        var caret = Math.Clamp(selectionEnd, 0, text.Length);
        var anchor = Math.Clamp(selectionStart, 0, text.Length);
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < caret; i++)
        {
            if (text[i] == '\n' || text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) { line++; lineStart = i + 1; }
        }
        // A caret between \r and \n is still on the line the \r ends.
        var (from, to) = anchor < caret ? (anchor, caret) : (caret, anchor);
        var selected = to - from;
        for (var i = from; i + 1 < to; i++) if (text[i] == '\r' && text[i + 1] == '\n') selected--;
        return (line, caret - lineStart + 1, selected);
    }

    // Plain digits like VS Code, independent of the current culture.
    public static string Format(string text, int selectionStart, int selectionEnd)
    {
        var (line, column, selected) = Measure(text, selectionStart, selectionEnd);
        return $"Ln {line}, Col {column}" + (selected > 0 ? $" ({selected} selected)" : "");
    }
}
