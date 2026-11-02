using System.Net;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Mdhub.Text;

namespace Mdhub.Services;

public static class UnusedImageScanner
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    private static readonly Regex HtmlLink = new("\\b(?:src|href)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A level owns only its immediate .assets files and immediate .md files. Never follow links out of the tree.
    public static IReadOnlyList<string> Scan(string directory, bool recursive, IReadOnlyDictionary<string, string>? openMarkdown = null)
    {
        directory = Path.GetFullPath(directory);
        var unused = new List<string>();
        Visit(directory);
        return unused.OrderBy(p => Path.GetRelativePath(directory, p), PathComparer).ToArray();

        void Visit(string level)
        {
            if (File.GetAttributes(level).HasFlag(FileAttributes.ReparsePoint)) return;
            var assets = Path.Combine(level, ImageAssets.FolderName);
            if (Directory.Exists(assets) && !File.GetAttributes(assets).HasFlag(FileAttributes.ReparsePoint))
            {
                var images = Directory.EnumerateFiles(assets).Where(IsImage)
                    .Where(p => !File.GetAttributes(p).HasFlag(FileAttributes.ReparsePoint)).ToArray();
                if (images.Length > 0)
                {
                    var used = new HashSet<string>(PathComparer);
                    foreach (var note in Directory.EnumerateFiles(level).Where(p => Path.GetExtension(p).Equals(".md", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Keep disk references too: unsaved removal of a link must not break the saved note.
                        ReadLinks(TextFileStore.Decode(File.ReadAllBytes(note)).Text, level, used);
                        if (openMarkdown is not null && openMarkdown.TryGetValue(note, out var text)) ReadLinks(text, level, used);
                    }
                    unused.AddRange(images.Where(p => !used.Contains(p)));
                }
            }
            if (!recursive) return;
            foreach (var child in Directory.EnumerateDirectories(level))
            {
                var name = Path.GetFileName(child);
                if (name.Equals(ImageAssets.FolderName, StringComparison.OrdinalIgnoreCase) || name.Equals(".git", StringComparison.OrdinalIgnoreCase) || FolderService.IsInternal(child)) continue;
                Visit(child);
            }
        }
    }

    private static bool IsImage(string path) => ImageAssets.IsImageFile(path)
        || Path.GetExtension(path).ToLowerInvariant() is ".svg" or ".ico" or ".tif" or ".tiff" or ".avif";

    private static void ReadLinks(string markdown, string level, HashSet<string> used)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        foreach (var link in document.Descendants<LinkInline>()) Add(link.Url);
        foreach (var html in document.Descendants<HtmlInline>()) ReadHtml(html.Tag);
        foreach (var html in document.Descendants<HtmlBlock>()) ReadHtml(html.Lines.ToString());

        void ReadHtml(string html)
        {
            foreach (Match match in HtmlLink.Matches(html))
                Add(WebUtility.HtmlDecode(match.Groups.Cast<Group>().Skip(1).First(g => g.Success).Value));
        }
        void Add(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                string path;
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                {
                    if (!uri.IsFile) return;
                    path = uri.LocalPath;
                }
                else path = Uri.UnescapeDataString(url.Split('#', '?')[0]).Replace('/', Path.DirectorySeparatorChar);
                used.Add(Path.GetFullPath(path, level));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or UriFormatException) { }
        }
    }
}
