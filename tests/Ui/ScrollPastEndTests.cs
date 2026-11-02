using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;

internal static class ScrollPastEndTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SessionManager.Enabled = false;
        void Pump() { using var timeout = new CancellationTokenSource(350); Dispatcher.UIThread.MainLoop(timeout.Token); }
        var count = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
        var source = new TextEditor();
        var preview = new MdEditor();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        grid.Children.Add(source); grid.Children.Add(preview); Grid.SetColumn(preview, 1);
        var window = new Window { Content = grid, Width = 1000, Height = 650 };
        var document = new NoteDocument();
        source.ShowDocument(document); preview.ShowDocument(document);
        try
        {
            window.Show(); Pump();
            foreach (var text in new[] { "One short line", string.Join("\n\n", Enumerable.Range(1, 80).Select(i => $"Paragraph {i} with some text.")), new string('x', 1500) })
            {
                document.Editor.TextWrapping = TextWrapping.Wrap;
                document.Editor.Text = text; preview.ScheduleRefresh(); Pump(); Pump();
                foreach (var height in new[] { 650, 450 })
                {
                    window.Height = height; Pump();
                    var scroll = document.Editor.GetVisualDescendants().OfType<ScrollViewer>().First();
                    var presenter = document.Editor.GetVisualDescendants().OfType<TextPresenter>().First();
                    var last = presenter.TextLayout.HitTestTextPosition(text.Length);
                    scroll.Offset = new Vector(0, last.Top); Pump();
                    Check(Math.Abs(scroll.Offset.Y - last.Top) < 1, $"Editor last line reaches top ({text.Length} chars, {height}px)");
                    var ps = preview.FindControl<ScrollViewer>("PreviewScroll")!;
                    var content = preview.FindControl<StackPanel>("PreviewContent")!;
                    var block = content.Children.Last();
                    var layer = preview.FindControl<Panel>("PreviewLayer")!;
                    var bottom = block.TranslatePoint(new Point(0, block.Bounds.Height), layer)!.Value.Y;
                    ps.Offset = new Vector(0, bottom - 1); Pump();
                    Check(Math.Abs(ps.Offset.Y - (bottom - 1)) < 1, $"Preview final content reaches top ({text.Length} chars, {height}px)");
                    Check(document.Text == text, "Extra scroll space does not modify the document");
                }
            }
        }
        finally { window.Close(); }
        Console.WriteLine($"Scroll past end tests: {count} passed");
    }
}
