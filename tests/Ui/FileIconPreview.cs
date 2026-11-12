using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;

// Item 112: headless preview of the new/changed file-type icons at 16px and 32px in both themes.
internal static class FileIconPreview
{
    public static void Write(string path)
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }

        var samples = new (string FileName, FileIcon Icon)[]
        {
            ("notes.md", FileIcons.Markdown),
            ("page.html", FileIcons.Html),
            ("main.c", FileIcons.C),
            ("App.java", FileIcons.Java),
            ("script.py", FileIcons.Python),
        };

        var root = new StackPanel { Spacing = 0 };
        foreach (var (theme, bg, fg) in new (AppTheme, Color, Color)[]
        {
            (AppTheme.Dark, Color.Parse("#1E1E1E"), Color.Parse("#F2F2F2")),
            (AppTheme.Light, Color.Parse("#F5F5F5"), Color.Parse("#1A1A1A")),
        })
        {
            ThemeService.Apply(theme, persist: false);
            var band = new StackPanel
            {
                Background = new SolidColorBrush(bg),
                Spacing = 8,
                Margin = new Thickness(0),
            };
            band.Children.Add(new TextBlock
            {
                Text = theme + " theme",
                Foreground = new SolidColorBrush(fg),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(16, 12, 16, 0),
            });
            var sizes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 32, Margin = new Thickness(16, 4, 16, 16) };
            foreach (var size in new[] { 16.0, 32.0 })
            {
                var col = new StackPanel { Spacing = 8 };
                col.Children.Add(new TextBlock { Text = $"{size:0}px", Foreground = new SolidColorBrush(fg), FontSize = 11, Opacity = 0.8 });
                var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
                foreach (var (fileName, icon) in samples)
                {
                    var cell = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Width = 56 };
                    var glyph = new Viewbox
                    {
                        Width = size, Height = size, Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Child = new FileTypeIcon(fileName),
                    };
                    cell.Children.Add(glyph);
                    cell.Children.Add(new TextBlock
                    {
                        Text = System.IO.Path.GetExtension(fileName),
                        Foreground = new SolidColorBrush(fg),
                        FontSize = 10,
                        HorizontalAlignment = HorizontalAlignment.Center,
                    });
                    row.Children.Add(cell);
                }
                col.Children.Add(row);
                sizes.Children.Add(col);
            }
            band.Children.Add(sizes);
            root.Children.Add(band);
        }

        const int width = 720, height = 268;
        var host = new Border { Child = root, Background = new SolidColorBrush(Color.Parse("#808080")), Width = width, Height = height };
        var window = new Window { Content = host, Width = width, Height = height, WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        Pump();
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        Pump();
        var bitmap = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96));
        bitmap.Render(host);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        bitmap.Save(path);
        window.Close();
        Pump();
        ThemeService.Apply(AppTheme.Dark, persist: false);
        Console.WriteLine("Wrote " + path);
    }
}
