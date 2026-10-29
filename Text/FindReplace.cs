using System.Text;
using System.Text.RegularExpressions;

namespace Mdhub.Text;

public readonly record struct FindOptions(bool MatchCase = false, bool WholeWord = false, bool UseRegex = false, bool PreserveCase = false);

public readonly record struct FindMatch(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed record FindResult(IReadOnlyList<FindMatch> Matches, string? Error)
{
    public bool IsValid => Error is null;
    public static readonly FindResult Empty = new([], null);
}

// UI-free find/replace: case, whole word, regex, an optional range, preserve-case and regex group substitution.
public static class FindReplace
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    public static Regex? Build(string query, FindOptions options, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(query)) return null;
        var pattern = options.UseRegex ? query : Regex.Escape(query);
        if (options.WholeWord) pattern = $@"(?<![\p{{L}}\p{{N}}_])(?:{pattern})(?![\p{{L}}\p{{N}}_])";
        var flags = RegexOptions.Multiline | RegexOptions.CultureInvariant | (options.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try { return new Regex(pattern, flags, Timeout); }
        catch (ArgumentException ex) { error = ex.Message; return null; }
    }

    // All non-empty matches, optionally only those fully inside [range.Start, range.End).
    public static FindResult FindAll(string text, string query, FindOptions options, (int Start, int End)? range = null)
    {
        var regex = Build(query, options, out var error);
        if (regex is null) return error is null ? FindResult.Empty : new FindResult([], error);
        var (start, end) = Clamp(text, range);
        var matches = new List<FindMatch>();
        try
        {
            for (var m = regex.Match(text, start); m.Success; m = m.NextMatch())
            {
                if (m.Index + m.Length > end) break;
                if (m.Length > 0) matches.Add(new FindMatch(m.Index, m.Length));
            }
        }
        catch (RegexMatchTimeoutException ex) { return new FindResult([], ex.Message); }
        return new FindResult(matches, null);
    }

    // The text that replaces one match: regex groups ($1, ${name}) are expanded in regex mode, then case is preserved if asked.
    public static string Replacement(string text, FindMatch match, string query, string replace, FindOptions options)
    {
        var value = replace;
        if (options.UseRegex && Build(query, options, out _) is { } regex)
        {
            var m = regex.Match(text, match.Start);
            if (m.Success && m.Index == match.Start && m.Length == match.Length) value = m.Result(replace);
        }
        return options.PreserveCase ? PreserveCase(text.Substring(match.Start, match.Length), value) : value;
    }

    // Mirrors the matched text's casing onto the replacement: ALL CAPS, all lower, or Capitalized.
    public static string PreserveCase(string matched, string replacement)
    {
        if (replacement.Length == 0 || !matched.Any(char.IsLetter)) return replacement;
        var letters = matched.Where(char.IsLetter).ToArray();
        if (letters.All(char.IsUpper) && letters.Length > 1) return replacement.ToUpperInvariant();
        if (letters.All(char.IsLower)) return replacement.ToLowerInvariant();
        if (char.IsUpper(letters[0]) && letters.Skip(1).All(char.IsLower))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..].ToLowerInvariant();
        if (char.IsUpper(letters[0])) return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        return replacement;
    }

    // Replaces every match and returns the new text plus the span [Start, End) of the original that changed.
    public static (string Text, int Count, int Start, int End, string Segment) ReplaceAll(string text, string query, string replace, FindOptions options, (int Start, int End)? range = null)
    {
        var matches = FindAll(text, query, options, range).Matches;
        if (matches.Count == 0) return (text, 0, 0, 0, "");
        var start = matches[0].Start;
        var end = matches[^1].End;
        var segment = new StringBuilder();
        var position = start;
        foreach (var match in matches)
        {
            segment.Append(text, position, match.Start - position);
            segment.Append(Replacement(text, match, query, replace, options));
            position = match.End;
        }
        var replaced = segment.ToString();
        return (text[..start] + replaced + text[end..], matches.Count, start, end, replaced);
    }

    // First match starting at or after the position, wrapping around.
    public static int NextIndex(IReadOnlyList<FindMatch> matches, int position)
    {
        if (matches.Count == 0) return -1;
        for (var i = 0; i < matches.Count; i++) if (matches[i].Start >= position) return i;
        return 0;
    }

    // Last match starting before the position, wrapping around.
    public static int PreviousIndex(IReadOnlyList<FindMatch> matches, int position)
    {
        if (matches.Count == 0) return -1;
        for (var i = matches.Count - 1; i >= 0; i--) if (matches[i].Start < position) return i;
        return matches.Count - 1;
    }

    public static string CountLabel(int current, int total) => total == 0 ? "No results" : $"{(current < 0 ? "?" : (current + 1).ToString())} of {total}";

    private static (int, int) Clamp(string text, (int Start, int End)? range)
    {
        if (range is not { } r) return (0, text.Length);
        var start = Math.Clamp(Math.Min(r.Start, r.End), 0, text.Length);
        var end = Math.Clamp(Math.Max(r.Start, r.End), 0, text.Length);
        return (start, end);
    }
}