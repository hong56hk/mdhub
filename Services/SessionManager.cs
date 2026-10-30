using Avalonia;
using Avalonia.Controls;

namespace Mdhub.Services;

// Collects each window's session as it closes and writes session.json, so closing windows one at a time
// keeps the earlier ones; restores them on the next launch. Serialization and validation live in SessionStore.
public static class SessionManager
{
    private sealed class Entry(MainWindow window)
    {
        public MainWindow Window { get; } = window;
        public WindowSession? Snapshot { get; set; }
    }

    private static readonly List<Entry> Entries = [];

    // Off unless the app (or a test) turns it on, so stray windows never touch the real session file.
    public static bool Enabled { get; set; }
    public static string? PathOverride { get; set; }
    public static string SessionPath => PathOverride ?? SessionStore.DefaultPath;

    public static void Track(MainWindow window)
    {
        if (!Enabled || Entries.Any(e => e.Window == window)) return;
        Entries.Add(new Entry(window));
        window.Closing += (_, e) => { if (!e.Cancel) Capture(window); };
        window.Closed += (_, _) => WindowClosed(window);
    }

    // Called once a close has been accepted (after the unsaved prompt), so a cancelled close changes nothing.
    private static void Capture(MainWindow window)
    {
        if (Entries.FirstOrDefault(e => e.Window == window) is { } entry) FolderTabsStore.Record(FolderTabsStore.PathFor(SessionPath), entry.Snapshot = window.CaptureSession());
    }

    private static bool _shuttingDown;

    // Only windows open when the app quits come back. A non-final close drops that window from the session file
    // (written right away, so a killed process still restores the windows that were open); the last close writes itself.
    private static void WindowClosed(MainWindow window)
    {
        if (Entries.FirstOrDefault(e => e.Window == window) is not { } entry) return;
        Entries.Remove(entry);
        if (_shuttingDown) { if (Entries.Count == 0) _shuttingDown = false; return; }
        var windows = Entries.Count == 0
            ? [entry.Snapshot ?? new WindowSession()]
            : Entries.Select(e => e.Window.CaptureSession()).ToList();
        Write(windows);
    }

    // Shutdown (Exit, log off) closes every window: save them all now so the closes that follow do not drop any.
    public static void BeginShutdown()
    {
        if (!Enabled || Entries.Count == 0) return;
        var windows = Entries.Select(e => e.Window.CaptureSession()).ToList();
        foreach (var session in windows) FolderTabsStore.Record(FolderTabsStore.PathFor(SessionPath), session); // item 64
        Write(windows);
        _shuttingDown = true;
    }

    private static void Write(List<WindowSession> windows)
    {
        try { SessionStore.Save(SessionPath, new SessionData { Windows = windows.Where(w => !w.IsEmpty).ToList(), Theme = ThemeService.Current.ToString(), Language = Mdhub.Localization.Loc.Chosen,
                RecentFiles = [.. RecentService.Files], RecentFolders = [.. RecentService.Folders], CustomShortcuts = Shortcuts.AnyCustomized ? Shortcuts.Export() : null,
                Preview = PreviewSettings.IsDefault ? null : PreviewSettings.Current,
                EditorFonts = EditorFonts.IsDefault ? null : EditorFonts.Current,
                EditorColors = EditorColors.Export() }); }
        catch (Exception) { /* A session that cannot be written must not block closing. */ }
    }

    // Theme, language and shortcut changes are saved right away with the open windows, so a crash keeps the choice.
    public static void SaveSettings()
    {
        if (!Enabled) return;
        Write(Entries.Count > 0 ? Entries.Select(e => e.Window.CaptureSession()).ToList() : SessionStore.Load(SessionPath)?.Windows ?? []);
    }

    // Open Recent changes are saved right away but keep the saved windows as they are: open windows are still
    // only written when they close, so a session file never lists windows that were open at a crash.
    public static void SaveRecent()
    {
        if (!Enabled) return;
        Write(SessionStore.Load(SessionPath)?.Windows ?? []);
    }

    public static void Reset() { Entries.Clear(); _shuttingDown = false; }

    // Builds windows for every valid saved window; empty when there is nothing to restore.
    // Each window loads its folder and tabs when it opens; skipped items are noted in the first window's status bar.
    public static List<MainWindow> Restore(Func<MainWindow>? factory = null)
    {
        var (sessions, skipped) = SessionStore.Validate(SessionStore.Load(SessionPath), Directory.Exists, File.Exists);
        var windows = new List<MainWindow>();
        foreach (var session in sessions)
        {
            var window = (factory ?? (() => new MainWindow()))();
            window.ApplyBounds(session);
            var note = windows.Count == 0 && skipped > 0
                ? Mdhub.Localization.Loc.Plural("SessionSkippedOne", "SessionSkippedMany", skipped)
                : null;
            window.RestoreOnOpen(session, note);
            windows.Add(window);
        }
        return windows;
    }

    public static WindowSession Bounds(Window window, WindowSession session)
    {
        var width = double.IsNaN(window.Width) ? window.ClientSize.Width : window.Width;
        var height = double.IsNaN(window.Height) ? window.ClientSize.Height : window.Height;
        var state = window.WindowState is WindowState.Maximized or WindowState.FullScreen ? window.WindowState.ToString() : "Normal";
        return session with { X = window.Position.X, Y = window.Position.Y, Width = width, Height = height, State = state,
            Zoom = window is MainWindow main ? main.ZoomPercent : ZoomLevel.Default };
    }

    public static void Apply(Window window, WindowSession session)
    {
        if (window is MainWindow main) main.SetZoom(session.Zoom, announce: false);
        if (session.Width >= 320 && session.Height >= 240 && session.Width < 20000 && session.Height < 20000)
        {
            window.Width = session.Width;
            window.Height = session.Height;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            var (x, y) = ((int)session.X, (int)session.Y);
            if (window.Screens is { } screens && screens.All.Count > 0)
            {
                ScreenArea Area(Avalonia.Platform.Screen s) => new(s.WorkingArea.X, s.WorkingArea.Y, s.WorkingArea.Width, s.WorkingArea.Height);
                var primary = screens.Primary ?? screens.All[0];
                (x, y) = WindowPlacement.Place(x, y, (int)session.Width, (int)session.Height, screens.All.Select(Area).ToList(), Area(primary));
            }
            window.Position = new PixelPoint(x, y);
        }
        if (Enum.TryParse<WindowState>(session.State, out var state) && state is WindowState.Maximized or WindowState.FullScreen) window.WindowState = state;
    }
}