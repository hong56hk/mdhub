using System.Globalization;

namespace Mdhub.Services;

// Item 35: preview typography (gear flyout). The saved values (PreviewSettingsData) live in SessionStore.cs.
public static class PreviewSettings
{
    public const double DefaultBodySize = 15, MinBodySize = 10, MaxBodySize = 28;
    public const string DefaultCodeFamily = "Consolas";
    public const string CodeFallback = "Consolas, Menlo, monospace";
    public const double DefaultCodeSize = 13, MinCodeSize = 9, MaxCodeSize = 24;
    public const double DefaultLineHeight = 1.6, MinLineHeight = 1.0, MaxLineHeight = 2.5;
    public const double DefaultParagraphSpacing = 14, MinParagraphSpacing = 0, MaxParagraphSpacing = 32;
    // Nested stacks (list items, quotes, cells) were 8 px against the 14 px top level: keep that ratio.
    public const double NestedRatio = 8.0 / 14.0;
    public const double NumberScale = 1.10, BulletScale = 1.15;

    public static PreviewSettingsData Current { get; private set; } = new();
    public static event Action? Changed;
    public static bool IsDefault => Current == new PreviewSettingsData();

    public static PreviewSettingsData Normalize(PreviewSettingsData? data)
    {
        data ??= new PreviewSettingsData();
        static double Snap(double v, double min, double max, double step, double fallback) =>
            double.IsFinite(v) ? Math.Round(Math.Clamp(Math.Round((v - min) / step) * step + min, min, max), 2) : fallback;
        return new PreviewSettingsData
        {
            BodyFamily = string.IsNullOrWhiteSpace(data.BodyFamily) ? null : data.BodyFamily.Trim(),
            BodySize = Snap(data.BodySize, MinBodySize, MaxBodySize, 1, DefaultBodySize),
            CodeFamily = string.IsNullOrWhiteSpace(data.CodeFamily) ? DefaultCodeFamily : data.CodeFamily.Trim(),
            CodeSize = Snap(data.CodeSize, MinCodeSize, MaxCodeSize, 1, DefaultCodeSize),
            LineHeight = Snap(data.LineHeight, MinLineHeight, MaxLineHeight, 0.1, DefaultLineHeight),
            ParagraphSpacing = Snap(data.ParagraphSpacing, MinParagraphSpacing, MaxParagraphSpacing, 2, DefaultParagraphSpacing),
        };
    }

    // Loading a session: no event, no save.
    public static void Import(PreviewSettingsData? data) => Current = Normalize(data);

    public static void Update(Func<PreviewSettingsData, PreviewSettingsData> change, bool persist = true)
    {
        var next = Normalize(change(Current));
        if (next == Current) return;
        Current = next;
        Changed?.Invoke();
        if (persist) SessionManager.SaveSettings();
    }

    public static void Reset(bool persist = true) => Update(_ => new PreviewSettingsData(), persist);

    public static double BodyLineHeight => Math.Round(Current.BodySize * Current.LineHeight, 2);
    public static double NestedSpacing => Math.Round(Current.ParagraphSpacing * NestedRatio, 1);
    // The chosen code font first, then the monospace fallback chain.
    public static string CodeFamilyList => Current.CodeFamily == DefaultCodeFamily ? CodeFallback : Current.CodeFamily + ", " + CodeFallback;

    // CSS values for item 51's export.
    internal static string Css(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    internal static string CssFamily(string family) => "'" + family.Replace("\\", "").Replace("'", "") + "'";
}