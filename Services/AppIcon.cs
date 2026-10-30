using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Mdhub.Services;

// Item 34: the app logo. Every window (main and dialogs) gets the icon in one place: ThemeService.PrepareTitleBar
// and the Opened hook both call Apply.
public static class AppIcon
{
    // Avalonia embeds <ApplicationIcon> under this name (AvaloniaIncludeApplicationIconAsWindowIcon); listing the .ico
    // again as an AvaloniaResource duplicates it and silently drops the compiled XAML.
    public static readonly Uri IconUri = new("avares://Mdhub/!__AvaloniaDefaultWindowIcon");
    public static readonly Uri LogoUri = new("avares://Mdhub/Assets/mdhub-128.png");

    private static WindowIcon? _icon;
    private static bool _loaded;

    // Null when the platform has no icon support (it then keeps its default).
    public static WindowIcon? Icon
    {
        get
        {
            if (_loaded) return _icon;
            _loaded = true;
            try { using var stream = AssetLoader.Open(IconUri); _icon = new WindowIcon(stream); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or ArgumentException) { _icon = null; }
            return _icon;
        }
    }

    public static void Apply(Window window)
    {
        if (window.Icon is null && Icon is { } icon) window.Icon = icon;
    }

    // The logo for the About dialog and other in-app places.
    public static Bitmap Logo() { using var stream = AssetLoader.Open(LogoUri); return new Bitmap(stream); }
}