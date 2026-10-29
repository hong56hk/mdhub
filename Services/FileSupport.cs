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

    // Inspect a bounded sample on the folder-loading worker, including extensionless text files.
    public static SupportedFileKind Detect(string path)
    {
        if (IsImage(path)) return SupportedFileKind.Image;
        if (IsKnownBinary(path)) return SupportedFileKind.Unsupported;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Decode the sample without exceptions: invalid UTF-8 (e.g. cp1252 text) stays Unsupported for opening,
            // but no longer raises a DecoderFallbackException per file while a folder loads.
            var bytes = new byte[8192];
            var length = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
            var sample = bytes.AsSpan(0, length);
            var final = length < bytes.Length;
            if (sample is [0xEF, 0xBB, 0xBF, ..] || sample is not ([0xFF, 0xFE, ..] or [0xFE, 0xFF, ..] or [0x00, 0x00, 0xFE, 0xFF, ..]))
            {
                var utf8 = sample is [0xEF, 0xBB, 0xBF, ..] ? sample[3..] : sample;
                var chars = new char[utf8.Length];
                var status = System.Text.Unicode.Utf8.ToUtf16(utf8, chars, out _, out var written, replaceInvalidSequences: false, isFinalBlock: final);
                if (status == System.Buffers.OperationStatus.InvalidData) return SupportedFileKind.Unsupported;
                return IsText(chars.AsSpan(0, written)) ? SupportedFileKind.Text : SupportedFileKind.Unsupported;
            }
            var decoded = TextFileStore.DecodeLenient(sample);
            var text = decoded.Text.AsSpan();
            if (!final && text.Length > 0 && text[^1] == '\uFFFD') text = text[..^1]; // a code unit split by the sample boundary
            return !text.Contains('\uFFFD') && IsText(text) ? SupportedFileKind.Text : SupportedFileKind.Unsupported;
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
