using System.Globalization;

namespace Mdhub.Localization;

// A UI language: its code, its name written in itself (shown untranslated in Settings > Language), the culture
// used for number formatting, and the fonts that render it (CJK fonts first so Han characters use that
// language's glyph forms; the default Latin font comes before them at runtime).
public sealed record LanguageInfo(string Code, string NativeName, string Culture, string Fonts);

// UI-free string tables and the current language. XAML reads the same keys through DynamicResource
// (LocResources copies the active table into the application resources), code calls T/F.
public static partial class Loc
{
    public const string English = "en", TraditionalChinese = "zh-Hant", Japanese = "ja", Korean = "ko";

    public static readonly IReadOnlyList<LanguageInfo> Languages =
    [
        new(English, "English", "en-US", ""),
        new(TraditionalChinese, "繁體中文", "zh-TW", "Microsoft JhengHei UI, Microsoft JhengHei, PingFang TC, Noto Sans CJK TC, Noto Sans TC"),
        new(Japanese, "日本語", "ja-JP", "Yu Gothic UI, Meiryo UI, Meiryo, Hiragino Sans, Noto Sans CJK JP, Noto Sans JP"),
        new(Korean, "한국어", "ko-KR", "Malgun Gothic, Apple SD Gothic Neo, Noto Sans CJK KR, Noto Sans KR"),
    ];

    public static string Current { get; private set; } = English;
    // The language the user picked in Settings; null until then (the OS language is used and nothing is saved).
    public static string? Chosen { get; set; }
    public static LanguageInfo Info => Languages.First(l => l.Code == Current);
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Info.Culture);
    public static event Action? Changed;

    public static IReadOnlyDictionary<string, string> Table(string code) => code switch
    {
        TraditionalChinese => ZhHant, Japanese => Ja, Korean => Ko, _ => En
    };

    // A supported code (case-insensitive) or null.
    public static string? Normalize(string? code) => Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Code;

    // OS UI culture to a supported language: Traditional Chinese (zh-TW/HK/MO or zh-Hant), Japanese, Korean; anything else
    // (including Simplified Chinese) is English.
    public static string Detect(string? cultureName)
    {
        var name = (cultureName ?? "").Replace('_', '-').ToLowerInvariant();
        if (name.StartsWith("zh"))
            return name.Contains("hant") || name.EndsWith("-tw") || name.EndsWith("-hk") || name.EndsWith("-mo") ? TraditionalChinese : English;
        if (name == "ja" || name.StartsWith("ja-")) return Japanese;
        if (name == "ko" || name.StartsWith("ko-")) return Korean;
        return English;
    }

    public static void Set(string? code)
    {
        var next = Normalize(code) ?? English;
        if (next == Current) return;
        Current = next;
        Changed?.Invoke();
    }

    public static string T(string key) =>
        Table(Current).TryGetValue(key, out var value) || En.TryGetValue(key, out value) ? value : key;

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    // English singular/plural; the CJK tables use the same text for both.
    public static string Plural(string one, string many, int count) => F(count == 1 ? one : many, count);
}