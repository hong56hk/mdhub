using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 91: image viewer navigation (zoom steps/limits/anchoring, fit/100%, rotate snap, flip pixel readout, reset,
// per-tab view state, tool keys scoped to the focused viewer), headless.
internal static class ImageViewTests
{
    public static void Run()
    {
        Math_();
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Viewer();
        InApp();
    }

    private static void Math_()
    {
        Check(ImageZoom.Max == 32 && ImageZoom.Min == 0.1, "range is 10%-3200%");
        Check(ImageZoom.StepZoom(1, 1) == 1.5 && Math.Abs(ImageZoom.StepZoom(1, -1) - 2 / 3.0) < 1e-9 && ImageZoom.StepZoom(0.7, 1) == 1 && ImageZoom.StepZoom(0.7, -1) == 2 / 3.0,
            "Ctrl+=/- step to the next preset");
        Check(ImageZoom.StepZoom(32, 1) == 32 && ImageZoom.StepZoom(0.1, -1) == 0.1 && ImageZoom.StepZoom(40, 1) == 32, "steps stop at the limits");
        Check(ImageZoom.Snap(22) == 15 && ImageZoom.Snap(23) == 30 && ImageZoom.Snap(-8) == 345 && ImageZoom.Snap(359) == 0, "rotation snaps to 15 degrees");
        Check(ImageZoom.Normalize(-90) == 270 && ImageZoom.Normalize(450) == 90, "angles normalise to 0-360");
        var (bw, bh) = ImageZoom.RotatedBounds(800, 400, 90);
        Check(bw == 400 && bh == 800 && ImageZoom.Fit(800, 400, 400, 400, 90) == 0.5 && ImageZoom.Fit(800, 200, 400, 400, 90) == 0.5 && ImageZoom.Fit(800, 200, 400, 400, 0) == 0.5
            && ImageZoom.Fit(400, 100, 400, 200, 90) == 0.5, "fit uses the rotated bounds");

        // Pixel readout: map image points to the screen the way the render transform does (flip, then rotate, about the
        // centre) and check that ToImage undoes it for every rotation/flip combination.
        const double w = 100, h = 50, s = 2, ox = 30, oy = -20;
        var ok = true;
        foreach (var deg in new[] { 0.0, 90, 180, 270, 15, 37.5 })
            foreach (var fh in new[] { false, true })
                foreach (var fv in new[] { false, true })
                    foreach (var (u, v) in new[] { (10.5, 5.5), (99.2, 0.3), (50.0, 25.0), (0.4, 49.6) })
                    {
                        double x = (u - w / 2) * s * (fh ? -1 : 1), y = (v - h / 2) * s * (fv ? -1 : 1);
                        var (rx, ry) = ImageZoom.Rotate(x, y, deg);
                        var back = ImageZoom.ToImage(ox + w * s / 2 + rx, oy + h * s / 2 + ry, ox, oy, s, w, h, deg, fh, fv);
                        ok &= back is { } b && Math.Abs(b.X - u) < 1e-6 && Math.Abs(b.Y - v) < 1e-6;
                    }
        Check(ok, "pixel readout is correct under every rotation and flip");
        Check(ImageZoom.ToPixel(10.5, 5.5, 0, 0, 1, 100, 50, 100, 50, 0, true, false) == (89, 5) && ImageZoom.ToPixel(10.5, 5.5, 0, 0, 1, 100, 50, 100, 50, 0, false, true) == (10, 44),
            "a horizontal flip mirrors x, a vertical flip mirrors y");
        Check(ImageZoom.ToPixel(69.5, -14.5, 0, 0, 1, 100, 50, 100, 50, 90, false, false) == (10, 5), "a quarter turn clockwise maps the screen point back to the pixel");
        Check(ImageZoom.ToPixel(20, 20, 0, 0, 1, 100, 50, 200, 100, 0, false, false) == (40, 40), "readout is in bitmap pixels (high-DPI bitmap)");
        Check(ImageZoom.ToImage(-1, 10, 0, 0, 1, 100, 50, 0, false, false) is null && ImageZoom.ToImage(10, 50, 0, 0, 1, 100, 50, 0, false, false) is null, "outside the image is null");
        var (px, py) = ImageZoom.RotateAbout(0, 0, 100, 50, 1, 50, 25, 90);
        Check(px == 0 && py == 0, "rotating about the image centre does not move it");
        (px, py) = ImageZoom.RotateAbout(100, 0, 100, 50, 1, 50, 25, 180);
        Check(px == -100 && py == 0, "rotating about the view centre moves the image around it");
    }

    private static void Viewer()
    {
        var viewer = new ImageViewer();
        var window = new Window { Width = 440, Height = 440, Content = viewer };
        window.Show();
        void Pump() { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        var bitmap = new RenderTargetBitmap(new PixelSize(800, 400));
        viewer.ShowImage(bitmap); Pump();
        Check(viewer.IsFit && viewer.ZoomPercent == 50, "fit: 800x400 in 400x400 is 50%");
        Point CenterImage() { var o = viewer.Offset; return new((200 - o.X) / viewer.Scale, (200 - o.Y) / viewer.Scale); }
        var before = CenterImage();
        Check(viewer.StepZoom(1) && viewer.ZoomPercent == 67, $"Ctrl+= steps 50% -> 67% ({viewer.ZoomPercent})");
        var after = CenterImage();
        Check(Math.Abs(before.X - after.X) < 1e-6 && Math.Abs(before.Y - after.Y) < 1e-6, "key zoom is anchored at the view centre");
        // Zoom tool / wheel anchoring at a pointer (viewer coordinates include the 20 px padding).
        var pointer = new Point(120, 260);
        var at = viewer.ImagePointAt(pointer)!.Value;
        bool Same(Point? q) => q is { } v && Math.Abs(v.X - at.X) < 1e-6 && Math.Abs(v.Y - at.Y) < 1e-6;
        viewer.ZoomAt(3, pointer);
        Check(Same(viewer.ImagePointAt(pointer)), "wheel zoom keeps the pixel under the pointer");
        viewer.StepZoom(1, pointer);
        Check(Same(viewer.ImagePointAt(pointer)), "zoom-tool click keeps the pixel under the pointer");
        for (var i = 0; i < 40; i++) viewer.StepZoom(1);
        Check(viewer.ZoomPercent == 3200, "zoom in stops at 3200%");
        for (var i = 0; i < 40; i++) viewer.StepZoom(-1);
        Check(viewer.ZoomPercent == 10, "zoom out stops at 10%");
        Check(viewer.ActualSize() && viewer.ZoomPercent == 100 && !viewer.IsFit, "Ctrl+1 is 100%");
        Check(viewer.FitToWindow() && viewer.IsFit && viewer.ZoomPercent == 50, "Ctrl+0 fits to the window");

        // Pan.
        viewer.ActualSize();
        var o0 = viewer.Offset;
        Check(viewer.Pan(0, -ImageViewer.WheelPan) && viewer.Offset == o0 + new Point(0, -ImageViewer.WheelPan), "wheel pans the image");

        // Rotation: quarter turns and the free-rotate drag with Shift snapping.
        viewer.FitToWindow();
        Check(viewer.Rotate90(1) && viewer.Rotation == 90 && viewer.IsFit, "rotate right is 90 degrees and stays fitted");
        Check(viewer.Rotate90(-1) && viewer.Rotate90(-1) && viewer.Rotation == 270, "rotate left twice from 90 is 270");
        viewer.BeginRotateDrag(0);
        viewer.RotateDrag(22, false);
        Check(Math.Abs(viewer.Rotation - 292) < 1e-9 && !viewer.IsFit, $"free rotate follows the pointer ({viewer.Rotation})");
        viewer.RotateDrag(22, true);
        Check(viewer.Rotation == 285, $"Shift snaps to 15 degrees ({viewer.Rotation})");
        var centre = viewer.Offset + new Point(viewer.Scale * 400, viewer.Scale * 200);
        Check(Math.Abs(centre.X - 200) < 1e-6 && Math.Abs(centre.Y - 200) < 1e-6, "rotation pivots on the view centre");

        // Flip changes the pixel readout, not the file.
        viewer.ResetView();
        viewer.ActualSize(); Pump();
        var p = new Point(30.5, 220.5); // a pixel centre; viewer coords: surface (10.5, 200.5) -> image x 210 at 100% centred (offset -200)
        var plain = viewer.PixelAt(p);
        viewer.Flip(true);
        var flipped = viewer.PixelAt(p);
        Check(plain is { } a && flipped is { } b && a.X + b.X == 799 && a.Y == b.Y && viewer.FlipHorizontal, $"horizontal flip mirrors the readout ({plain} / {flipped})");
        viewer.Flip(false);
        Check(viewer.PixelAt(p) is { } c && c.Y + plain!.Value.Y == 399 && viewer.FlipVertical, "vertical flip mirrors the readout");
        viewer.FitToWindow();
        Check(viewer.PixelAt(new Point(220, 30)) is null, "outside the image there is no pixel");

        // Reset (Esc / double-click) clears everything.
        viewer.Rotate90(1); viewer.Pan(5, 5);
        Check(viewer.ResetView() && viewer.IsFit && viewer.Rotation == 0 && !viewer.FlipHorizontal && !viewer.FlipVertical, "reset view: fit, no rotation, no flip");

        // View state per image (tab), kept while others are shown.
        viewer.Rotate90(1); viewer.Flip(true);
        var other = new RenderTargetBitmap(new PixelSize(100, 100));
        viewer.ShowImage(other); Pump();
        Check(viewer.IsFit && viewer.Rotation == 0 && !viewer.FlipHorizontal, "another image has its own view");
        viewer.ShowImage(bitmap); Pump();
        Check(viewer.Rotation == 90 && viewer.FlipHorizontal, "switching back restores the image's view");
        viewer.ResetView();

        // Keys while the viewer has focus.
        viewer.Focus(); Pump();
        void Key(Key k, RawInputModifiers m = RawInputModifiers.None) { window.KeyPress(k, m, PhysicalKey.None, null); window.KeyRelease(k, m, PhysicalKey.None, null); Pump(); }
        Key(Avalonia.Input.Key.Z); Check(viewer.Tool == ImageTool.Zoom, "Z selects the zoom tool");
        Key(Avalonia.Input.Key.R); Check(viewer.Tool == ImageTool.Rotate && viewer.CursorType == StandardCursorType.Cross, "R selects free rotate");
        window.KeyPress(Avalonia.Input.Key.Space, RawInputModifiers.None, PhysicalKey.None, null); Pump();
        Check(viewer.EffectiveTool == ImageTool.Hand && viewer.Tool == ImageTool.Rotate && viewer.CursorType == StandardCursorType.Hand, "holding Space is a temporary hand");
        window.KeyRelease(Avalonia.Input.Key.Space, RawInputModifiers.None, PhysicalKey.None, null); Pump();
        Check(viewer.EffectiveTool == ImageTool.Rotate, "releasing Space returns to the previous tool");
        Key(Avalonia.Input.Key.H); Check(viewer.Tool == ImageTool.Hand, "H selects the hand tool");
        var ctrl = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        Key(Avalonia.Input.Key.D1, ctrl); Check(viewer.ZoomPercent == 100, "Ctrl+1 key shows 100%");
        viewer.Rotate90(1);
        Key(Avalonia.Input.Key.Escape); Check(viewer.IsFit && viewer.Rotation == 0, "Esc resets the view");

        // Mouse: hand drag pans, zoom-tool click / Alt+click, double-click resets.
        viewer.ActualSize(); Pump();
        var start = viewer.Offset;
        window.MouseDown(new Point(200, 200), MouseButton.Left); window.MouseMove(new Point(230, 190)); window.MouseUp(new Point(230, 190), MouseButton.Left); Pump();
        Check(viewer.Offset == start + new Point(30, -10), $"hand drag pans ({viewer.Offset - start})");
        viewer.Tool = ImageTool.Zoom;
        var z = viewer.ZoomPercent;
        window.MouseDown(new Point(300, 250), MouseButton.Left); window.MouseUp(new Point(300, 250), MouseButton.Left); Pump();
        Check(viewer.ZoomPercent == 150 && z == 100, $"zoom tool click zooms in ({viewer.ZoomPercent})");
        window.MouseDown(new Point(300, 250), MouseButton.Left, RawInputModifiers.Alt); window.MouseUp(new Point(300, 250), MouseButton.Left, RawInputModifiers.Alt); Pump();
        Check(viewer.ZoomPercent == 100, $"Alt+click zooms out ({viewer.ZoomPercent})");
        window.MouseWheel(new Point(300, 250), new Vector(0, 2), RawInputModifiers.Alt); Pump();
        Check(viewer.ZoomPercent == 121, $"Alt+wheel zooms ({viewer.ZoomPercent})");
        viewer.Tool = ImageTool.Hand;
        var o1 = viewer.Offset;
        viewer.Rotate90(1); viewer.Flip(true);
        window.MouseDown(new Point(300, 250), MouseButton.Left); window.MouseUp(new Point(300, 250), MouseButton.Left);
        window.MouseDown(new Point(300, 250), MouseButton.Left); window.MouseUp(new Point(300, 250), MouseButton.Left); Pump();
        Check(viewer.IsFit && viewer.Rotation == 0 && !viewer.FlipHorizontal, "double-click resets the view");
        viewer.ZoomAt(2, new Point(300, 250));
        o1 = viewer.Offset;
        window.MouseWheel(new Point(300, 250), new Vector(0, -1)); Pump();
        Check(viewer.Offset == o1 + new Point(0, -50), "wheel scrolls vertically");
        window.MouseWheel(new Point(300, 250), new Vector(0, -1), RawInputModifiers.Shift); Pump();
        Check(viewer.Offset == o1 + new Point(-50, -50), "Shift+wheel scrolls horizontally");
        window.Close();
    }

    private static void InApp()
    {
        var window = new MainWindow { Width = 1000, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = window.FindControl<MainView>("MainView")!;
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        void Pump() { for (var k = 0; k < 3; k++) { using var timeout = new CancellationTokenSource(20); Dispatcher.UIThread.MainLoop(timeout.Token); } }
        bool Busy() => (bool)typeof(MainView).GetField("_busy", flags)!.GetValue(view)!;
        void WaitFor(Func<bool> condition) { var end = DateTime.UtcNow.AddSeconds(10); while ((!condition() || Busy()) && DateTime.UtcNow < end) Pump(); Pump(); }
        void Open(string path) { var t = (Task)typeof(MainView).GetMethod("OpenPathAsync", flags)!.Invoke(view, [path])!; WaitFor(() => t.IsCompleted); t.GetAwaiter().GetResult(); }
        var ctrl = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        void Key(Key k, RawInputModifiers m = RawInputModifiers.None) { window.KeyPress(k, m, PhysicalKey.None, null); window.KeyRelease(k, m, PhysicalKey.None, null); Pump(); }
        var tabs = window.GetVisualDescendants().OfType<ListBox>().First(x => x.Name == "Tabs");
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-imageview-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var png = Path.Combine(root, "pic.png");
            using (var rtb = new RenderTargetBitmap(new PixelSize(640, 320))) rtb.Save(png);
            var bytes = File.ReadAllBytes(png);
            var note = Path.Combine(root, "note.md");
            File.WriteAllText(note, "hello\n");
            Open(note);
            Open(png);
            var image = (NoteDocument)tabs.SelectedItem!;
            var viewer = window.GetVisualDescendants().OfType<ImageViewer>().First(v => v.IsVisible);
            var status = window.GetVisualDescendants().OfType<BottomStatusBar>().First();
            Check(viewer.IsKeyboardFocusWithin, "the image tab takes keyboard focus from the hidden editor");
            Key(Avalonia.Input.Key.Z);
            Check(viewer.Tool == ImageTool.Zoom, "Z works in the focused image tab");
            Key(Avalonia.Input.Key.H);
            var windowZoom = window.ZoomPercent;
            var imageZoom = viewer.ZoomPercent;
            Key(Avalonia.Input.Key.OemPlus, ctrl);
            Check(viewer.ZoomPercent > imageZoom && window.ZoomPercent == windowZoom, "Ctrl+= zooms the image, not the window");
            Key(Avalonia.Input.Key.OemMinus, ctrl); Key(Avalonia.Input.Key.OemMinus, ctrl);
            Check(viewer.ZoomPercent < imageZoom && window.ZoomPercent == windowZoom, "Ctrl+- zooms the image out");
            Key(Avalonia.Input.Key.D0, ctrl);
            Check(viewer.IsFit && window.ZoomPercent == windowZoom, "Ctrl+0 fits the image");
            viewer.Rotate90(1); viewer.Flip(true); viewer.Pan(10, 10);
            Check(!image.IsDirty && File.ReadAllBytes(png).SequenceEqual(bytes), "view transforms never touch the file or mark it dirty");

            // Status bar readout follows the pointer, a dash outside the image.
            viewer.ResetView(); viewer.ActualSize(); Pump();
            var inside = viewer.TranslatePoint(new Point(viewer.Bounds.Width / 2 + 5, viewer.Bounds.Height / 2 + 3), window)!.Value;
            window.MouseMove(inside); Pump();
            Check(status.ImageCursor is { } px && px.X == 325 && px.Y == 163 && Text(status).Contains(Mdhub.Localization.Loc.F("ImageCursorPosition", 325, 163)), $"status bar shows the pixel under the pointer ({status.ImageCursor}, '{Text(status)}')");
            viewer.FitToWindow(); Pump();
            var outside = viewer.TranslatePoint(new Point(viewer.Bounds.Width / 2, 30), window)!.Value;
            window.MouseMove(outside); Pump();
            Check(status.ImageCursor is null && Text(status).Contains("\u2014"), $"outside the image the readout is a dash ('{Text(status)}')");

            // Focus scoping: with the editor focused, H/Z/R type into it and never change the image tool.
            tabs.SelectedItem = tabs.Items.OfType<NoteDocument>().First(d => d.Path == note); WaitFor(() => true);
            var editor = view.GetVisualDescendants().OfType<TextEditor>().First(c => c.Name == "TextEditor" && c.IsEffectivelyVisible).GetVisualDescendants().OfType<TextBox>().First(b => b.IsEffectivelyVisible && b.Name != "FindBox" && b.Name != "ReplaceBox" && b.AcceptsReturn);
            editor.Focus(); editor.CaretIndex = 0; Pump();
            viewer.Tool = ImageTool.Hand;
            foreach (var (k, ch) in new[] { (Avalonia.Input.Key.Z, "z"), (Avalonia.Input.Key.R, "r") })
            {
                window.KeyPress(k, RawInputModifiers.None, PhysicalKey.None, ch); window.KeyTextInput(ch); window.KeyRelease(k, RawInputModifiers.None, PhysicalKey.None, ch); Pump();
            }
            Check(viewer.Tool == ImageTool.Hand && editor.Text!.StartsWith("zr"), $"tool keys do not fire in the editor ('{editor.Text}')");
            Key(Avalonia.Input.Key.OemPlus, ctrl);
            Check(window.ZoomPercent > windowZoom, "Ctrl+= still zooms the window in a text tab");
            Key(Avalonia.Input.Key.D0, ctrl);
            Check(window.ZoomPercent == windowZoom, "Ctrl+0 resets the window zoom in a text tab");
            Check(!Shortcuts.Conflicts().Any(), "the shortcut table has no conflicts");
            Check(new[] { "image.actualSize", "image.handTool", "image.zoomTool", "image.rotateTool", "image.tempHand", "image.resetView" }
                .All(id => Shortcuts.Get(id) is { ImageOnly: true, IsCustomizable: true, Area: ShortcutArea.Image }), "image shortcuts are registered and customizable");
            Check(Shortcuts.FindConflicts("image.handTool", new ShortcutGesture("Escape", ShortcutMods.None)).Select(s => s.Id).SequenceEqual(["image.resetView"]),
                "Esc in the viewer does not clash with closing the find bar");
        }
        finally
        {
            window.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    private static string Text(BottomStatusBar status) => status.FindControl<TextBlock>("Counts")!.Text ?? "";

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
