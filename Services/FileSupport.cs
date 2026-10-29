using System.Text;

namespace Mdhub.Services;

public enum SupportedFileKind { Unsupported, Text, Image }

public static class FileSupport
{
    public static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico" or ".tif" or ".tiff";

    // Inspect a bounded sample on the folder-loading worker, including extensionless text files.
    public static SupportedFileKind Detect(string path)
    {
        if (IsImage(path)) return SupportedFileKind.Image;
        if (Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll" or ".pdb" or ".zip" or ".7z" or ".rar"
            or ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx"
            or ".sqlite" or ".db" or ".mp3" or ".mp4" or ".wav" or ".woff" or ".woff2" or ".ttf")
            return SupportedFileKind.Unsupported;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, TextFileStore.DefaultEncoding, detectEncodingFromByteOrderMarks: true);
            var sample = new char[8192];
            var count = reader.ReadBlock(sample, 0, sample.Length);
            return IsText(sample.AsSpan(0, count)) ? SupportedFileKind.Text : SupportedFileKind.Unsupported;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return SupportedFileKind.Unsupported;
        }
    }

    public static bool IsText(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
            if (char.IsControl(character) && character is not ('\t' or '\r' or '\n' or '\f')) return false;
        return true;
    }
}
