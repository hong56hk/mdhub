using System.Runtime.InteropServices;
using System.Text;

namespace Mdhub.Services;

// No UI dependencies: the filesystem behavior can be tested independently.
public static class TextFileStore
{
    static TextFileStore() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding DefaultEncoding => new UTF8Encoding(false, true);

    // Test seam: the ANSI code page legacy files fall back to (null = the system's, e.g. cp950 on a Traditional Chinese Windows).
    public static int? AnsiCodePageOverride { get; set; }

    [DllImport("kernel32.dll")] private static extern int GetACP();

    // The system ANSI code page as a strict encoding (invalid bytes or unencodable characters throw), or null when
    // the system code page is UTF-8 or unavailable.
    public static Encoding? AnsiEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        int codePage;
        try { codePage = AnsiCodePageOverride ?? (OperatingSystem.IsWindows() ? GetACP() : 1252); }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { codePage = 1252; }
        if (codePage is 65001 or 0) codePage = 1252;
        try { return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return null; }
    }

    public static async Task<(string Text, Encoding Encoding)> ReadAsync(string path) => Decode(await File.ReadAllBytesAsync(path));

    // Bug 39 / item 67: a BOM picks UTF-8/16/32; else UTF-16 without a BOM by its NUL pattern; else valid UTF-8; else the
    // legacy candidates of TextEncodings (confident detector guess, UI language, Default encoding, system ANSI code page
    // such as Big5/cp950, then a weak guess), else Latin-1. The returned encoding is strict and is used to save, so the file
    // keeps its encoding (and BOM or no BOM).
    // final = false: bytes is a sample cut from a longer file, so a sequence split at its end is ignored.
    public static (string Text, Encoding Encoding) Decode(ReadOnlySpan<byte> bytes, bool final = true)
    {
        (Encoding Encoding, int Bom)? bom = bytes switch
        {
            [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(true, true), 3),
            [0xFF, 0xFE, 0x00, 0x00, ..] => (new UTF32Encoding(false, true, true), 4),
            [0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(true, true, true), 4),
            [0xFF, 0xFE, ..] => (new UnicodeEncoding(false, true, true), 2),
            [0xFE, 0xFF, ..] => (new UnicodeEncoding(true, true, true), 2),
            _ => null
        };
        if (bom is { } b) return (b.Encoding.GetString(Trim(bytes[b.Bom..], b.Encoding, final)), b.Encoding);
        if (Utf16Endianness(bytes) is { } bigEndian)
        {
            var utf16 = new UnicodeEncoding(bigEndian, false, true);
            if (TextEncodings.TryDecode(utf16, Trim(bytes, utf16, final), out var text16)) return (text16, utf16); // item 67: no first-chance exception
        }
        // Item 67: ISO-2022-JP is 7-bit (valid UTF-8 too); its ESC $B / ESC $@ designations identify it.
        if (TextEncodings.LooksIso2022Jp(bytes) && TextEncodings.Strict(50220) is { } jis && TextEncodings.TryDecode(jis, bytes, out var textJis)) return (textJis, jis);
        var utf8 = DefaultEncoding;
        var body = Trim(bytes, utf8, final);
        if (System.Text.Unicode.Utf8.IsValid(body)) return (utf8.GetString(body), utf8);
        foreach (var legacy in TextEncodings.LegacyCandidates(bytes))
            if (TextEncodings.TryDecode(legacy, Trim(bytes, legacy, final), out var text)) return (text, legacy);
        // Last resort: Latin-1 maps every byte to one character, so any file opens and saves back byte for byte.
        var latin1 = Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        return (latin1.GetString(bytes), latin1);
    }

    // UTF-16 without a BOM: mostly-ASCII text has a NUL in nearly every high byte and almost none in the low bytes.
    // Item 67: gates the binary check, so tiny samples ('ab\0c') do not count as UTF-16.
    public static bool LooksUtf16(ReadOnlySpan<byte> bytes) => bytes.Length >= 16 && Utf16Endianness(bytes) is not null;
    private static bool? Utf16Endianness(ReadOnlySpan<byte> bytes)
    {
        var length = Math.Min(bytes.Length, 4096) & ~1;
        if (length < 4) return null;
        int evenZeros = 0, oddZeros = 0;
        for (var i = 0; i < length; i += 2) { if (bytes[i] == 0) evenZeros++; if (bytes[i + 1] == 0) oddZeros++; }
        var pairs = length / 2;
        if (oddZeros >= pairs * 0.4 && evenZeros <= pairs * 0.05) return false; // little endian: 'a' 00
        if (evenZeros >= pairs * 0.4 && oddZeros <= pairs * 0.05) return true;  // big endian: 00 'a'
        return null;
    }

    // A sample may end inside a multi-byte sequence: drop up to 3 trailing bytes until the rest decodes.
    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> bytes, Encoding encoding, bool final)
    {
        if (final) return bytes;
        if (encoding is UnicodeEncoding) return bytes[..(bytes.Length & ~1)];
        if (encoding is UTF32Encoding) return bytes[..(bytes.Length & ~3)];
        for (var cut = 0; cut <= Math.Min(3, bytes.Length); cut++)
        {
            var part = bytes[..^cut];
            if (encoding is UTF8Encoding ? System.Text.Unicode.Utf8.IsValid(part) : Decodes(encoding, part)) return part;
        }
        return bytes;
    }

    private static bool Decodes(Encoding encoding, ReadOnlySpan<byte> bytes)
    {
        return TextEncodings.TryDecode(encoding, bytes, out _);
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
