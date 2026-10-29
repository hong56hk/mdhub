using System.Text;

namespace Mdhub.Services;

// No UI dependencies: the filesystem behavior can be tested independently.
public static class TextFileStore
{
    public static Encoding DefaultEncoding => new UTF8Encoding(false, true);

    public static async Task<(string Text, Encoding Encoding)> ReadAsync(string path)
    {
        using var reader = new StreamReader(path, DefaultEncoding, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync();
        return (text, reader.CurrentEncoding);
    }

    // Search reads never throw on bad bytes: a BOM picks the encoding, else valid UTF-8, else UTF-8 with
    // undecodable bytes replaced by U+FFFD (Lossy = true; such text must not be written back).
    // Encoding is the strict encoding to save with, matching ReadAsync.
    public static (string Text, Encoding Encoding, bool Lossy) DecodeLenient(ReadOnlySpan<byte> bytes)
    {
        (Encoding Lenient, Encoding Strict, int Bom) choice = bytes switch
        {
            [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(false, false), new UTF8Encoding(true, true), 3),
            [0xFF, 0xFE, 0x00, 0x00, ..] => (new UTF32Encoding(false, false, false), new UTF32Encoding(false, true, true), 4),
            [0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(true, false, false), new UTF32Encoding(true, true, true), 4),
            [0xFF, 0xFE, ..] => (new UnicodeEncoding(false, false, false), new UnicodeEncoding(false, true, true), 2),
            [0xFE, 0xFF, ..] => (new UnicodeEncoding(true, false, false), new UnicodeEncoding(true, true, true), 2),
            _ => (new UTF8Encoding(false, false), DefaultEncoding, 0)
        };
        var body = bytes[choice.Bom..];
        var text = choice.Lenient.GetString(body);
        // UTF-16/32 have no cheap validity check; a literal U+FFFD in such a file is conservatively treated as lossy.
        var lossy = choice.Lenient is UTF8Encoding ? !System.Text.Unicode.Utf8.IsValid(body) : text.Contains('\uFFFD');
        return (text, choice.Strict, lossy);
    }

    public static async Task<(string Text, Encoding Encoding, bool Lossy)> ReadLenientAsync(string path) => DecodeLenient(await File.ReadAllBytesAsync(path));

    public static async Task WriteAsync(string path, string text, Encoding encoding)
    {
        // Write beside the destination, then replace it. A failed write must not truncate the original.
        var fullPath = Path.GetFullPath(path);
        var temporaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, $".mdhub-{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, text, encoding);
            if (File.Exists(fullPath))
                File.Replace(temporaryPath, fullPath, null);
            else
                File.Move(temporaryPath, fullPath);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
