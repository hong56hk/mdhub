namespace Mdhub.Services;

// Item 66: insert or overwrite typing, app-wide (every tab and window) and never saved, so each start is Insert.
public static class EditorMode
{
    private static bool _overwrite;
    public static bool Overwrite
    {
        get => _overwrite;
        set { if (_overwrite == value) return; _overwrite = value; Changed?.Invoke(); }
    }
    public static event Action? Changed;
    public static void Toggle() => Overwrite = !Overwrite;
}
