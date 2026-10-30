using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Mdhub.Localization;

// Publishes the current language's strings as application resources so XAML DynamicResource lookups switch live in
// every window, and sets a CJK font chain (default font first, then that language's fonts) so nothing renders as boxes.
public static class LocResources
{
    private static Application? _app;
    private static ResourceDictionary? _current;
    // Menu items built in code (context menus are not in the tree until opened) and the key of their header.
    private static readonly ConditionalWeakTable<MenuItem, string> MenuKeys = new();

    public static void Install(Application app)
    {
        _app = app;
        Loc.Changed -= Apply;
        Loc.Changed += Apply;
        Apply();
    }

    private static void Apply()
    {
        if (_app is null) return;
        var next = new ResourceDictionary();
        foreach (var key in Loc.En.Keys) next[key] = Loc.T(key);
        if (FontFor(Loc.Info) is { } font) next["ContentControlThemeFontFamily"] = font;
        // One swap per change, so every window re-reads its resources once.
        if (_current is not null) _app.Resources.MergedDictionaries.Remove(_current);
        _app.Resources.MergedDictionaries.Add(_current = next);
    }

    public static FontFamily? FontFor(LanguageInfo language) =>
        language.Fonts.Length == 0 ? null : new FontFamily(DefaultFontName + ", " + language.Fonts);

    private static string DefaultFontName
    {
        get
        {
            try { return FontManager.Current.DefaultFontFamily.Name; }
            catch (Exception) { return "Segoe UI"; }
        }
    }

    // Settings > Language: switch now and remember the choice.
    public static void Choose(string code)
    {
        if (Loc.Normalize(code) is not { } normalized) return;
        Loc.Chosen = normalized;
        Loc.Set(normalized);
        Services.SessionManager.SaveSettings();
    }

    public static MenuItem Item(string key)
    {
        var item = new MenuItem { Header = Loc.T(key) };
        MenuKeys.AddOrUpdate(item, key);
        return item;
    }

    // Refreshes the headers of code-built items (call when the menu opens).
    public static void Refresh(ItemsControl menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (MenuKeys.TryGetValue(item, out var key)) item.Header = Loc.T(key);
            Refresh(item);
        }
    }

    // Item 45: the padding every context menu gets, set as a local value so it applies from the very first measure
    // (a style can land after the popup first lays out, which left the first open without padding).
    public static readonly Thickness MenuPadding = new(10);

    public static ContextMenu Localized(this ContextMenu menu)
    {
        menu.Padding = MenuPadding;
        // Fluent's template puts a 4px margin above and below the items; drop it so the padding is 10px on every side.
        // TemplateApplied runs before the popup's first measure, so the first open matches later ones.
        menu.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Avalonia.Controls.Presenters.ItemsPresenter>("PART_ItemsPresenter") is { } items) items.Margin = default;
        };
        menu.Opening += (_, _) => Refresh(menu);
        return menu;
    }
}