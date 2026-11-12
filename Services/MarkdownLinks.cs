using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Mdhub.Rendering;

namespace Mdhub.Services;

public enum MarkdownLinkKind { Heading, LocalFile, LocalFolder, OpenInApp, Web, Mail, Missing, Blocked }

public sealed record MarkdownLink(MarkdownLinkKind Kind, string Href, string? Path = null, string? Fragment = null, int? Page = null, Uri? Uri = null);

// Item 111: resolve a Markdown link destination (inline [text](url), <url>, or #anchor) against the current file.
public static class MarkdownLinks
{
    public static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".ps1", ".psm1", ".lnk", ".vbs", ".vbe", ".msi", ".com", ".scr",
        ".js", ".jse", ".wsf", ".wsh", ".hta", ".cpl", ".reg", ".app", ".sh", ".pif", ".msc", ".jar"
    };

    public static bool IsBlocked(string path) => BlockedExtensions.Contains(System.IO.Path.GetExtension(path));

    public static string Decode(string value)
    {
        try { return Uri.UnescapeDataString(value.Replace("+", "%2B")); }
        catch (Exception) { return value; }
    }

    public static string StripBrackets(string href)
    {
        href = href.Trim();
        if (href.Length >= 2 && href[0] == '<' && href[^1] == '>') href = href[1..^1].Trim();
        return href;
    }

    public static void SplitFragment(string href, out string pathPart, out string? fragment)
    {
        href = StripBrackets(href);
        var hash = href.IndexOf('#');
        if (hash < 0) { pathPart = href; fragment = null; return; }
        pathPart = href[..hash];
        fragment = href[(hash + 1)..];
    }

    public static string? ResolveLocalPath(string pathPart, string? fromDocumentPath)
    {
        pathPart = Decode(StripBrackets(pathPart));
        if (pathPart.Length == 0) return null;
        try
        {
            if (Path.IsPathRooted(pathPart)) return Path.GetFullPath(pathPart);
            if (fromDocumentPath is not { Length: > 0 }) return null;
            var dir = Path.GetDirectoryName(Path.GetFullPath(fromDocumentPath));
            return dir is null ? null : Path.GetFullPath(Path.Combine(dir, pathPart));
        }
        catch (Exception) { return null; }
    }

    public static MarkdownLink Resolve(string href, string? fromDocumentPath, string? workspaceRoot = null)
    {
        var original = href ?? "";
        href = StripBrackets(original);
        if (href.Length == 0) return new(MarkdownLinkKind.Missing, original);

        if (LooksLikeScheme(href))
        {
            SplitFragment(href, out var schemePath, out var schemeFragment);
            if (Uri.TryCreate(href, UriKind.Absolute, out var uri) || Uri.TryCreate(schemePath, UriKind.Absolute, out uri))
            {
                if (uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                    || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                    return new(MarkdownLinkKind.Web, original, Uri: uri);
                if (uri.Scheme.Equals(Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase))
                    return new(MarkdownLinkKind.Mail, original, Uri: uri);
                if (uri.IsFile)
                    return ClassifyPath(original, uri.LocalPath, schemeFragment, fromDocumentPath, workspaceRoot, fileUri: true);
            }
            return new(MarkdownLinkKind.Blocked, original);
        }

        SplitFragment(href, out var pathPart, out var fragment);
        if (pathPart.Length == 0)
            return new(MarkdownLinkKind.Heading, original, Path: fromDocumentPath, Fragment: fragment);

        var resolved = ResolveLocalPath(pathPart, fromDocumentPath);
        if (resolved is null) return new(MarkdownLinkKind.Missing, original, Fragment: fragment);
        return ClassifyPath(original, resolved, fragment, fromDocumentPath, workspaceRoot, fileUri: false);
    }

    static MarkdownLink ClassifyPath(string original, string resolved, string? fragment, string? fromDocumentPath, string? workspaceRoot, bool fileUri)
    {
        if (IsBlocked(resolved)) return new(MarkdownLinkKind.Blocked, original, resolved, fragment);

        if (fileUri && workspaceRoot is { Length: > 0 } && !InWorkspace(workspaceRoot, resolved) && !IsSafeExternal(resolved))
            return new(MarkdownLinkKind.Blocked, original, resolved, fragment);

        if (Directory.Exists(resolved)) return new(MarkdownLinkKind.LocalFolder, original, resolved, fragment);
        if (!File.Exists(resolved)) return new(MarkdownLinkKind.Missing, original, resolved, fragment);

        if (FileSupport.IsPdf(resolved) && PdfNotes.TryParsePageLink(original, fromDocumentPath, out var pdf, out var page))
            return new(MarkdownLinkKind.LocalFile, original, pdf, fragment, page);

        // Re-parse a bare path#page= after the href was a file:// URL or similar.
        if (FileSupport.IsPdf(resolved) && fragment is not null && fragment.StartsWith("page=", StringComparison.OrdinalIgnoreCase)
            && PdfNotes.TryParsePageLink(resolved + "#" + fragment, fromDocumentPath, out pdf, out page))
            return new(MarkdownLinkKind.LocalFile, original, pdf, fragment, page);

        var kind = FileSupport.Detect(resolved);
        if (kind is SupportedFileKind.Text or SupportedFileKind.Image or SupportedFileKind.Pdf)
            return new(MarkdownLinkKind.LocalFile, original, resolved, fragment);
        return new(MarkdownLinkKind.OpenInApp, original, resolved, fragment);
    }

    static bool LooksLikeScheme(string href)
    {
        var colon = href.IndexOf(':');
        if (colon <= 0) return false;
        for (var i = 0; i < colon; i++)
        {
            var c = href[i];
            if (!(char.IsAsciiLetter(c) || (i > 0 && (char.IsAsciiDigit(c) || c is '+' or '-' or '.')))) return false;
        }
        return true;
    }

    static bool InWorkspace(string workspaceRoot, string path)
    {
        try { return ExplorerFileOperations.Contains(workspaceRoot, path); }
        catch (Exception) { return false; }
    }

    // file:// outside the workspace: allowed only when it is not an executable or script.
    static bool IsSafeExternal(string path) => !IsBlocked(path);

    public static int? FindHeadingLine(string markdown, string? fragment)
    {
        if (string.IsNullOrWhiteSpace(fragment) || fragment.StartsWith("page=", StringComparison.OrdinalIgnoreCase)) return null;
        var wanted = Slug(Decode(fragment.Trim().TrimStart('#')));
        if (wanted.Length == 0) return null;
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var heading in MarkdownPreview.Parse(markdown).Descendants<HeadingBlock>())
        {
            var title = HeadingText(heading);
            var slug = Slug(title);
            seen.TryGetValue(slug, out var n);
            seen[slug] = n + 1;
            var id = n == 0 ? slug : slug + "-" + n;
            if (id == wanted || slug.Equals(wanted, StringComparison.Ordinal) || title.Equals(Decode(fragment), StringComparison.OrdinalIgnoreCase))
                return heading.Line + 1;
        }
        return null;
    }

    public static string Slug(string text)
    {
        var sb = new StringBuilder();
        foreach (var rune in text.Trim().ToLowerInvariant().EnumerateRunes())
        {
            if (System.Text.Rune.IsLetterOrDigit(rune) || rune.Value is '-' or '_') sb.Append(rune);
            else if (System.Text.Rune.IsWhiteSpace(rune) && sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }

    static string HeadingText(HeadingBlock heading)
    {
        if (heading.Inline is null) return heading.Lines.ToString().Trim();
        var sb = new StringBuilder();
        Walk(heading.Inline);
        return sb.ToString().Trim();

        void Walk(ContainerInline container)
        {
            foreach (var item in container)
            {
                switch (item)
                {
                    case LiteralInline literal: sb.Append(literal.Content); break;
                    case CodeInline code: sb.Append(code.Content); break;
                    case LineBreakInline: sb.Append(' '); break;
                    case ContainerInline nested: Walk(nested); break;
                }
            }
        }
    }
}