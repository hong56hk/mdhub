using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Services;

// Item 38: the New file / New folder name dialog: themed title bar from the first frame and after a live
// switch, shared button style, and the rename-style pre-selection.
internal static class NewFileDialogTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        bool Styled(Button b) => b.Classes.Contains("textAction") && b.CornerRadius == new CornerRadius(4) && b.Background is ISolidColorBrush bg && bg.Color == ThemeService.Lookup("ButtonBackground");
        var w = new MainWindow { Width = 1100, Height = 700 };
        try
        {
            w.Show(); Pump();
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme, persist: false); Pump();
                var dark = theme == AppTheme.Dark;
                var task = DialogService.PromptNameAsync(w, "New file", Path.GetTempPath(), "Untitled.md"); Pump(); Pump();
                var dlg = w.OwnedWindows.Single();
                Check(ThemeService.TitleBarDarkAtOpen(dlg) == dark, $"{theme}: the New file title bar is themed before the dialog first shows");
                Check(ThemeService.TitleBarDark(dlg) == dark, $"{theme}: the New file title bar is {(dark ? "dark" : "light")} once open");
                var other = dark ? AppTheme.Light : AppTheme.Dark;
                ThemeService.Apply(other, persist: false); Pump();
                Check(ThemeService.TitleBarDark(dlg) == !dark, $"{theme}: a live switch to {other} updates the open dialog's title bar");
                var buttons = dlg.GetVisualDescendants().OfType<Button>().ToList();
                var create = buttons.Single(b => (b.Content as string) == Mdhub.Localization.Loc.T("Create"));
                var cancel = buttons.Single(b => (b.Content as string) == Mdhub.Localization.Loc.T("Cancel"));
                Check(Styled(create) && Styled(cancel), $"{other}: Create and Cancel use the shared button style");
                ThemeService.Apply(theme, persist: false); Pump();
                Check(Styled(create) && Styled(cancel), $"{theme}: Create and Cancel use the shared button style");
                var input = dlg.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ItemName");
                Check(input.IsFocused && input.SelectionStart == 0 && input.SelectionEnd == "Untitled".Length && input.SelectedText == "Untitled",
                    $"{theme}: New file pre-selects only \"Untitled\", not \".md\"");
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                Check(task.IsCompleted && task.Result is null && w.OwnedWindows.Count == 0, $"{theme}: Cancel closes the New file dialog");

                var folderTask = DialogService.PromptNameAsync(w, "New folder", Path.GetTempPath(), "New folder"); Pump(); Pump();
                var fd = w.OwnedWindows.Single();
                var fin = fd.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "ItemName");
                Check(ThemeService.TitleBarDarkAtOpen(fd) == dark && fin.SelectedText == "New folder", $"{theme}: New folder is themed and selects the whole name");
                fd.Close(); Pump();
            }
        }
        finally
        {
            foreach (var o in w.OwnedWindows.ToList()) o.Close();
            w.Close(); Pump();
            ThemeService.Apply(AppTheme.Dark, persist: false);
        }
        Console.WriteLine($"New file dialog tests: {passed} passed");
    }
}
