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
        ApplyTitleBars();
        Changed?.Invoke(theme);
        if (persist) SessionManager.SaveSettings();
    }

    // ---- Title bars follow the app theme on every window (item 32). Windows only: DWM's immersive dark mode
    // attribute; other systems keep their own title bar. TitleBarDark records what was asked for each window
    // (headless tests have no HWND, so this is what they can check).
    private const int DwmUseImmersiveDarkMode = 20;
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, object> TitleBars = new();
    private static readonly List<WeakReference<Window>> Windows = [];
    private static bool _titleBarHooked;

    public static bool? TitleBarDark(Window window) => TitleBars.TryGetValue(window, out var dark) ? (bool)dark : null;

    // Every window, dialog and prompt goes through here when it opens (a class handler, so none can be missed).
    public static void HookTitleBars()
    {
        if (_titleBarHooked) return;
        _titleBarHooked = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, args) =>
        {
            Windows.RemoveAll(w => !w.TryGetTarget(out _));
            Windows.Add(new WeakReference<Window>(window));
            ApplyTitleBar(window);
        });
    }

    public static void ApplyTitleBar(Window window)
    {
        var dark = Current == AppTheme.Dark;
        TitleBars.AddOrUpdate(window, dark);
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" } handle && handle.Handle != IntPtr.Zero)
            {
                var value = dark ? 1 : 0;
                DwmSetWindowAttribute(handle.Handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));
            }
        }
        catch (Exception) { /* Older Windows without the attribute (or without dwmapi) keep the default title bar. */ }
    }

    private static void ApplyTitleBars()
    {
        foreach (var weak in Windows.ToList())
            if (weak.TryGetTarget(out var window) && window.IsVisible) ApplyTitleBar(window);
    }
}