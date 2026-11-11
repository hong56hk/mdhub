using Mdhub.Text;

namespace Mdhub.Services;

// Item 102c: the Markdown notes file that sits beside a PDF, and the block "Add to notes" appends.
public static class PdfNotes
{
    public static string PathFor(string pdfPath)
    {
        var full = Path.GetFullPath(pdfPath);
        var dir = Path.GetDirectoryName(full) ?? "";
        return Path.Combine(dir, ImageAssets.FolderName, Path.GetFileNameWithoutExtension(full) + ".notes.md");
    }

    public static string PageLink(string pdfPath, int page)
    {
        var name = Path.GetFileName(pdfPath).Replace("%", "%25").Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29");
        return $"[p. {page}](../{name}#page={page})";
    }

    public static string Quote(string selected, string lineEnding = "\n")
    {
        var text = selected.Replace("\r\n", "\n").Replace('\r', '\n');
        return string.Join(lineEnding, text.Split('\n').Select(line => "> " + line));
    }

    // A blank line, then every selected line as a Markdown quote, then the page link, ending with a newline.
    public static string ApplyAppend(string existing, string selected, string pdfPath, int page, string lineEnding = "\n")
    {
        var body = Quote(selected, lineEnding) + lineEnding + PageLink(pdfPath, page) + lineEnding;
        var prefix = existing;
        if (prefix.Length > 0 && !prefix.EndsWith('\n') && !prefix.EndsWith('\r')) prefix += lineEnding;
        return prefix + lineEnding + body;
    }
}