namespace Mdhub.Text;

// VS Code-style fuzzy matching: every pattern character must appear in order (case-insensitive). The best alignment
// is found by dynamic programming; matches at the start, at word starts (after / \ _ - . space, or a lower-to-upper
// camel hump) and runs of consecutive characters score higher. Pure, so it is unit tested.
public static class FuzzyMatch
{
    public const int MaxPattern = 64, MaxTarget = 512;

    public static bool IsSeparator(char c) => c is '/' or '\\';

    private static bool Same(char p, char t) =>
        char.ToLowerInvariant(p) == char.ToLowerInvariant(t) || IsSeparator(p) && IsSeparator(t);

    private static bool WordStart(string t, int j)
    {
        if (j == 0) return true;
        var prev = t[j - 1];
        return prev is '/' or '\\' or '_' or '-' or '.' or ' ' || char.IsUpper(t[j]) && char.IsLower(prev) || char.IsLetter(t[j]) && char.IsDigit(prev);
    }

    // Cheap pre-check: is the pattern a subsequence of the target?
    public static bool IsSubsequence(string pattern, string target)
    {
        var i = 0;
        for (var j = 0; j < target.Length && i < pattern.Length; j++) if (Same(pattern[i], target[j])) i++;
        return i == pattern.Length;
    }

    // Score and matched positions in the target, or null when the pattern does not match. An empty pattern matches with score 0.
    public static (int Score, int[] Positions)? Match(string pattern, string target)
    {
        if (pattern.Length == 0) return (0, []);
        if (pattern.Length > MaxPattern || pattern.Length > target.Length || !IsSubsequence(pattern, target)) return null;
        if (target.Length > MaxTarget) target = target[..MaxTarget];
        int m = pattern.Length, n = target.Length;
        const int None = int.MinValue / 2;
        // at[i, j]: best score with pattern[i] matched at target[j]; from[i, j]: where pattern[i-1] was matched.
        var at = new int[m, n];
        var from = new int[m, n];
        for (var i = 0; i < m; i++)
        {
            var bestPrev = None; var bestPrevAt = -1; // best at[i-1, k] for k < j-1
            for (var j = 0; j < n; j++)
            {
                at[i, j] = None;
                if (i > 0 && j >= 2 && at[i - 1, j - 2] > bestPrev) { bestPrev = at[i - 1, j - 2]; bestPrevAt = j - 2; }
                if (j < i || !Same(pattern[i], target[j])) continue;
                var score = 1 + (pattern[i] == target[j] ? 1 : 0);
                var start = WordStart(target, j);
                if (j == 0) score += 10; else if (start) score += 7;
                if (i == 0) { at[i, j] = score; from[i, j] = -1; continue; }
                var consecutive = j >= 1 && at[i - 1, j - 1] > None ? at[i - 1, j - 1] + score + 5 : None;
                var gap = bestPrev > None ? bestPrev + score : None;
                if (consecutive >= gap && consecutive > None) { at[i, j] = consecutive; from[i, j] = j - 1; }
                else if (gap > None) { at[i, j] = gap; from[i, j] = bestPrevAt; }
            }
        }
        var bestJ = -1; var best = None;
        for (var j = 0; j < n; j++) if (at[m - 1, j] > best) { best = at[m - 1, j]; bestJ = j; }
        if (bestJ < 0) return null;
        var positions = new int[m];
        for (int i = m - 1, j = bestJ; i >= 0; i--) { positions[i] = j; j = from[i, j]; }
        return (best, positions);
    }
}

// One Quick Open row: the relative path, where its file name starts, and the matched characters (indices into RelativePath).
public sealed record QuickOpenItem(string RelativePath, int NameStart, int Score, int[] Hits, bool Recent)
{
    public string Name => RelativePath[NameStart..];
    public string Folder => NameStart > 0 ? RelativePath[..(NameStart - 1)] : "";
}

public static class QuickOpenRanker
{
    public const int MaxResults = 100;
    private const int NameBonus = 1_000_000;

    // "name:12" (or "name:12:3", column ignored) -> ("name", 12). The line is null without a trailing :digits.
    public static (string Pattern, int? Line) Parse(string? query)
    {
        var q = (query ?? "").Trim();
        var m = System.Text.RegularExpressions.Regex.Match(q, @"^(.*?):(\d{1,9})(?::\d*)?$");
        if (!m.Success) return (q, null);
        return (m.Groups[1].Value.Trim(), int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static int NameStartOf(string path)
    {
        var i = path.LastIndexOfAny(['/', '\\']);
        return i + 1;
    }

    // Ranks relative paths for the pattern: name matches above path-only matches, then score, recently opened,
    // shorter names and paths. A pattern with a path separator is matched against the whole relative path.
    // With an empty pattern the recent files are returned in their order.
    public static IReadOnlyList<QuickOpenItem> Rank(IReadOnlyList<string> files, string pattern, IReadOnlyList<string> recent, int max = MaxResults, CancellationToken cancel = default)
    {
        var recentSet = new HashSet<string>(recent, StringComparer.OrdinalIgnoreCase);
        pattern = string.Concat(pattern.Where(c => !char.IsWhiteSpace(c)));
        if (pattern.Length == 0)
            return recent.Take(max).Select(r => new QuickOpenItem(r, NameStartOf(r), 0, [], true)).ToList();
        var pathPattern = pattern.Any(FuzzyMatch.IsSeparator);
        var found = new List<QuickOpenItem>();
        for (var k = 0; k < files.Count; k++)
        {
            if ((k & 1023) == 0) cancel.ThrowIfCancellationRequested();
            var path = files[k];
            var nameStart = NameStartOf(path);
            if (!pathPattern && FuzzyMatch.Match(pattern, path[nameStart..]) is { } name)
            {
                found.Add(new QuickOpenItem(path, nameStart, NameBonus + name.Score, name.Positions.Select(p => p + nameStart).ToArray(), recentSet.Contains(path)));
                continue;
            }
            if (FuzzyMatch.Match(pattern, path) is { } full)
                found.Add(new QuickOpenItem(path, nameStart, full.Score, full.Positions, recentSet.Contains(path)));
        }
        return found.OrderByDescending(f => f.Score).ThenByDescending(f => f.Recent).ThenBy(f => f.Name.Length).ThenBy(f => f.RelativePath.Length)
            .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).Take(max).ToList();
    }
}