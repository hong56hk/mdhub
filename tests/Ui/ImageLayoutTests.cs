using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Rendering;

internal static class ImageLayoutTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-image-layout-" + Guid.NewGuid().ToString("N"))).FullName;
        var window = new Window { Width = 600, Height = 800 };
        try
        {
            using (var bitmap = new RenderTargetBitmap(new PixelSize(180, 320))) bitmap.Save(Path.Combine(root, "tall.png"));
            window.Show();
            foreach (var source in new[]
            {
                "- Label ![image](tall.png)\n- Next item",
                "1. Label ![image](tall.png)\n2. Next item",
                "- Parent\n  - **Label ![image](tall.png)**\n  - Next item",
                "- ![image](tall.png)\n- Next item",
                "Label ![image](tall.png)\n\nNext item",
                "## Label ![image](tall.png)\n\nNext item"
            })
            {
                var panel = new StackPanel();
                foreach (var control in MarkdownPreview.RenderMapped(MarkdownPreview.Parse(source), source, root).Controls) panel.Children.Add(control);
                window.Content = new ScrollViewer { Content = panel };
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var image = panel.GetVisualDescendants().OfType<Image>().Single();
                var next = panel.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Any(r => r.Text == "Next item") == true);
                var imageTop = image.TranslatePoint(default, panel)!.Value.Y;
                var nextTop = next.TranslatePoint(default, panel)!.Value.Y;
                if (image.Bounds.Height < 319 || nextTop < imageTop + image.Bounds.Height)
                    throw new Exception($"Image clipped or next item overlaps: image={image.Bounds}, imageTop={imageTop}, nextTop={nextTop}, markdown={source}");
                Console.WriteLine("PASS: full-height image without overlap: " + source.Split('\n')[0]);
                (image.Source as IDisposable)?.Dispose();
            }
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
}
