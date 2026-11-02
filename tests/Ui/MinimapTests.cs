using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;

internal static class MinimapTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var timeout = new CancellationTokenSource(180); Dispatcher.UIThread.MainLoop(timeout.Token); }
        var passed = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); passed++; Console.WriteLine("PASS " + message); }
        SessionManager.Enabled = false;
        EditorBehavior.Import(null);
        var editor = new TextEditor();
        var document = new NoteDocument();
        document.Editor.Text = string.Join('\n', Enumerable.Range(1, 250).Select(i => $"## Heading {i}\nText with **emphasis** and `code`."));
        editor.ShowDocument(document);
        var window = new Window { Content = editor, Width = 800, Height = 600 };
        try
        {
            window.Show(); Pump();
            var map = editor.GetVisualDescendants().OfType<EditorMinimap>().Single();
            var scroll = document.Editor.GetVisualDescendants().OfType<ScrollViewer>().First();
            Check(map.IsVisible && EditorBehavior.Current.Minimap, "Minimap enabled by default");
            var scrollbar = scroll.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().Single(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
            var presenter = scroll.Presenter!;
            Check(map.TranslatePoint(default, editor)!.Value.X >= presenter.TranslatePoint(default, editor)!.Value.X + presenter.Bounds.Width,
                "Minimap sits to the right of the text viewport");
            Check(map.TranslatePoint(default, editor)!.Value.X + map.Bounds.Width <= scrollbar.TranslatePoint(default, editor)!.Value.X,
                "Original vertical scrollbar sits to the right of the minimap");
            document.Editor.SelectionStart = 4; document.Editor.SelectionEnd = 12;
            var selection = (document.Editor.SelectionStart, document.Editor.SelectionEnd);
            Point At(double y) => map.TranslatePoint(new Point(50, y), window)!.Value;
            window.MouseDown(At(map.Bounds.Height * 0.7), MouseButton.Left);
            window.MouseUp(At(map.Bounds.Height * 0.7), MouseButton.Left); Pump();
            Check(scroll.Offset.Y > scroll.Extent.Height / 2, "Clicking the map scrolls to a distant section");
            Check(selection == (document.Editor.SelectionStart, document.Editor.SelectionEnd), "Navigation preserves selection");
            var offset = scroll.Offset.Y;
            window.MouseDown(At(map.Bounds.Height * 0.7), MouseButton.Left);
            window.MouseMove(At(map.Bounds.Height * 0.3));
            window.MouseUp(At(map.Bounds.Height * 0.3), MouseButton.Left); Pump();
            Check(scroll.Offset.Y < offset, "Dragging the viewport scrolls upward");
            using (var frame = window.CaptureRenderedFrame()) frame?.Save(Path.Combine(Path.GetTempPath(), "mdhub-minimap.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            var fullWidth = scroll.Viewport.Width;
            var check = EditorFontsFlyout.BuildMinimap();
            check.IsChecked = false; Pump();
            Check(!map.IsVisible && scroll.Viewport.Width > fullWidth, "Gear setting hides minimap and returns its width to editor");
            var json = System.Text.Json.JsonSerializer.Serialize(EditorBehavior.Current);
            Check(System.Text.Json.JsonSerializer.Deserialize<EditorBehaviorData>(json)?.Minimap == false, "Disabled setting survives serialization");
            Check(System.Text.Json.JsonSerializer.Deserialize<EditorBehaviorData>("{}")!.Minimap, "Older settings enable minimap by default");
            check.IsChecked = true; Pump();
            Check(map.IsVisible, "Gear setting restores minimap live");
            document.Editor.TextWrapping = TextWrapping.Wrap;
            document.Editor.Text = string.Join(' ', Enumerable.Repeat("wrapped text", 1000)); Pump();
            window.MouseDown(At(map.Bounds.Height * 0.5), MouseButton.Left);
            window.MouseUp(At(map.Bounds.Height * 0.5), MouseButton.Left); Pump();
            Check(scroll.Offset.Y > 0, "Navigation works with wrapped text");
            var shortDoc = new NoteDocument(); shortDoc.Editor.Text = "short";
            editor.ShowDocument(shortDoc); Pump();
            Check(shortDoc.Editor.GetVisualDescendants().OfType<ScrollViewer>().First().Offset.Y == 0, "Switching to a short document resets the map source");
            shortDoc.Editor.Text = ""; Pump();
            Check(map.Bounds.Height > 0, "Empty document renders safely");
        }
        finally { window.Close(); EditorBehavior.Import(null); }
        Console.WriteLine($"Minimap tests: {passed} passed");
    }
}
