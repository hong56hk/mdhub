using System.Text.RegularExpressions;

namespace Mdhub.Text;

/// <summary>
/// Small offline highlighter for fenced code blocks: keywords, strings, comments, numbers, types and a few
/// language-specific constructs (JSON/YAML keys, XML tags/attributes, CSS properties, shell variables).
/// Line-based; the only state carried between lines is "inside a block comment".
/// </summary>
public static class CodeHighlighter
{
    private sealed record Lang(HashSet<string> Keywords, HashSet<string> Types, string[] LineComments, (string Open, string Close)? Block, string Quotes,
        bool CaseInsensitive = false, bool Variables = false, bool Decorators = false);

    private static HashSet<string> W(string words, bool ci = false) => new(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), ci ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private static readonly Lang CSharp = new(
        W("abstract as async await base break case catch checked class const continue default delegate do else enum event explicit extern false finally fixed for foreach goto if implicit in interface internal is lock namespace new null operator out override params private protected public readonly record ref return sealed sizeof stackalloc static struct switch this throw true try typeof unchecked unsafe using var virtual void volatile when where while yield get set init value partial required file global"),
        W("bool byte char decimal double float int long object sbyte short string uint ulong ushort nint nuint dynamic Task List Dictionary String Console Math DateTime Guid Exception IEnumerable Action Func"),
        ["//"], ("/*", "*/"), "\"'", Decorators: false);
    private static readonly Lang Js = new(
        W("async await break case catch class const continue debugger default delete do else export extends false finally for from function if import in instanceof let new null of return static super switch this throw true try typeof undefined var void while with yield as interface type enum implements private protected public readonly declare namespace abstract keyof"),
        W("string number boolean any unknown never object Array Promise Map Set Date Error JSON Math console window document"),
        ["//"], ("/*", "*/"), "\"'`", Decorators: true);
    private static readonly Lang Json = new(W("true false null"), W(""), [], null, "\"");
    private static readonly Lang Python = new(
        W("and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield match case self"),
        W("int float str bool list dict set tuple bytes object type print len range open super Exception"),
        ["#"], null, "\"'", Decorators: true);
    private static readonly Lang Sql = new(
        W("select from where and or not insert into values update set delete create table drop alter add primary key foreign references join left right inner outer full on as group by order having limit offset distinct union all null is in like between exists case when then else end index view begin commit rollback transaction default unique check asc desc count sum avg min max with", ci: true),
        W("int integer bigint smallint varchar nvarchar char text date datetime timestamp boolean bool decimal numeric float real blob uuid serial", ci: true),
        ["--"], ("/*", "*/"), "'\"", CaseInsensitive: true);
    private static readonly Lang Shell = new(
        W("if then else elif fi for while until do done case esac in function return exit break continue local export readonly declare echo cd source alias set unset shift test true false sudo"),
        W(""), ["#"], null, "\"'", Variables: true);
    private static readonly Lang PowerShell = new(
        W("if else elseif switch foreach for while do until break continue function return param begin process end try catch finally throw trap filter in exit", ci: true),
        W("Write-Host Write-Output Get-ChildItem Set-Location Get-Content Set-Content New-Item Remove-Item Copy-Item Move-Item Test-Path Join-Path Select-Object Where-Object ForEach-Object Invoke-WebRequest", ci: true),
        ["#"], ("<#", "#>"), "\"'", CaseInsensitive: true, Variables: true);
    private static readonly Lang Css = new(W("important inherit initial unset auto none"), W(""), [], ("/*", "*/"), "\"'");
    private static readonly Lang Yaml = new(W("true false null yes no on off ~"), W(""), ["#"], null, "\"'");
    private static readonly Lang Xml = new(W(""), W(""), [], ("<!--", "-->"), "\"'");

    private static Lang? For(string? name) => name switch
    {
        "c#" or "cs" or "csharp" => CSharp,
        "js" or "javascript" or "jsx" or "mjs" or "cjs" or "ts" or "typescript" or "tsx" => Js,
        "json" or "jsonc" or "json5" => Json,
        "py" or "python" or "python3" => Python,
        "sql" or "mysql" or "postgresql" or "postgres" or "sqlite" or "tsql" => Sql,
        "bash" or "sh" or "shell" or "zsh" or "console" => Shell,
        "powershell" or "ps1" or "pwsh" or "ps" => PowerShell,
        "css" => Css,
        "yaml" or "yml" => Yaml,
        "xml" or "html" or "htm" or "xhtml" or "xaml" or "axaml" or "svg" or "csproj" => Xml,
        _ => null
    };

    public static bool IsSupported(string? language) => For(language?.ToLowerInvariant()) is not null;

    private static readonly Regex Number = new(@"\G(0[xX][0-9a-fA-F_]+|\d[\d_]*(\.\d+)?([eE][+-]?\d+)?[a-zA-Z]{0,2})", RegexOptions.Compiled);
    private static readonly Regex YamlKey = new(@"^(\s*(?:-\s+)?)([^\s#:'""][^:#]*?|""[^""]*""|'[^']*')\s*:(?=\s|$)", RegexOptions.Compiled);
    private static readonly Regex CssProperty = new(@"^\s*((?:--)?[a-zA-Z][\w-]*)\s*:(?![^{]*\{)", RegexOptions.Compiled);

    /// <summary>Appends spans for one code line (offsets relative to the line); returns whether a block comment is still open.</summary>
    public static bool Line(string s, string? language, bool inComment, List<SyntaxSpan> o)
    {
        var lang = For(language);
        if (lang is null) { if (s.Length > 0) o.Add(new(0, s.Length, SyntaxColor.CodeText)); return false; }
        var i = 0;
        var textStart = 0;
        void Add(int start, int length, SyntaxColor color)
        {
            if (start > textStart) o.Add(new(textStart, start - textStart, SyntaxColor.CodeText));
            if (length > 0) o.Add(new(start, length, color));
            textStart = start + length;
        }
        if (inComment && lang.Block is { } bc)
        {
            var end = s.IndexOf(bc.Close, StringComparison.Ordinal);
            if (end < 0) { Add(0, s.Length, SyntaxColor.CodeComment); return true; }
            Add(0, end + bc.Close.Length, SyntaxColor.CodeComment);
            i = end + bc.Close.Length;
        }
        if (lang == Yaml && YamlKey.Match(s, i) is { Success: true } yk && yk.Index == i) { Add(yk.Groups[2].Index, yk.Groups[2].Length, SyntaxColor.CodeProperty); i = yk.Groups[2].Index + yk.Groups[2].Length; }
        if (lang == Css && CssProperty.Match(s) is { Success: true } cp) { Add(cp.Groups[1].Index, cp.Groups[1].Length, SyntaxColor.CodeProperty); i = cp.Groups[1].Index + cp.Groups[1].Length; }
        var inTag = false;
        while (i < s.Length)
        {
            var c = s[i];
            if (lang.Block is { } b && string.CompareOrdinal(s, i, b.Open, 0, b.Open.Length) == 0)
            {
                var end = s.IndexOf(b.Close, i + b.Open.Length, StringComparison.Ordinal);
                if (end < 0) { Add(i, s.Length - i, SyntaxColor.CodeComment); return true; }
                Add(i, end + b.Close.Length - i, SyntaxColor.CodeComment); i = end + b.Close.Length; continue;
            }
            if (lang.LineComments.Any(p => string.CompareOrdinal(s, i, p, 0, p.Length) == 0) && (lang != Shell || i == 0 || char.IsWhiteSpace(s[i - 1])))
            {
                Add(i, s.Length - i, SyntaxColor.CodeComment); return false;
            }
            if (lang == Xml)
            {
                if (c == '<')
                {
                    var j = i + 1; if (j < s.Length && s[j] is '/' or '?' or '!') j++;
                    var k = j; while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] is ':' or '-' or '_' or '.')) k++;
                    Add(i, j - i, SyntaxColor.Marker);
                    if (k > j) Add(j, k - j, SyntaxColor.CodeTag);
                    inTag = true; i = k; continue;
                }
                if (inTag && c is '>' or '/') { Add(i, 1, SyntaxColor.Marker); if (c == '>') inTag = false; i++; continue; }
                if (inTag && (char.IsLetter(c) || c == '_'))
                {
                    var k = i; while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] is ':' or '-' or '_' or '.')) k++;
                    Add(i, k - i, SyntaxColor.CodeAttribute); i = k; continue;
                }
                if (inTag && lang.Quotes.Contains(c)) { var e = StringEnd(s, i, c); Add(i, e - i, SyntaxColor.CodeString); i = e; continue; }
                if (c == '&') { var semi = s.IndexOf(';', i); if (semi > i && semi - i < 10) { Add(i, semi - i + 1, SyntaxColor.CodeKeyword); i = semi + 1; continue; } }
                i++; continue;
            }
            if (lang.Quotes.Contains(c))
            {
                var e = c == '"' && lang == Python && string.CompareOrdinal(s, i, "\"\"\"", 0, 3) == 0 ? (s.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal) is var t and >= 0 ? t + 3 : s.Length) : StringEnd(s, i, c);
                // JSON keys: a string followed by ':'.
                var after = e; while (after < s.Length && s[after] == ' ') after++;
                var isKey = lang == Json && after < s.Length && s[after] == ':';
                if (lang == CSharp && i > 0 && s[i - 1] is '@' or '$') Add(i - 1, e - i + 1, SyntaxColor.CodeString);
                else Add(i, e - i, isKey ? SyntaxColor.CodeProperty : SyntaxColor.CodeString);
                i = e; continue;
            }
            if (lang.Variables && c == '$' && i + 1 < s.Length && (char.IsLetter(s[i + 1]) || s[i + 1] is '_' or '{' or '(' ))
            {
                var k = i + 1;
                if (s[k] == '{') { var close = s.IndexOf('}', k); k = close < 0 ? s.Length : close + 1; }
                else if (s[k] == '(') k++;
                else while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] is '_' or ':')) k++;
                Add(i, k - i, SyntaxColor.CodeProperty); i = k; continue;
            }
            if (lang.Decorators && c == '@' && i + 1 < s.Length && char.IsLetter(s[i + 1]))
            {
                var k = i + 1; while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] is '_' or '.')) k++;
                Add(i, k - i, SyntaxColor.CodeType); i = k; continue;
            }
            if (char.IsDigit(c) && (i == 0 || !(char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_')))
            {
                var m = Number.Match(s, i);
                if (m.Success) { Add(i, m.Length, SyntaxColor.CodeNumber); i += m.Length; continue; }
            }
            if (char.IsLetter(c) || c == '_' || (lang == Css && c is '#' or '.' or '-'))
            {
                var k = i + 1; while (k < s.Length && (char.IsLetterOrDigit(s[k]) || s[k] == '_' || (lang == PowerShell || lang == Css || lang == Shell) && s[k] == '-')) k++;
                var word = s[i..k];
                if (lang == Css)
                {
                    // Selectors before '{' and on their own lines are tags; values stay text unless keywords.
                    var brace = s.IndexOf('{', k); var colon = s.IndexOf(':', i);
                    if (brace >= 0 && (colon < 0 || colon > brace || s.TrimStart().StartsWith(word))) { Add(i, k - i, SyntaxColor.CodeTag); i = k; continue; }
                }
                if (lang.Keywords.Contains(word)) Add(i, k - i, SyntaxColor.CodeKeyword);
                else if (lang.Types.Contains(word)) Add(i, k - i, SyntaxColor.CodeType);
                i = k; continue;
            }
            i++;
        }
        Add(s.Length, 0, SyntaxColor.CodeText);
        return false;
    }

    private static int StringEnd(string s, int i, char quote)
    {
        for (var j = i + 1; j < s.Length; j++)
        {
            if (s[j] == '\\') { j++; continue; }
            if (s[j] == quote) return j + 1;
        }
        return s.Length;
    }
}
