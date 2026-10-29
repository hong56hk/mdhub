namespace Mdhub.Services;

public readonly record struct ScreenArea(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

// Pure window placement rules: a saved position is kept only if enough of the window's title area is on some screen.
public static class WindowPlacement
{
    public const int MinimumVisibleWidth = 120, MinimumVisibleHeight = 32;

    public static bool IsOnScreen(int x, int y, int width, IReadOnlyList<ScreenArea> screens)
    {
        // The top strip (title bar / menu) must be reachable so the window can be moved.
        var stripWidth = Math.Max(width, 1);
        foreach (var screen in screens)
        {
            var visibleWidth = Math.Min(x + stripWidth, screen.Right) - Math.Max(x, screen.X);
            var visibleHeight = Math.Min(y + MinimumVisibleHeight, screen.Bottom) - Math.Max(y, screen.Y);
            if (visibleWidth >= Math.Min(MinimumVisibleWidth, stripWidth) && visibleHeight >= MinimumVisibleHeight) return true;
        }
        return false;
    }

    // Keeps an on-screen position; otherwise centers the window on the primary screen, clamped inside it.
    public static (int X, int Y) Place(int x, int y, int width, int height, IReadOnlyList<ScreenArea> screens, ScreenArea primary)
    {
        if (screens.Count == 0 || IsOnScreen(x, y, width, screens)) return (x, y);
        var px = primary.X + Math.Max(0, (primary.Width - width) / 2);
        var py = primary.Y + Math.Max(0, (primary.Height - height) / 2);
        return (px, py);
    }
}