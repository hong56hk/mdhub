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
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 58: About mdhub content, version, links, the bundled LICENSE tab and its strings in every language.
internal static class AboutTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly string[] Keys = ["AboutTitle", "AboutVersion", "AboutDescription", "AboutGitHub", "AboutLicense", "AboutMitLicense", "AboutCopyright", "OK"];

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }

        Check(AppInfo.Version == "0.1.0 (alpha)", $"the version is read from the assembly as '0.1.0 (alpha)' ({AppInfo.Version})");
        Check(AppInfo.CleanVersion("0.1.0 (alpha)+abc123") == "0.1.0 (alpha)", "a +commit suffix is stripped");
        var license = AppInfo.LicenseText();
        Check(license.StartsWith("MIT License") && license.Contains("Copyright (c) 2026 Hong"), "the embedded LICENSE resource loads");
        if (RepoRoot() is { } repo)
            Check(license == File.ReadAllText(Path.Combine(repo, "LICENSE")).ReplaceLineEndings("\n"), $"the embedded LICENSE matches the repo LICENSE ({repo})");
        else
            Console.WriteLine("SKIP: repo LICENSE comparison: Mdhub.csproj not found above the test exe, the working directory or AppContext.BaseDirectory (build output outside the repo; run from the worktree to include it)");
        foreach (var lang in Loc.Languages)
        {
            var table = Loc.Table(lang.Code);
            Check(Keys.All(k => table.TryGetValue(k, out var v) && v.Length > 0) && !table.ContainsKey("AboutText"), $"{lang.Code}: every About string exists");
            Check(table["AboutMitLicense"].Contains("MIT") && table["AboutGitHub"].StartsWith("GitHub") && table["AboutCopyright"] == "Copyright \u00a9 2026 Hong" && table["AboutDescription"].Contains(".NET") && table["AboutDescription"].Contains("Avalonia"),
                $"{lang.Code}: MDHub/GitHub/MIT/.NET/Avalonia stay unchanged");
            if (lang.Code != Loc.English) Check(table["AboutDescription"] != Loc.En["AboutDescription"] && table["AboutLicense"] != Loc.En["AboutLicense"], $"{lang.Code}: the description and labels are translated");
        }

        var w = new MainWindow { Width = 1100, Height = 700 };
        Uri? launched = null;
        DialogService.LaunchOverride = uri => { launched = uri; return Task.CompletedTask; };
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Window OpenAbout()
            {
                typeof(MainView).GetMethod("About_Click", Flags)!.Invoke(v, [null, new RoutedEventArgs()]); Pump(); Pump();
                return w.OwnedWindows.Single(o => o.Name == "AboutDialog");
            }
            var about = OpenAbout();
            T Named<T>(string name) where T : Control => about.GetVisualDescendants().OfType<T>().First(c => c.Name == name);
            var expected = new[] { "MDHub", "Version 0.1.0 (alpha)", Loc.En["AboutDescription"], "Copyright \u00a9 2026 Hong" };
            Check(Named<TextBlock>("AboutName").Text == expected[0] && Named<TextBlock>("AboutVersion").Text == expected[1] && Named<TextBlock>("AboutDescription").Text == expected[2] && Named<TextBlock>("AboutCopyright").Text == expected[3],
                "About shows MDHub, Version 0.1.0 (alpha), the description and the copyright");
            var github = Named<HyperlinkButton>("AboutGitHubLink");
            var mit = Named<HyperlinkButton>("AboutLicenseLink");
            Check(Named<StackPanel>("AboutGitHubRow").Children.OfType<TextBlock>().First().Text == "GitHub:" && github.Content as string == "github.com/hong56hk/mdhub"
                  && Named<StackPanel>("AboutLicenseRow").Children.OfType<TextBlock>().First().Text == "License:" && mit.Content as string == "MIT License"
                  && Named<Button>("AboutOk").Content as string == "OK", "GitHub: github.com/hong56hk/mdhub, License: MIT License and OK");
            var link = ThemeService.Lookup("LinkForeground");
            Color Fg(HyperlinkButton b) => (b.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First().Foreground as ISolidColorBrush)?.Color ?? default;
            Check(new[] { github, mit }.All(b => b.Cursor is not null && b.IsEffectivelyEnabled) && Fg(github) == link && Fg(mit) == link,
                $"links use the hand cursor and the theme LinkForeground ({link})");
            Check(ThemeService.TitleBarDarkAtOpen(about) == (ThemeService.Current == AppTheme.Dark) && ReferenceEquals(about.Icon, AppIcon.Icon), "About has the themed title bar and app icon");
            github.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(launched == new Uri("https://github.com/hong56hk/mdhub") && about.IsVisible, "the GitHub link opens https://github.com/hong56hk/mdhub");
            var before = ((System.Collections.IList)typeof(MainView).GetField("_documents", Flags)!.GetValue(v)!).Count;
            mit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Pump();
            var docs = ((IEnumerable<NoteDocument>)typeof(MainView).GetField("_documents", Flags)!.GetValue(v)!).ToList();
            var active = (NoteDocument)typeof(MainView).GetField("_active", Flags)!.GetValue(v)!;
            Check(!about.IsVisible && docs.Count == before + 1 && active.Title == "LICENSE" && active.Path is null && active.Text == license && active.Editor.IsReadOnly && !active.IsDirty,
                "MIT License closes About and opens LICENSE in a read-only tab");
            OpenAbout().GetVisualDescendants().OfType<HyperlinkButton>().First(b => b.Name == "AboutLicenseLink").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Pump();
            Check(((IEnumerable<NoteDocument>)typeof(MainView).GetField("_documents", Flags)!.GetValue(v)!).Count() == before + 1, "opening the licence again reuses its tab");
            Loc.Set(Loc.Japanese); Pump();
            var ja = OpenAbout();
            Check(ja.Title == Loc.Ja["AboutTitle"] && ja.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "AboutVersion" && t.Text == Loc.F("AboutVersion", "0.1.0 (alpha)")), "About follows the UI language");
            ja.Close(); Pump();
        }
        finally { Loc.Set(Loc.English); DialogService.LaunchOverride = null; foreach (var o in w.OwnedWindows.ToList()) o.Close(); try { w.Close(); Pump(); } catch { } }
    }

    // Bug 83: tests may be built outside the repo (%TEMP%), so also look above the working directory. Null: not found.
    static string? RepoRoot()
    {
        var starts = new[] { Path.GetDirectoryName(Environment.ProcessPath), Environment.CurrentDirectory, AppContext.BaseDirectory };
        foreach (var start in starts.Where(s => !string.IsNullOrEmpty(s)))
            for (var d = new DirectoryInfo(start!); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "Mdhub.csproj"))) return d.FullName;
        return null;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
