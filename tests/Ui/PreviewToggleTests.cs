using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Services;
using Mdhub.Views;

// Item 48: the icon-only toggle-preview button.
internal static class PreviewToggleTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        var note = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"mdhub-toggle-{Guid.NewGuid():N}.md"); File.WriteAllText(note, "# hi");
        var w = new MainWindow { Width = 1100, Height = 700 };
        var startLanguage = Loc.Info.Code;
        try
        {
            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var bar = w.GetVisualDescendants().OfType<TextFormatter>().Single();
            var button = bar.FindControl<Button>("TogglePreviewButton")!;
            var icon = bar.FindControl<Avalonia.Controls.Shapes.Path>("TogglePreviewIcon")!;
            var preview = (Control)typeof(MainView).GetField("MdEditor", Flags)!.GetValue(v)!;
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [note])!);
            Color Stroke() => ((ISolidColorBrush)icon.Stroke!).Color;
            Check(button.Content == icon && icon.StrokeThickness <= 1.5, "The button shows only the thin-stroke split-editor icon (no text)");
            var geometry = icon.Data!.Bounds;
            Check(geometry.Width > 10 && geometry.Height > 8 && icon.Data.StrokeContains(new Pen(Brushes.Black, 1.25), new Point(8, 7)) && icon.Data.StrokeContains(new Pen(Brushes.Black, 1.25), new Point(1.5, 7)) && icon.Data.StrokeContains(new Pen(Brushes.Black, 1.25), new Point(14.5, 7)) && !icon.Data.StrokeContains(new Pen(Brushes.Black, 1.25), new Point(5, 7)), "The icon is a rounded square split by a centre line");
            Check(AutomationProperties.GetName(button) == Loc.T("TogglePreview"), "The button keeps its accessibility name");
            foreach (var (theme, on, off) in new[] { (AppTheme.Dark, Color.Parse("#E0E0E0"), Color.Parse("#6A6A6A")), (AppTheme.Light, Color.Parse("#1F1F1F"), Color.Parse("#A0A0A0")) })
            {
                ThemeService.Apply(theme, persist: false); Pump();
                Check(preview.IsVisible && bar.PreviewOn && Stroke() == on, $"{theme}: preview on -> icon {on}");
                Check(button.Background is ISolidColorBrush bg && bg.Color == ThemeService.Lookup("PreviewToggleOnBackground"), $"{theme}: the on state has a subtle background");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(!preview.IsVisible && !bar.PreviewOn && Stroke() == off, $"{theme}: clicking hides the preview and dims the icon to {off}");
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(preview.IsVisible && bar.PreviewOn && Stroke() == on, $"{theme}: clicking again shows the preview and restores {on}");
            }
            ThemeService.Apply(AppTheme.Dark, persist: false); Pump();
            foreach (var code in Loc.Languages.Select(l => l.Code))
            {
                Loc.Set(code); Pump();
                var tip = ToolTip.GetTip(button) as string;
                Check(tip == Loc.F("TogglePreviewTip", Shortcuts.Format("view.togglePreview", ShortcutKeys.IsMac)) && tip!.Contains(Shortcuts.Format("view.togglePreview", ShortcutKeys.IsMac)) && AutomationProperties.GetName(button) == Loc.T("TogglePreview"),
                    $"{code}: tooltip \"{tip}\" names the action and its shortcut; the accessibility name is localized");
            }
        }
        finally
        {
            Loc.Set(startLanguage);
            try { w.Close(); Pump(); } catch { }
            File.Delete(note);
        }
        Console.WriteLine($"Preview toggle tests: {passed} passed");
    }
}