namespace Mdhub.Services;

// UI-free Open Recent list logic: newest first, no duplicates (case-insensitive on Windows), capped.
public static class RecentList
{
    public const int Max = 10;
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static bool Same(string a, string b) => string.Equals(Normalize(a), Normalize(b), Comparison);

    private static string Normalize(string path)
    {
        try { path = Path.GetFullPath(path); } catch (Exception) { }
        return path.Length > 3 ? path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : path;
    }

    // Moves (or inserts) the path to the front and drops anything past the cap.
    public static List<string> Add(IEnumerable<string> list, string path, int max = Max)
    {
        path = Normalize(path);
        return new[] { path }.Concat(list.Where(p => !Same(p, path))).Take(max).ToList();
    }

    public static List<string> Remove(IEnumerable<string> list, string path) => list.Where(p => !Same(p, path)).ToList();

    // Cleans a list read from disk: drops blanks and duplicates and applies the cap (existence is checked on click).
    public static List<string> Sanitize(IEnumerable<string?>? list, int max = Max)
    {
        var result = new List<string>();
        foreach (var p in list ?? [])
            if (!string.IsNullOrWhiteSpace(p) && !result.Any(r => Same(r, p))) result.Add(p);
        return result.Take(max).ToList();
    }

    // The containing folder shown next to the name: home becomes "~", long paths keep the root and the last two folders.
    public static string ShortParent(string path, string? home = null, int maxLength = 40)
    {
        var parent = Path.GetDirectoryName(Normalize(path)) ?? "";
        home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var sep = Path.DirectorySeparatorChar;
        if (!string.IsNullOrEmpty(home))
        {
            home = home.TrimEnd(sep, Path.AltDirectorySeparatorChar);
            if (string.Equals(parent, home, Comparison)) parent = "~";
            else if (parent.StartsWith(home + sep, Comparison)) parent = "~" + parent[home.Length..];
        }
        if (parent.Length <= maxLength) return parent;
        var parts = parent.Split(sep, Path.AltDirectorySeparatorChar);
        if (parts.Length <= 3) return parent;
        return string.Join(sep, parts[0], "…", parts[^2], parts[^1]);
    }
}