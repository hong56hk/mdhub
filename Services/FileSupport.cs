using System.Text;

namespace Mdhub.Services;

public enum SupportedFileKind { Unsupported, Text, Image }

public static class FileSupport
{
    public static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tif" or ".tiff";

    public static bool IsKnownBinary(string path) => Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll" or ".pdb" or ".zip" or ".7z" or ".rar"
        or ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx"
        or ".sqlite" or ".db" or ".mp3" or ".mp4" or ".wav" or ".woff" or ".woff2" or ".ttf";

    // Bug 39 / item 52: text by extension (notes, config, data and source files), whatever their encoding or stray control bytes;
    // never dimmed, always open as text. Everything else is sniffed.
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".jsonc", ".yaml", ".yml", ".log", ".csv", ".tsv", ".ini", ".cfg", ".conf", ".toml", ".env", ".properties",
        ".xml", ".xaml", ".axaml", ".csproj", ".props", ".targets", ".sln", ".config", ".html", ".htm", ".css", ".scss", ".svg",
        ".cs", ".fs", ".vb", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".py", ".rb", ".go", ".rs", ".java", ".kt", ".c", ".h", ".cpp", ".hpp", ".swift",
        ".php", ".sql", ".sh", ".bash", ".ps1", ".psm1", ".bat", ".cmd", ".lua", ".r", ".tex", ".rst", ".adoc", ".gitignore", ".gitattributes", ".editorconfig",
    };
    public static bool IsTextByExtension(string path) => TextExtensions.Contains(Path.GetExtension(path));
    public const long LargeTextBytes = 50L * 1024 * 1024;

    // Inspect a bounded sample on the folder-loading worker, including extensionless text files.
    public static SupportedFileKind Detect(string path)
    {
        if (IsImage(path)) return SupportedFileKind.Image;
        if (IsTextByExtension(path)) return SupportedFileKind.Text;
        if (IsKnownBinary(path)) return SupportedFileKind.Unsupported;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bytes = new byte[8192];
            var length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            // Item 52 (VS Code): a raw NUL byte means binary unless a UTF-16/32 BOM says otherwise.
            var unicodeBom = length >= 2 && (bytes[0] == 0xFF && bytes[1] == 0xFE || bytes[0] == 0xFE && bytes[1] == 0xFF) || length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF;
            if (!unicodeBom && bytes.AsSpan(0, length).Contains((byte)0)) return SupportedFileKind.Unsupported;
            // Same detection as opening (BOM, UTF-16, UTF-8, then the ANSI code page); binary data fails IsText.
            var text = TextFileStore.Decode(bytes.AsSpan(0, length), final: length < bytes.Length).Text;
            return IsText(text) ? SupportedFileKind.Text : SupportedFileKind.Unsupported;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return SupportedFileKind.Unsupported;
        }
    }

    // Item 52 (VS Code): binary when the sample has a NUL, or more than 10% control characters (tab/CR/LF/FF excluded).
    public static bool IsText(ReadOnlySpan<char> text)
    {
        if (text.Length > 8192) text = text[..8192];
        var controls = 0;
        foreach (var character in text)
        {
            if (character == '\0') return false;
            if (char.IsControl(character) && character is not ('\t' or '\r' or '\n' or '\f')) controls++;
        }
        return controls * 10 <= text.Length;
    }
}
