using System.Text.RegularExpressions;

namespace Mdhub.Text;

// Item 68 (VS Code): a text file's line ending. The editor keeps the text exactly as read (every line keeps its own
// terminator, which item 72's line moves rely on); the document's LineEnding is what Enter, paste and Change End of
// Line Sequence use, and a file that mixes endings is reported so the user can normalize it.
public enum LineEnding { CrLf, Lf, Cr }

public readonly record struct LineEndingCounts(int CrLf, int Lf, int Cr)
{
    public int Total => CrLf + Lf + Cr;
    public bool IsMixed => (CrLf > 0 ? 1 : 0) + (Lf > 0 ? 1 : 0) + (Cr > 0 ? 1 : 0) > 1;
}

public static partial class LineEndings
{
    public static LineEnding Os => OperatingSystem.IsWindows() ? LineEnding.CrLf : LineEnding.Lf;
    public static string Sequence(LineEnding ending) => ending switch { LineEnding.CrLf => "\r\n", LineEnding.Lf => "\n", _ => "\r" };
    public static string Label(LineEnding ending) => ending switch { LineEnding.CrLf => "CRLF", LineEnding.Lf => "LF", _ => "CR" };

    public static LineEndingCounts Count(string text)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n') lf++;
            else if (c == '\r') { if (i + 1 < text.Length && text[i + 1] == '\n') { crlf++; i++; } else cr++; }
        }
        return new(crlf, lf, cr);
    }

    // The majority ending; a text without line breaks (or a tie that includes the fallback) uses the fallback,
    // other ties prefer CRLF, then LF.
    public static LineEnding Detect(string text, LineEnding fallback) => Majority(Count(text), fallback);
    public static LineEnding Majority(LineEndingCounts counts, LineEnding fallback)
    {
        if (counts.Total == 0) return fallback;
        var max = Math.Max(counts.CrLf, Math.Max(counts.Lf, counts.Cr));
        int Of(LineEnding e) => e switch { LineEnding.CrLf => counts.CrLf, LineEnding.Lf => counts.Lf, _ => counts.Cr };
        if (Of(fallback) == max) return fallback;
        return counts.CrLf == max ? LineEnding.CrLf : counts.Lf == max ? LineEnding.Lf : LineEnding.Cr;
    }

    [GeneratedRegex("\r\n|\r|\n")] private static partial Regex Breaks();
    public static string Normalize(string text, LineEnding ending)
    {
        var sequence = Sequence(ending);
        return Breaks().Replace(text, sequence);
    }
}