using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Mdhub.Services;

public enum AppTheme { Dark, Light }

// App-wide Light/Dark theme. XAML uses {DynamicResource} entries from Styles/Theme.axaml; code-built visuals
// (preview blocks, overlays, dialogs) use Brush(key), a shared brush whose colour is updated on every switch.
public static class ThemeService
{
    private static readonly Dictionary<string, SolidColorBrush> Tracked = [];
    private static readonly List<WeakReference<TopLevel>> Roots = [];
    public static AppTheme Current { get; private set; } = AppTheme.Dark;
    public static event Action<AppTheme>? Changed;

    public static ThemeVariant Variant(AppTheme theme) => theme == AppTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark;
    public static AppTheme Parse(string? value) => string.Equals(value, nameof(AppTheme.Light), StringComparison.OrdinalIgnoreCase) ? AppTheme.Light : AppTheme.Dark;

    public static SolidColorBrush Brush(string key)
    {
        if (!Tracked.TryGetValue(key, out var brush)) Tracked[key] = brush = new SolidColorBrush(Lookup(key));
        return brush;
    }

    public static Color Lookup(string key, AppTheme? theme = null)
    {
        if (Application.Current is { } app && app.TryFindResource(key, Variant(theme ?? Current), out var value))
            return value switch { ISolidColorBrush s => s.Color, Color c => c, _ => Colors.Transparent };
        return Colors.Transparent;
    }

    // Windows whose custom-drawn visuals must repaint on a switch.
    public static void Register(TopLevel root)
    {
        Roots.RemoveAll(r => !r.TryGetTarget(out _));
        Roots.Add(new WeakReference<TopLevel>(root));
    }

    public static void Apply(AppTheme theme, bool persist = true)
    {
        Current = theme;
        if (Application.Current is { } app) app.RequestedThemeVariant = Variant(theme);
        foreach (var (key, brush) in Tracked) brush.Color = Lookup(key);
        foreach (var weak in Roots.ToList())
        {
            if (!weak.TryGetTarget(out var root)) continue;
            foreach (var visual in root.GetVisualDescendants().OfType<Layoutable>()) { visual.InvalidateMeasure(); visual.InvalidateVisual(); }
        }
        Changed?.Invoke(theme);
        if (persist) SessionManager.SaveSettings();
    }
}