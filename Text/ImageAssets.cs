namespace Mdhub.Text;

// Pure naming rules for images pasted into a Markdown note's .assets folder.
public static class ImageAssets
{
    public const string FolderName = ".assets";
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"];

    public static bool IsImageFile(string path) =>
        ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    // <md name without ext>-<yyyyMMddHHmmss><ext>, then -1, -2, ... when taken.
    public static string FileName(string markdownPath, string extension, DateTime now, Func<string, bool> exists)
    {
        if (!extension.StartsWith('.')) extension = "." + extension;
        var stem = $"{Path.GetFileNameWithoutExtension(markdownPath)}-{now:yyyyMMddHHmmss}";
        var candidate = stem + extension;
        for (var i = 1; exists(candidate); i++) candidate = $"{stem}-{i}{extension}";
        return candidate;
    }

    // Relative, forward-slash Markdown link; characters that break a link destination are percent-encoded.
    public static string MarkdownLink(string fileName) =>
        $"![{Path.GetFileNameWithoutExtension(fileName)}]({FolderName}/{fileName.Replace("%", "%25").Replace(" ", "%20").Replace("(", "%28").Replace(")", "%29")})";
}