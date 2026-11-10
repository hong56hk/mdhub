using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Mdhub.Components;
using Mdhub.Services;

namespace Mdhub;

// Application shell. Full-screen pages live in Views; workspace pieces live in Components.
public partial class MainWindow : Window
{
    static int _dialogHookInstalled;

    public MainWindow()
    {
        InitializeComponent();
        Services.ThemeService.Register(this);
        MainView.TitleChanged += title => Title = title;
        MainView.CloseRequested += Close;
        MainView.WindowActionRequested += HandleWindowAction;
        PropertyChanged += (_, args) =>
        {
            if (args.Property == WindowStateProperty) MainView.UpdateWindowState(WindowState);
        };
        MainView.UpdateWindowState(WindowState);
        Closing += (_, args) => MainView.HandleClosing(args);
        Closed += (_, _) => MainView.StopPreview();
        Opened += (_, _) => { Title = MainView.DocumentTitle; MainView.FocusEditor(); };
        AddHandler(KeyDownEvent, OnZoomKey, RoutingStrategies.Tunnel);
        // Sidebar shortcuts at window level: they must work from any focus state, even with nothing focused.
        AddHandler(KeyDownEvent, (_, e) => MainView.HandleSidebarShortcut(e), RoutingStrategies.Tunnel);
        // Bug 86: with focus outside the view (e.g. the editor hid for a PDF placeholder and focus dropped to the window),
        // keys never cross MainView's own tunnel handler, so Ctrl+W, Ctrl+Tab and the other global shortcuts run from here.
        AddHandler(KeyDownEvent, (_, e) => { if (!e.Handled && !MainView.IsKeyboardFocusWithin) MainView.HandleUnfocusedShortcut(e); }, RoutingStrategies.Tunnel);
        // Bug 87: NoRepeat shortcuts re-arm on key release; losing activation (a dialog, another app) re-arms everything.
        // (Not on in-window focus moves: Ctrl+W moves focus to the next tab, and its auto-repeat must stay swallowed.)
        AddHandler(KeyUpEvent, (_, e) => Shortcuts.KeyReleased(e.Key.ToString()), RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => { Shortcuts.ReleaseAll(); _ = MainView.FlushRecoveryAsync(); };
        Activated += (_, _) => MainView.ScheduleExternalCheck();
        // A modal dialog swallows the owner's KeyUp, so any window opening re-arms everything too.
        if (Interlocked.Exchange(ref _dialogHookInstalled, 1) == 0)
            WindowOpenedEvent.AddClassHandler<Window>((_, _) => Shortcuts.ReleaseAll(), handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnZoomWheel, RoutingStrategies.Tunnel, handledEventsToo: true);
        SetZoom(ZoomLevel.Default, announce: false);
        Mdhub.Services.ThemeService.PrepareTitleBar(this); // themed caption from the first frame (item 38)
        SessionManager.Track(this); // after Closing above, so it sees the unsaved prompt's decision
    }

    // ---- Zoom: one LayoutTransform over the whole window content. Menus, context menus and tooltips are separate
    // popup roots; the Popup InheritsTransform style (WorkspaceStyles.axaml) makes them copy the placement target's
    // transform, so they are scaled by the same factor. Owned dialog windows are separate windows and stay at 100%.
    public int ZoomPercent { get; private set; } = ZoomLevel.Default;
    public double ZoomScale => ZoomLevel.Scale(ZoomPercent);

    public void SetZoom(int percent, bool announce = true)
    {
        ZoomPercent = ZoomLevel.Normalize(percent);
        ZoomHost.LayoutTransform = new ScaleTransform(ZoomScale, ZoomScale);
        if (announce) MainView.ShowTransientStatus(ZoomLevel.Label(ZoomPercent));
    }

    private static KeyModifiers CommandModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    private void OnZoomKey(object? sender, KeyEventArgs e)
    {
        // Item 37/91/102a: with an image or PDF tab active, Ctrl+=/-/0 zoom that viewer instead of the window.
        if (ShortcutKeys.Match(e, s => s.Area == ShortcutArea.Zoom) is { } imageZoom && MainView.ImageZoomKey(imageZoom)) { e.Handled = true; return; }
        int? next = ShortcutKeys.Match(e, s => s.Area == ShortcutArea.Zoom) switch
        {
            "zoom.in" => ZoomLevel.In(ZoomPercent),
            "zoom.out" => ZoomLevel.Out(ZoomPercent),
            "zoom.reset" => ZoomLevel.Default,
            _ => null
        };
        if (next is not { } value) return;
        e.Handled = true;
        SetZoom(value);
    }

    private void OnZoomWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(CommandModifier) || e.Delta.Y == 0) return;
        e.Handled = true; // the editor, preview and tab strip must not also scroll
        if (MainView.ZoomImage(e) || MainView.ZoomPdf(e)) return; // item 37/102a: Ctrl+wheel over an image or PDF zooms that viewer
        SetZoom(ZoomLevel.Wheel(ZoomPercent, e.Delta.Y));
    }

    public WindowSession CaptureRecoverySession() => SessionManager.Bounds(this, MainView.CaptureRecoverySession());
    public WindowSession CaptureSession() => SessionManager.Bounds(this, MainView.CaptureSession());
    public void ApplyBounds(WindowSession session) => SessionManager.Apply(this, session);

    // Loads the saved folder and tabs once the window is open.
    public void RestoreOnOpen(WindowSession session, string? note)
    {
        var started = false;
        Opened += async (_, _) =>
        {
            if (started) return;
            started = true;
            try { await MainView.RestoreSessionAsync(session, note); }
            finally { SessionManager.CompleteRestore(this); }
            Title = MainView.DocumentTitle;
        };
    }

    private void HandleWindowAction(WindowAction action)
    {
        switch (action)
        {
            case WindowAction.Minimize: WindowState = WindowState.Minimized; break;
            case WindowAction.ToggleMaximize:
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                break;
            case WindowAction.Close: Close(); break; // Uses the existing unsaved-change checks.
        }
    }
}
