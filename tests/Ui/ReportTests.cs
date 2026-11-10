using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Localization;
using Mdhub.Services;
using Mdhub.Views;

// Item 103: Help > Report menu item, the Report window, the issues link (no browser launch) and strings in every language.
internal static class ReportTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly string[] Keys = ["MenuReport", "ReportTitle", "ReportDescription", "ReportLink"];

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }

        foreach (var lang in Loc.Languages)
        {
            var table = Loc.Table(lang.Code);
            Check(Keys.All(k => table.TryGetValue(k, out var v) && v.Length > 0), $"{lang.Code}: every Report string exists");
            if (lang.Code == Loc.English)
                Check(table["ReportLink"] == AppInfo.IssuesDisplay, "English link label is github.com/hong56hk/mdhub/issues");
            else
                Check(table["MenuReport"] != Loc.En["MenuReport"] && table["ReportTitle"] != Loc.En["ReportTitle"] && table["ReportDescription"] != Loc.En["ReportDescription"] && table["ReportLink"] != Loc.En["ReportLink"],
                    $"{lang.Code}: the menu, title, description and link label are translated");
        }

        var w = new MainWindow { Width = 1100, Height = 700 };
        Uri? launched = null;
        DialogService.LaunchOverride = uri => { launched = uri; return Task.CompletedTask; };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var help = w.GetVisualDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu").Items.OfType<MenuItem>().First(m => (m.Header as string) == Loc.T("MenuHelp"));
            var items = help.Items.OfType<MenuItem>().ToList();
            Check(items[0].Header as string == Loc.T("MenuReport") && items[0].InputGesture is null, "Help starts with Report and it has no shortcut");
            Check(items.Any(i => i.Header as string == Loc.T("MenuGuide")) && items.Any(i => i.Header as string == Loc.T("MenuAbout")), "Guide and About remain in Help");

            typeof(MainView).GetMethod("Report_Click", Flags)!.Invoke(v, [null, new RoutedEventArgs()]); Pump(); Pump();
            var report = w.OwnedWindows.Single(o => o.Name == "ReportDialog");
            T Named<T>(string name) where T : Control => report.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
            Check(report.IsVisible && report.Title == Loc.En["ReportTitle"], "Report opens a modal window titled Report");
            Check(report.Width == 540 && report.SizeToContent == SizeToContent.Height && !report.CanResize && report.WindowStartupLocation == WindowStartupLocation.CenterOwner,
                "Report uses the same size behaviour as About");
            Check(Named<TextBlock>("ReportDescription").Text == Loc.En["ReportDescription"], "the window shows the short description");
            var link = Named<HyperlinkButton>("ReportLink");
            Check(link.Content as string == Loc.En["ReportLink"] && link.NavigateUri is null && link.Cursor is not null && link.IsEffectivelyEnabled,
                "the issues link is shown and has no NavigateUri");
            var themeLink = ThemeService.Lookup("LinkForeground");
            Color Fg(HyperlinkButton b) => (b.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First().Foreground as ISolidColorBrush)?.Color ?? default;
            Check(Fg(link) == themeLink, $"the link uses the hand cursor and the theme LinkForeground ({themeLink})");
            Check(Named<Button>("ReportOk").Content as string == "OK" && Named<Image>("ReportLogo").Source is not null && Named<Image>("ReportLogo").Width == 64,
                "OK and the logo match the About window");
            Check(ThemeService.TitleBarDarkAtOpen(report) == (ThemeService.Current == AppTheme.Dark) && ReferenceEquals(report.Icon, AppIcon.Icon),
                "Report has the themed title bar and app icon");
            Check(launched is null, "opening the window does not launch a browser");
            link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(launched == AppInfo.IssuesUri && launched == new Uri("https://github.com/hong56hk/mdhub/issues") && report.IsVisible,
                "the link opens https://github.com/hong56hk/mdhub/issues only when clicked");
            Named<Button>("ReportOk").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(!report.IsVisible, "OK closes the Report window");

            Loc.Set(Loc.Japanese); Pump();
            typeof(MainView).GetMethod("Report_Click", Flags)!.Invoke(v, [null, new RoutedEventArgs()]); Pump(); Pump();
            var ja = w.OwnedWindows.Single(o => o.Name == "ReportDialog");
            Check(ja.Title == Loc.Ja["ReportTitle"] && ja.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "ReportDescription" && t.Text == Loc.Ja["ReportDescription"])
                  && ja.GetVisualDescendants().OfType<HyperlinkButton>().Any(b => b.Name == "ReportLink" && b.Content as string == Loc.Ja["ReportLink"]),
                "Report follows the UI language");
            ja.Close(); Pump();
        }
        finally { Loc.Set(Loc.English); DialogService.LaunchOverride = null; foreach (var o in w.OwnedWindows.ToList()) o.Close(); try { w.Close(); Pump(); } catch { } }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}