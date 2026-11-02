using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Services;
using Mdhub.Views;

internal static class DisabledButtonTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var timeout = new CancellationTokenSource(60); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
        string Brush(IBrush? brush) => brush?.ToString() ?? "null";
        ContentPresenter Presenter(Button button) => button.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
        var window = new MainWindow();
        window.Show(); Pump();
        try
        {
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            var busy = typeof(MainView).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme, persist: false); Pump();
                var undo = window.GetVisualDescendants().OfType<Button>().Single(b => b.Tag as string == "undo");
                var close = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "×");
                var preview = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "TogglePreviewButton");
                var buttons = new[] { undo, close, preview };
                var normal = buttons.Select(b => (Background: Brush(Presenter(b).Background), Border: Brush(Presenter(b).BorderBrush), Foreground: Brush(Presenter(b).Foreground))).ToArray();
                busy.Invoke(view, [true]); Pump();
                for (var i = 0; i < buttons.Length; i++)
                {
                    var b = buttons[i]; var p = Presenter(b);
                    Check(!b.IsEffectivelyEnabled && Brush(p.Background) == normal[i].Background && Brush(p.BorderBrush) == normal[i].Border,
                        $"{theme}: {b.Tag ?? b.Content} keeps its surface while busy");
                    Check(Brush(p.Foreground) != normal[i].Foreground, $"{theme}: {b.Tag ?? b.Content} foreground dims");
                    foreach (var icon in b.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>())
                        Check(Brush(icon.Stroke ?? icon.Fill) == Brush(p.Foreground), $"{theme}: button icon uses disabled foreground");
                }
                busy.Invoke(view, [false]); Pump();
                for (var i = 0; i < buttons.Length; i++)
                    Check(buttons[i].IsEffectivelyEnabled && Brush(Presenter(buttons[i]).Foreground) == normal[i].Foreground, $"{theme}: button restores enabled appearance");

                var samples = new Button[]
                {
                    new() { Content = "Dialog", Classes = { "textAction" } },
                    new() { Content = "Custom", Background = Brushes.Coral, BorderBrush = Brushes.Blue },
                    new ToggleButton { Content = "Checked", IsChecked = true, Classes = { "fieldToggle" } }
                };
                var sampleWindow = new Window { Content = new StackPanel { Children = { samples[0], samples[1], samples[2] } } };
                sampleWindow.Show(); Pump();
                try
                {
                    foreach (var b in samples)
                    {
                        var p = Presenter(b); var bg = Brush(p.Background); var border = Brush(p.BorderBrush); var fg = Brush(p.Foreground);
                        b.IsEnabled = false; Pump();
                        Check(Brush(p.Background) == bg && Brush(p.BorderBrush) == border && Brush(p.Foreground) != fg && b.Opacity == 1,
                            $"{theme}: {b.Content} dims only foreground");
                    }
                }
                finally { sampleWindow.Close(); }
            }
        }
        finally { window.Close(); }
    }
}
