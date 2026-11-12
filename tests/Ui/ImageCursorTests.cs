using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Mdhub;
using Mdhub.Components;

// Item 110: while panning an image (left-drag or right-drag past 4 px), show the shared open-hand cursor
// from item 109 and restore the rest cursor on release and on pointer capture lost.
internal static class ImageCursorTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS: " + name); }
        static string CursorName(Cursor? c) => c?.ToString() ?? "(null)";
        static string HandName() => new Cursor(StandardCursorType.Hand).ToString();
        static string CrossName() => new Cursor(StandardCursorType.Cross).ToString();

        var viewer = new ImageViewer();
        var window = new Window { Width = 440, Height = 440, Content = viewer };
        window.Show();
        void Pump() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        void Wait(Task task) { var end = DateTime.UtcNow.AddSeconds(10); while (!task.IsCompleted && DateTime.UtcNow < end) Pump(); task.GetAwaiter().GetResult(); Pump(); }
        var bitmap = new RenderTargetBitmap(new PixelSize(800, 400));
        viewer.ShowImage(bitmap); Pump();
        viewer.ActualSize(); Pump();
        var scale = TopLevel.GetTopLevel(viewer)?.RenderScaling ?? 1;
        var open = PanCursors.OpenHand(scale);
        var p = new Point(200, 200);
        var menu = viewer.ContextMenu!;

        viewer.Tool = ImageTool.Hand; Pump();
        Check(CursorName(viewer.CurrentCursor) == HandName() && !viewer.IsPanning,
            "the Hand tool uses StandardCursorType.Hand at rest");

        var before = viewer.Offset;
        window.MouseDown(p, MouseButton.Left); Pump();
        Check(viewer.IsPanning && ReferenceEquals(viewer.CurrentCursor, open),
            "a left-button Hand pan shows the open-hand cursor");
        window.MouseMove(p + new Point(40, -20), RawInputModifiers.LeftMouseButton); Pump();
        Check(viewer.IsPanning && viewer.Offset == before + new Point(40, -20) && ReferenceEquals(viewer.CurrentCursor, open),
            "the open-hand cursor stays in place while the image pans");
        window.MouseUp(p + new Point(40, -20), MouseButton.Left); Pump();
        Check(!viewer.IsPanning && CursorName(viewer.CurrentCursor) == HandName(),
            "releasing the left button restores the Hand cursor");

        // Capture lost: start a pan, then release capture without a MouseUp first.
        Wait(Task.Delay(700));
        IPointer? pointer = null;
        void Save(object? s, PointerPressedEventArgs e) { pointer = e.Pointer; }
        viewer.AddHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Save, RoutingStrategies.Tunnel);
        window.MouseDown(p, MouseButton.Left); Pump();
        viewer.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Save);
        Check(viewer.IsPanning && ReferenceEquals(viewer.CurrentCursor, open) && pointer is not null,
            "a second left pan shows the open-hand cursor again");
        pointer!.Capture(null);
        Pump();
        Check(!viewer.IsPanning && CursorName(viewer.CurrentCursor) == HandName(),
            "losing pointer capture restores the Hand cursor");
        window.MouseUp(p, MouseButton.Left); Pump();
        Check(!viewer.IsPanning && CursorName(viewer.CurrentCursor) == HandName(),
            "a MouseUp after capture lost leaves the rest cursor in place");

        foreach (var tool in new[] { ImageTool.Hand, ImageTool.Rotate })
        {
            viewer.Tool = tool; Pump();
            var rest = tool == ImageTool.Hand ? HandName() : CrossName();
            Check(CursorName(viewer.CurrentCursor) == rest && !viewer.IsPanning && !viewer.IsRightPanning,
                $"{tool}: rest cursor is unchanged before a right-drag");
            var origin = viewer.Offset;
            window.MouseDown(p, MouseButton.Right); Pump();
            window.MouseMove(p + new Point(2, 1), RawInputModifiers.RightMouseButton); Pump();
            Check(!viewer.IsPanning && !viewer.IsRightPanning && CursorName(viewer.CurrentCursor) == rest && viewer.Offset == origin,
                $"{tool}: a right press under the 4 px threshold does not pan or change the cursor");
            window.MouseMove(p + new Point(30, 12), RawInputModifiers.RightMouseButton); Pump();
            Check(viewer.IsRightPanning && viewer.IsPanning && ReferenceEquals(viewer.CurrentCursor, open),
                $"{tool}: right-drag panning shows the same open-hand cursor");
            Check(viewer.Offset == origin + new Point(30, 12), $"{tool}: right-drag pans the image");
            window.MouseUp(p + new Point(30, 12), MouseButton.Right); Pump();
            Check(!viewer.IsPanning && !viewer.IsRightPanning && CursorName(viewer.CurrentCursor) == rest && !menu.IsOpen,
                $"{tool}: releasing a right-drag restores the rest cursor and does not open the menu");

            window.MouseDown(p, MouseButton.Right); Pump();
            window.MouseMove(p + new Point(3, -3), RawInputModifiers.RightMouseButton); Pump();
            window.MouseUp(p + new Point(3, -3), MouseButton.Right); Pump();
            Check(menu.IsOpen && viewer.Offset == origin + new Point(30, 12) && !viewer.IsRightPanning,
                $"{tool}: a right click (moved < 4 px) opens the context menu and does not pan");
            menu.Close(); Pump();
        }

        // Rotate left-drag is not a pan and keeps the cross cursor.
        viewer.Tool = ImageTool.Rotate; Pump();
        var rot = viewer.Rotation;
        window.MouseDown(p, MouseButton.Left); Pump();
        Check(!viewer.IsPanning && CursorName(viewer.CurrentCursor) == CrossName(),
            "a left-button Rotate drag does not use the pan cursor");
        window.MouseMove(p + new Point(40, 0), RawInputModifiers.LeftMouseButton); Pump();
        window.MouseUp(p + new Point(40, 0), MouseButton.Left); Pump();
        Check(!viewer.IsPanning && CursorName(viewer.CurrentCursor) == CrossName() && viewer.Rotation != rot,
            "releasing a rotate drag restores the Cross cursor");

        try { window.Close(); Pump(); } catch { }
        Console.WriteLine($"Image cursor tests passed: {passed}");
    }
}
