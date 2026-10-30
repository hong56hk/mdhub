using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Mdhub;

public partial class App : Application
{
    public const Avalonia.Controls.ShutdownMode ShutdownModeSetting = Avalonia.Controls.ShutdownMode.OnLastWindowClose;
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Localization.LocResources.Install(this);
        Services.ThemeService.Apply(Services.ThemeService.Current, persist: false);
        Services.ThemeService.HookTitleBars();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Each folder can have its own window; the app exits when the last one closes.
            desktop.ShutdownMode = ShutdownModeSetting;
            // Reopen the last session's windows; a missing or unusable session starts as a single new window.
            Services.SessionManager.Enabled = true;
            var saved = Services.SessionStore.Load(Services.SessionManager.SessionPath);
            Services.ThemeService.Apply(Services.ThemeService.Parse(saved?.Theme), persist: false);
            Services.RecentService.Load(saved);
            Services.Shortcuts.Import(saved?.CustomShortcuts, Components.ShortcutKeys.IsKeyName);
            Services.PreviewSettings.Import(saved?.Preview);
            Services.EditorFonts.Import(saved?.EditorFonts);
            Services.EditorColors.Import(saved?.EditorColors);
            Services.EditorBehavior.Import(saved?.EditorBehavior);
            // A saved choice wins; otherwise the OS language when supported, else English (not saved until picked).
            Localization.Loc.Chosen = Localization.Loc.Normalize(saved?.Language);
            Localization.Loc.Set(Localization.Loc.Chosen ?? Localization.Loc.Detect(System.Globalization.CultureInfo.CurrentUICulture.Name));
            List<MainWindow> restored;
            try { restored = Services.SessionManager.Restore(); }
            catch (Exception) { restored = []; }
            if (restored.Count == 0) restored.Add(new MainWindow());
            desktop.MainWindow = restored[0];
            desktop.ShutdownRequested += (_, e) => { if (!e.Cancel) Services.SessionManager.BeginShutdown(); };
            restored[0].Opened += (_, _) => { foreach (var window in restored.Skip(1)) window.Show(); };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
