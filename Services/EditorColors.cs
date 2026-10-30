using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mdhub.Services;

// Item 62: one editor colour the gear can change, and the theme resource keys it overrides.
public sealed record EditorColorItem(string Id, string Label, IReadOnlyList<string> Keys);

// Item 62: the editor gear's colours. Overrides are kept per theme and installed as Application resources
// (Resources.ThemeDictionaries[Dark/Light]) in front of Styles/Theme.axaml, so a theme switch shows that theme's own values
// and a reset falls back to the palette. Nothing is hard-coded: the defaults are the theme resources.
public static class EditorColors
{
    public static readonly IReadOnlyList<EditorColorItem> Items =
    [
        new("Background", "EditorColorBackground", ["EditorTextBackground"]),
        new("Text", "EditorColorText", ["EditorForeground"]),
        new("Headings", "EditorColorHeadings", ["SyntaxH1", "SyntaxH2", "SyntaxH3", "SyntaxH4"]),
        new("OrderedNumbers", "EditorColorOrdered", ["SyntaxOrderedMarker"]),
        new("Bullets", "EditorColorBullets", ["SyntaxListMarker"]),
        new("Links", "EditorColorLinks", ["SyntaxLink"]),
        new("Bold", "EditorColorBold", ["SyntaxBold"]),
        new("Italic", "EditorColorItalic", ["SyntaxItalic"]),
        new("Strikethrough", "EditorColorStrike", ["SyntaxStrike"]),
        new("InlineCode", "EditorColorInlineCode", ["SyntaxInlineCode"]),
        new("CodeBlocks", "EditorColorCodeBlocks", ["SyntaxCodeText"]),
        new("Markers", "EditorColorMarkers", ["SyntaxMarker"]),
        new("Selection", "EditorColorSelection", ["EditorSelectionBackground"]),
    ];
    private static readonly Dictionary<AppTheme, Dictionary<string, Color>> Overrides = new() { [AppTheme.Dark] = [], [AppTheme.Light] = [] };
    public static event Action? Changed;

    public static EditorColorItem Item(string id) => Items.First(i => i.Id == id);
    public static bool IsDefault => Overrides.Values.All(o => o.Count == 0);
    public static bool HasOverrides(AppTheme? theme = null) => Overrides[theme ?? ThemeService.Current].Count > 0;
    public static bool IsOverridden(string id, AppTheme? theme = null) => Overrides[theme ?? ThemeService.Current].ContainsKey(id);
    // The theme's own value (the first key; headings show H1).
    public static Color Default(string id, AppTheme? theme = null) => ThemeService.ThemeDefault(Item(id).Keys[0], theme ?? ThemeService.Current);
    public static Color Get(string id, AppTheme? theme = null) =>
        Overrides[theme ?? ThemeService.Current].TryGetValue(id, out var c) ? c : Default(id, theme);

    // A colour equal to the theme's value is stored as no override.
    public static void Set(string id, Color? color, AppTheme? theme = null, bool persist = true)
    {
        var t = theme ?? ThemeService.Current;
        if (color is { } c && Item(id).Keys.Any(k => ThemeService.ThemeDefault(k, t) != c)) Overrides[t][id] = c;
        else Overrides[t].Remove(id);
        Install(); Changed?.Invoke();
        if (persist) SessionManager.SaveSettings();
    }
    public static void Reset(string id, AppTheme? theme = null, bool persist = true) => Set(id, null, theme, persist);
    public static void ResetAll(AppTheme? theme = null, bool persist = true)
    {
        Overrides[theme ?? ThemeService.Current].Clear();
        Install(); Changed?.Invoke();
        if (persist) SessionManager.SaveSettings();
    }

    // Session load (no save): unknown ids and unparsable values are skipped.
    public static void Import(EditorColorsData? data)
    {
        foreach (var o in Overrides.Values) o.Clear();
        void Read(AppTheme theme, Dictionary<string, string>? values)
        {
            foreach (var (id, text) in values ?? [])
                if (Items.Any(i => i.Id == id) && Color.TryParse(text, out var c)) Overrides[theme][id] = c;
        }
        Read(AppTheme.Dark, data?.Dark); Read(AppTheme.Light, data?.Light);
        Install(); Changed?.Invoke();
    }
    public static EditorColorsData? Export()
    {
        if (IsDefault) return null;
        Dictionary<string, string>? Write(AppTheme theme) => Overrides[theme].Count == 0 ? null : Overrides[theme].ToDictionary(p => p.Key, p => p.Value.ToString());
        return new EditorColorsData { Dark = Write(AppTheme.Dark), Light = Write(AppTheme.Light) };
    }

    // Application.Resources.ThemeDictionaries are searched before the merged Theme.axaml, per theme variant.
    private static void Install()
    {
        if (Application.Current is not { } app) return;
        foreach (var (theme, overrides) in Overrides)
        {
            var dictionary = new ResourceDictionary();
            foreach (var (id, color) in overrides)
                foreach (var key in Item(id).Keys) dictionary[key] = new SolidColorBrush(color);
            app.Resources.ThemeDictionaries[ThemeService.Variant(theme)] = dictionary;
        }
        ThemeService.Refresh();
    }
}