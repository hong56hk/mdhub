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
            var editor = v.FindControl<TextEditor>("TextEditor")!;
            var splitter = v.FindControl<GridSplitter>("PreviewSplitter")!;
            var editButton = bar.FindControl<Button>("EditorOnlyButton")!;
            var viewButton = bar.FindControl<Button>("ViewerOnlyButton")!;
            void Click(Button target) { target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
            void Mode(int mode)
            {
                Check(editor.IsVisible == (mode != 2) && preview.IsVisible == (mode != 0) && splitter.IsVisible == (mode == 1), $"Mode {mode}: correct panes and splitter");
                Check(editButton.Classes.Contains("on") == (mode == 0) && button.Classes.Contains("on") == (mode == 1) && viewButton.Classes.Contains("on") == (mode == 2), "Exactly the selected mode is highlighted");
                if (mode == 2) Check(Grid.GetColumn(preview) == 2 && Grid.GetColumnSpan(preview) == 3 && preview.Bounds.Width > 0, "Viewer fills the content area");
            }
            void Key(Avalonia.Input.RawInputModifiers mods)
            {
                w.KeyPress(Avalonia.Input.Key.OemPipe, mods, Avalonia.Input.PhysicalKey.None, null);
                w.KeyRelease(Avalonia.Input.Key.OemPipe, mods, Avalonia.Input.PhysicalKey.None, null);
                Pump();
            }
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme, persist: false); Pump();
                Click(button); Mode(1);
                foreach (var target in new[] { editButton, button, viewButton })
                    Check(target.Content is Avalonia.Controls.Shapes.Path path && path.StrokeThickness <= 1.5, "Mode buttons use thin paths without text or emoji");
                Click(editButton); Mode(0); Click(editButton); Mode(0);
                Key(Avalonia.Input.RawInputModifiers.Control); Mode(1);
                Key(Avalonia.Input.RawInputModifiers.Control); Mode(0);
                Key(Avalonia.Input.RawInputModifiers.Alt); Mode(1);
                Key(Avalonia.Input.RawInputModifiers.Alt); Mode(2);
                Key(Avalonia.Input.RawInputModifiers.Control); Mode(1);
                Click(viewButton); Mode(2); Click(viewButton); Mode(2);
                Key(Avalonia.Input.RawInputModifiers.Alt); Mode(1);
            }
            foreach (var code in Loc.Languages.Select(l => l.Code))
            {
                Loc.Set(code); Pump();
                Check(AutomationProperties.GetName(button) == Loc.T("ViewModeSplit") && AutomationProperties.GetName(editButton) == Loc.T("ViewModeEditor") && AutomationProperties.GetName(viewButton) == Loc.T("ViewModeViewer"), $"{code}: accessible mode names");
                Check(((string)ToolTip.GetTip(viewButton)!).Contains(Shortcuts.Format("view.toggleEditor", ShortcutKeys.IsMac)), "Viewer tooltip shows live shortcut");
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