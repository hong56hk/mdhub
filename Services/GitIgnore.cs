using System.Text;
using System.Text.RegularExpressions;

namespace Mdhub.Services;

// Item 46: a small .gitignore matcher (patterns, negation, dir-only, anchored, *, **, ?, [...]), applied per folder
// like git: rules from a folder's .gitignore apply to paths below it, later rules win, and an ignored folder is not entered.
public sealed class GitIgnore
{
    private sealed record Rule(string Base, Regex Pattern, bool Negate, bool DirectoryOnly);
    private readonly IReadOnlyList<Rule> _rules;
    public static readonly GitIgnore Empty = new([]);
    private GitIgnore(IReadOnlyList<Rule> rules) => _rules = rules;
    public bool IsEmpty => _rules.Count == 0;

    // Returns the rules for `directory`: the parent's plus any in directory/.gitignore.
    public GitIgnore Enter(string directory)
    {
        var file = Path.Combine(directory, ".gitignore");
        string[] lines;
        try { if (!File.Exists(file)) return this; lines = File.ReadAllLines(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return this; }
        var added = Parse(directory, lines);
        return added.Count == 0 ? this : new GitIgnore([.. _rules, .. added]);
    }

    public static GitIgnore FromLines(string directory, IEnumerable<string> lines) => new(Parse(directory, lines));

    public bool IsIgnored(string path, bool isDirectory)
    {
        var ignored = false;
        foreach (var rule in _rules)
        {
            if (rule.DirectoryOnly && !isDirectory) continue;
            if (ignored != rule.Negate) continue; // this rule cannot change the outcome
            var relative = Path.GetRelativePath(rule.Base, path);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) continue;
            if (rule.Pattern.IsMatch(relative.Replace('\\', '/'))) ignored = !rule.Negate;
        }
        return ignored;
    }

    private static List<Rule> Parse(string directory, IEnumerable<string> lines)
    {
        var rules = new List<Rule>();
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line[0] == '#') continue;
            var negate = line[0] == '!';
            if (negate) line = line[1..];
            else if (line.StartsWith("\\!") || line.StartsWith("\\#")) line = line[1..];
            var directoryOnly = line.EndsWith('/');
            line = line.TrimEnd('/');
            if (line.Length == 0) continue;
            var anchored = line.Contains('/');
            line = line.TrimStart('/');
            if (line.Length == 0) continue;
            var regex = "^" + (anchored ? "" : "(?:.*/)?") + Translate(line) + "$";
            try { rules.Add(new(directory, new Regex(regex, RegexOptions.CultureInvariant | (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? RegexOptions.IgnoreCase : 0)), negate, directoryOnly)); }
            catch (ArgumentException) { }
        }
        return rules;
    }

    private static string Translate(string pattern)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*' && i + 1 < pattern.Length && pattern[i + 1] == '*')
            {
                var atStart = i == 0 || pattern[i - 1] == '/';
                var slashAfter = i + 2 < pattern.Length && pattern[i + 2] == '/';
                if (atStart && slashAfter) { builder.Append("(?:.*/)?"); i += 2; }
                else if (atStart && i + 2 == pattern.Length) { builder.Append(".*"); i++; }
                else { builder.Append("[^/]*"); i++; }
            }
            else if (c == '*') builder.Append("[^/]*");
            else if (c == '?') builder.Append("[^/]");
            else if (c == '\\' && i + 1 < pattern.Length) builder.Append(Regex.Escape(pattern[++i].ToString()));
            else if (c == '[' && pattern.IndexOf(']', i + 1) is var close and > 0)
            {
                var body = pattern[(i + 1)..close];
                if (body.StartsWith('!')) body = "^" + body[1..];
                builder.Append('[').Append(body.Replace("\\", "\\\\")).Append(']');
                i = close;
            }
            else builder.Append(Regex.Escape(c.ToString()));
        }
        return builder.ToString();
    }
}