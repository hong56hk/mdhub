using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

internal static class ReopenTabTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SessionManager.Enabled = false;
        Shortcuts.ResetAll();
        var w = new MainWindow { Width = 1100, Height = 760 };
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
        var path = Path.Combine(Path.GetTempPath(), $"mdhub-reopen-{Guid.NewGuid():N}.txt");
        try
        {
            w.Show(); Settle();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            var tabs = w.GetVisualDescendants().OfType<FileTabs>().Single();
            var list = tabs.FindControl<ListBox>("Tabs")!;
            List<NoteDocument> Docs() => list.Items.OfType<NoteDocument>().ToList();
            void Key(Key key, bool shift = false)
            {
                var mods = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
                if (shift) mods |= KeyModifiers.Shift;
                view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods });
                Shortcuts.KeyReleased(key.ToString());
                Settle();
            }
            Key(Avalonia.Input.Key.T, true);
            Check(Docs().Count == 1, "Reopen with no history does nothing");
            Key(Avalonia.Input.Key.T);
            var untitled = tabs.SelectedDocument!;
            Check(Docs().Count == 2 && untitled.Path is null && untitled.Text == "", "Ctrl+T adds an empty untitled tab");
            Key(Avalonia.Input.Key.N);
            Check(Docs().Count == 3 && tabs.SelectedDocument!.Text == "", "Ctrl+N still adds an empty tab");
            Key(Avalonia.Input.Key.W);
            Key(Avalonia.Input.Key.W);
            Key(Avalonia.Input.Key.T, true);
            Check(tabs.SelectedDocument!.UntitledName == untitled.UntitledName && Docs().Count == 2, "Reopen restores the most recently closed untitled tab");
            Key(Avalonia.Input.Key.T, true);
            Check(Docs().Count == 3, "Repeated presses restore older closed tabs");
            File.WriteAllText(path, "saved file");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var open = (Task)typeof(MainView).GetMethod("OpenPathAsync", flags)!.Invoke(view, [path])!;
            while (!open.IsCompleted) Pump();
            open.GetAwaiter().GetResult(); Settle();
            var index = Docs().IndexOf(tabs.SelectedDocument!);
            Key(Avalonia.Input.Key.W);
            File.WriteAllText(path, "updated on disk");
            Key(Avalonia.Input.Key.T, true);
            Check(tabs.SelectedDocument!.Path == path && tabs.SelectedDocument.Text == "updated on disk", "Closed files reopen from disk");
            Check(Docs().IndexOf(tabs.SelectedDocument!) == index, "Reopened tab returns to its tab position");
            Check(!Shortcuts.Conflicts().Any() && Shortcuts.Get("file.reopenTab").NoRepeat, "Bindings have no conflicts and reopening ignores key repeat");
        }
        finally
        {
            foreach (var document in w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!.Items.OfType<NoteDocument>()) document.SavedText = document.Text;
            w.Close(); Pump();
            File.Delete(path);
            Shortcuts.ResetAll();
        }
    }
}
