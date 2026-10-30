using System.Collections.Concurrent;
using System.Text;
using Mdhub.Localization;
using UtfUnknown;

namespace Mdhub.Services;

// Item 67 (VS Code): the encodings mdhub reads and writes, the detection order and the per-file cache used by search.
// Detection: BOM -> UTF-16 without BOM -> valid UTF-8 -> UTF.Unknown (Mozilla universal charset detector, MPL 1.1/LGPL)
// when confident -> UI-language fallback (ja Shift_JIS, ko EUC-KR/CP949, zh-Hans GBK, zh-Hant Big5) -> the Default
// encoding setting -> the system ANSI code page (bug 39) -> a low-confidence detector guess -> Latin-1.
public sealed record EncodingOption(string Id, string Name, int CodePage, bool Bom = false)
{
    // Strict: invalid bytes or unencodable characters throw, so a wrong guess never silently corrupts a file.
    public Encoding Create() => CodePage switch
    {
        65001 => new UTF8Encoding(Bom, true),
        1200 => new UnicodeEncoding(false, Bom, true),
        1201 => new UnicodeEncoding(true, Bom, true),
        _ => TextEncodings.Strict(CodePage)!,
    };
}

public static class TextEncodings
{
    static TextEncodings() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static readonly IReadOnlyList<EncodingOption> All =
    [
        new("utf8", "UTF-8", 65001), new("utf8bom", "UTF-8 with BOM", 65001, true),
        new("utf16le", "UTF-16 LE", 1200, true), new("utf16be", "UTF-16 BE", 1201, true),
        new("windows1252", "Windows 1252", 1252), new("windows1251", "Windows 1251", 1251),
        new("iso88591", "ISO 8859-1", 28591), new("iso88592", "ISO 8859-2", 28592), new("iso88595", "ISO 8859-5", 28595),
        new("iso88597", "ISO 8859-7", 28597), new("iso88599", "ISO 8859-9", 28599), new("iso885915", "ISO 8859-15", 28605),
        new("shiftjis", "Shift_JIS", 932), new("eucjp", "EUC-JP", 51932), new("iso2022jp", "ISO-2022-JP", 50220),
        new("euckr", "EUC-KR", 51949), new("cp949", "CP949", 949),
        new("big5", "Big5", 950), new("gbk", "GBK", 936), new("gb18030", "GB18030", 54936), new("gb2312", "GB2312", 20936),
    ];

    public static EncodingOption? Find(string? id) => All.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

    public static Encoding? Strict(int codePage)
    {
        try { return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException) { return null; }
    }

    // Status bar and menus: VS Code style names ("UTF-8", "UTF-8 with BOM", "Big5", "Shift_JIS").
    public static string DisplayName(Encoding encoding)
    {
        var bom = encoding.GetPreamble().Length > 0;
        if (encoding is UTF8Encoding) return bom ? "UTF-8 with BOM" : "UTF-8";
        if (encoding is UTF32Encoding) return encoding.CodePage == 12001 ? "UTF-32 BE" : "UTF-32 LE";
        return All.FirstOrDefault(o => o.CodePage == encoding.CodePage && o.CodePage is not 65001)?.Name ?? encoding.WebName.ToUpperInvariant();
    }

    // The option a document's encoding corresponds to (for menu check marks); null when not in the list (e.g. UTF-32).
    public static EncodingOption? OptionOf(Encoding encoding) =>
        All.FirstOrDefault(o => o.CodePage == encoding.CodePage && (o.CodePage is not 65001 || o.Bom == encoding.GetPreamble().Length > 0));

    // Item 67 setting: the fallback when detection is not confident (and the UI language has no CJK default). UTF-8 by default.
    public const string DefaultId = "utf8";
    public static string DefaultEncodingId { get; private set; } = DefaultId;
    public static event Action? DefaultChanged;
    public static void ImportDefault(string? id) => DefaultEncodingId = Find(id)?.Id ?? DefaultId;
    public static void SetDefault(string id, bool persist = true)
    {
        var next = Find(id)?.Id ?? DefaultId;
        if (next == DefaultEncodingId) return;
        DefaultEncodingId = next;
        DefaultChanged?.Invoke();
#if !FILE_TESTS // the UI-free file tests compile this file without the session manager
        if (persist) SessionManager.SaveSettings();
#endif
    }

    // The UI-language fallback for legacy CJK files (VS Code's behaviour on a CJK system).
    public static int? LanguageCodePage(string language) => language switch
    {
        Loc.Japanese => 932, Loc.Korean => 949, Loc.SimplifiedChinese => 936, Loc.TraditionalChinese => 950, _ => null,
    };

    public const float ConfidentDetection = 0.8f;

    // Candidates after BOM/UTF-16/UTF-8 failed, in order; the caller takes the first that decodes strictly.
    public static IEnumerable<Encoding> LegacyCandidates(ReadOnlySpan<byte> bytes)
    {
        var list = new List<Encoding>();
        void Add(int? codePage) { if (codePage is { } cp && list.All(e => e.CodePage != cp) && Strict(cp) is { } e) list.Add(e); }
        var guesses = new List<(int CodePage, float Confidence)>();
        try
        {
            var sample = bytes[..Math.Min(bytes.Length, 64 * 1024)].ToArray();
            foreach (var detail in CharsetDetector.DetectFromBytes(sample).Details)
                if (detail.Encoding?.CodePage is { } cp && !(cp is >= 10000 and < 10100) && cp is not (20127 or 65001 or 1200 or 1201 or 12000 or 12001))
                    // CP949 and GBK are supersets of the EUC-KR and GB2312 the detector names; GB18030 when GBK fails.
                    foreach (var mapped in cp switch { 51949 => new[] { 949 }, 54936 or 20936 or 52936 => new[] { 936, 54936 }, _ => new[] { cp } })
                        guesses.Add((mapped, detail.Confidence));
        }
        catch (Exception) { /* the detector must never break opening a file */ }
        foreach (var guess in guesses.Where(g => g.Confidence >= ConfidentDetection)) Add(guess.CodePage);
        Add(LanguageCodePage(Loc.Current));
        if (Find(DefaultEncodingId) is { CodePage: not (65001 or 1200 or 1201) } option) Add(option.CodePage);
        Add(TextFileStore.AnsiEncoding()?.CodePage);
        foreach (var guess in guesses) Add(guess.CodePage);
        return list;
    }

    public static bool LooksIso2022Jp(ReadOnlySpan<byte> bytes) =>
        bytes.IndexOf("\u001b$B"u8) >= 0 || bytes.IndexOf("\u001b$@"u8) >= 0 || bytes.IndexOf("\u001b$(D"u8) >= 0;

    // Strict decode without exceptions (wrong guesses are common while detecting; first-chance exceptions are noisy and slow).
    public static bool TryDecode(Encoding encoding, ReadOnlySpan<byte> bytes, out string text)
    {
        if (encoding is UTF8Encoding) { text = System.Text.Unicode.Utf8.IsValid(bytes) ? encoding.GetString(bytes) : ""; return text.Length > 0 || bytes.IsEmpty; }
        var flag = new FlagFallback();
        var probe = (Encoding)encoding.Clone();
        probe.DecoderFallback = flag;
        text = probe.GetString(bytes);
        if (!flag.Failed) return true;
        text = "";
        return false;
    }

    private sealed class FlagFallback : DecoderFallback
    {
        public bool Failed;
        public override int MaxCharCount => 1;
        public override DecoderFallbackBuffer CreateFallbackBuffer() => new Buffer(this);
        private sealed class Buffer(FlagFallback owner) : DecoderFallbackBuffer
        {
            private bool _pending;
            public override bool Fallback(byte[] bytesUnknown, int index) { owner.Failed = true; _pending = true; return true; }
            public override char GetNextChar() { if (!_pending) return '\0'; _pending = false; return '\uFFFD'; }
            public override bool MovePrevious() => false;
            public override int Remaining => _pending ? 1 : 0;
        }
    }

    // Decodes with a chosen encoding (Reopen with Encoding); a matching BOM is skipped. Throws DecoderFallbackException.
    public static string DecodeWith(ReadOnlySpan<byte> bytes, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0 && encoding is UTF8Encoding or UnicodeEncoding)
            preamble = encoding is UTF8Encoding ? [0xEF, 0xBB, 0xBF] : encoding.CodePage == 1201 ? [0xFE, 0xFF] : [0xFF, 0xFE];
        if (preamble.Length > 0 && bytes.StartsWith(preamble)) bytes = bytes[preamble.Length..];
        return encoding.GetString(bytes);
    }

    // First character the encoding cannot represent, or null when the whole text encodes.
    public static string? FirstUnencodable(string text, Encoding encoding)
    {
        if (encoding is UTF8Encoding or UnicodeEncoding or UTF32Encoding) return null;
        try { encoding.GetByteCount(text); return null; }
        catch (EncoderFallbackException ex) { return ex.CharUnknownHigh != '\0' ? new string([ex.CharUnknownHigh, ex.CharUnknownLow]) : ex.CharUnknown.ToString(); }
    }

    // Item 67 / item 46 speed: search decodes legacy files with the same detection as opening, remembering the
    // detected encoding per path, size and modification time so a repeated search skips the detector.
    private static readonly ConcurrentDictionary<string, (DateTime Modified, long Length, Encoding Encoding)> Cache = new(StringComparer.OrdinalIgnoreCase);
    public static int CacheHits;
    public static void ClearCache() { Cache.Clear(); CacheHits = 0; }

    public static (string Text, Encoding Encoding, bool Lossy) DecodeForSearch(string path, FileInfo info, byte[] bytes)
    {
        // A BOM decides on its own: the lenient decode as before (U+FFFD for bad bytes, Lossy, never rewritten).
        if (bytes is [0xEF, 0xBB, 0xBF, ..] or [0xFF, 0xFE, ..] or [0xFE, 0xFF, ..] or [0x00, 0x00, 0xFE, 0xFF, ..]) return TextFileStore.DecodeLenient(bytes);
        if (Cache.TryGetValue(path, out var cached) && cached.Modified == info.LastWriteTimeUtc && cached.Length == bytes.Length && TryDecode(cached.Encoding, bytes, out var known))
        {
            Interlocked.Increment(ref CacheHits);
            return (known, cached.Encoding, false);
        }
        try
        {
            var (text, encoding) = TextFileStore.Decode(bytes);
            Cache[path] = (info.LastWriteTimeUtc, bytes.Length, encoding);
            return (text, encoding, false);
        }
        catch (DecoderFallbackException) { return TextFileStore.DecodeLenient(bytes); }
    }
}