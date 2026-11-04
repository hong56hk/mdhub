using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

internal static class RecoveryTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-recovery-" + Guid.NewGuid().ToString("N"))).FullName;
        SessionManager.PathOverride = Path.Combine(root, "appdata", "session.json");
        SessionManager.Enabled = true;
        SessionManager.Reset();
        var windows = new List<MainWindow>();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Pump() { using var timeout = new CancellationTokenSource(20); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!task.IsCompleted && DateTime.UtcNow < deadline) Pump();
            if (!task.IsCompleted) throw new Exception("Timed out waiting for recovery operation");
            task.GetAwaiter().GetResult();
        }
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
        MainView View(MainWindow w) => w.FindControl<MainView>("MainView")!;
        FileTabs Tabs(MainWindow w) => w.FindControl<MainView>("MainView")!.FindControl<EditorPane>("PrimaryPane")!.FindControl<FileTabs>("FileTabs")!;
        List<NoteDocument> Docs(MainWindow w) => Tabs(w).FindControl<ListBox>("Tabs")!.Items.OfType<NoteDocument>().ToList();
        void Invoke(MainWindow w, string method, params object[] args) => Wait((Task)typeof(MainView).GetMethod(method, flags)!.Invoke(View(w), args)!);
        MainWindow Open()
        {
            var w = new MainWindow { Width = 1000, Height = 700 }; windows.Add(w); w.Show(); Pump(); return w;
        }
        List<RecoveryDocument> Stored() => SessionStore.Load(SessionManager.RecoveryPath)!.Windows.SelectMany(w => w.Recovery).ToList();
        void Choice(MainWindow w, string value) => typeof(MainView).GetProperty("ConflictChoiceOverride", flags)!.SetValue(View(w), (Func<NoteDocument, Task<string?>>)(_ => Task.FromResult<string?>(value)));
        void Close(MainWindow w)
        {
            w.Close();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (w.IsVisible && DateTime.UtcNow < deadline) Pump();
            Check(!w.IsVisible && w.OwnedWindows.Count == 0, "Quit preserves recovery without a save prompt");
        }
        try
        {
            var path = Path.Combine(root, "existing.md"); File.WriteAllText(path, "original");
            var w = Open(); var scratch = Docs(w).Single(); scratch.Editor.Text = "new unsaved note \U0001F642";
            Invoke(w, "OpenPathAsync", path);
            var existing = Tabs(w).SelectedDocument!; existing.Editor.Text = "unsaved existing edits";
            Wait(View(w).FlushRecoveryAsync());
            Check(File.ReadAllText(path) == "original" && Stored().Any(r => r.Path == path && r.Text == existing.Text), "Recovery saves existing edits without touching the original file");
            Check(Stored().Any(r => r.Path is null && r.Text == scratch.Text), "Never-saved notes are included in recovery");

            // Actual idle timing: an edit reaches recovery after a pause, without an explicit flush.
            scratch.Editor.Text = "saved after pause";
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!Stored().Any(r => r.Text == scratch.Text) && DateTime.UtcNow < deadline) Pump();
            Check(Stored().Any(r => r.Text == scratch.Text), "Recovery updates after the two-second editing pause");

            // Keep editing so the idle trigger never fires; the maximum interval must still persist work.
            var start = DateTime.UtcNow;
            while ((DateTime.UtcNow - start).TotalSeconds < 10.7)
            {
                scratch.Editor.Text += ".";
                for (var i = 0; i < 3; i++) Pump();
            }
            Check(Stored().Any(r => r.Path is null && r.Text.StartsWith("saved after pause.", StringComparison.Ordinal)), "Continuous editing is recovered within ten seconds");

            var scratchText = scratch.Text;
            Close(w); Wait(SessionManager.SaveRecoveryAsync());
            SessionManager.Reset();
            File.WriteAllText(path, "external while app closed");
            var restored = SessionManager.Restore(); windows.AddRange(restored);
            Check(restored.Count == 1, "Recovery restores its window on startup");
            w = restored[0];
            var startupConflicts = 0;
            typeof(MainView).GetProperty("ConflictChoiceOverride", flags)!.SetValue(View(w),
                (Func<NoteDocument, Task<string?>>)(_ => { startupConflicts++; return Task.FromResult<string?>("Cancel"); }));
            w.Show();
            deadline = DateTime.UtcNow.AddSeconds(10);
            while (Docs(w).Count < 2 && DateTime.UtcNow < deadline) Pump();
            scratch = Docs(w).Single(d => d.Path is null); existing = Docs(w).Single(d => d.Path == path);
            Check(scratch.Text == scratchText && scratch.IsDirty && existing.Text == "unsaved existing edits" && existing.IsDirty, "Startup restores both dirty documents and their unsaved indicators");
            while (startupConflicts == 0 && DateTime.UtcNow < deadline) Pump();
            Check(startupConflicts > 0 && File.ReadAllText(path) == "external while app closed", "Startup detects a conflict without writing the original file");

            Tabs(w).SelectedDocument = existing; Pump();
            File.WriteAllText(path, "external change");
            Choice(w, "Cancel"); Invoke(w, "CheckExternalChangeAsync", existing);
            Check(existing.Text == "unsaved existing edits" && File.ReadAllText(path) == "external change", "Conflict Cancel preserves both versions");
            Choice(w, "Reload"); Invoke(w, "CheckExternalChangeAsync", existing);
            Check(existing.Text == "external change" && !existing.IsDirty && Stored().All(r => r.Path != path), "Conflict Reload replaces editor content and clears dirty recovery");
            File.WriteAllText(path, "clean external update"); Invoke(w, "CheckExternalChangeAsync", existing);
            Check(existing.Text == "clean external update" && !existing.IsDirty, "Unmodified editors reload external changes automatically");
            existing.Editor.Text = "replace disk with editor"; File.WriteAllText(path, "another external change");
            Choice(w, "ReplaceDiskFile"); Invoke(w, "CheckExternalChangeAsync", existing);
            Check(File.ReadAllText(path) == "replace disk with editor" && !existing.IsDirty, "Conflict Replace explicitly saves editor content to disk");

            var newPath = Path.Combine(root, "saved-scratch.md");
            typeof(MainView).GetProperty("SaveFilePickerOverride", flags)!.SetValue(View(w), (Func<NoteDocument, Task<string?>>)(_ => Task.FromResult<string?>(newPath)));
            Invoke(w, "SaveAsync", scratch, false);
            Check(File.ReadAllText(newPath) == scratchText && Stored().Count == 0, "First manual save clears the untitled recovery copy");

            existing.Editor.Text = "discard this"; Wait(View(w).FlushRecoveryAsync());
            var closeTab = (Task)typeof(MainView).GetMethod("CloseDocumentAsync", flags)!.Invoke(View(w), [existing])!;
            deadline = DateTime.UtcNow.AddSeconds(5);
            while (w.OwnedWindows.Count == 0 && DateTime.UtcNow < deadline) Pump();
            var dialog = w.OwnedWindows.Single();
            var buttons = ((StackPanel)dialog.Content!).Children.OfType<StackPanel>().Single().Children.OfType<Button>();
            buttons.Single(b => Equals(b.Content, "Discard")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Wait(closeTab);
            Check(Stored().All(r => r.Path != path) && File.ReadAllText(path) == "replace disk with editor", "Discarding a tab removes its recovery without changing disk");

            // A recovered file remains recoverable even after another program deletes its original.
            scratch.Editor.Text = "survives deletion"; Wait(View(w).FlushRecoveryAsync()); File.Delete(newPath);
            var validated = SessionStore.Validate(SessionStore.Load(SessionManager.RecoveryPath), Directory.Exists, File.Exists);
            Check(validated.Windows.Any(s => s.Recovery.Any(r => r.Path == newPath)), "Missing original files do not discard unsaved recovery");
            var utf16 = new RecoveryDocument { CodePage = 1201, Bom = false }.CreateEncoding();
            Check(utf16.CodePage == 1201 && utf16.GetPreamble().Length == 0, "Recovery preserves encoding and BOM settings");
            var previousRecovery = File.ReadAllText(SessionManager.RecoveryPath);
            var attributes = File.GetAttributes(SessionManager.RecoveryPath);
            File.SetAttributes(SessionManager.RecoveryPath, attributes | FileAttributes.ReadOnly);
            try
            {
                scratch.Editor.Text += " latest";
                var failedFlush = View(w).FlushRecoveryAsync(); Wait(failedFlush);
                Check(!failedFlush.Result && File.ReadAllText(SessionManager.RecoveryPath) == previousRecovery, "Failed recovery writes preserve the last good copy");
                w.Close();
                for (var i = 0; i < 10; i++) Pump();
                Check(w.IsVisible, "Recovery failure prevents quitting with unprotected edits");
            }
            finally { File.SetAttributes(SessionManager.RecoveryPath, attributes); }
            Close(w); Wait(SessionManager.SaveRecoveryAsync());

            SessionManager.Reset();
            var firstWindow = Open(); Invoke(firstWindow, "OpenPathAsync", path);
            var secondPath = Path.Combine(root, "second.md"); File.WriteAllText(secondPath, "second");
            var secondWindow = Open(); Invoke(secondWindow, "OpenPathAsync", secondPath);
            SessionManager.BeginShutdown(); Close(firstWindow); Close(secondWindow);
            Wait(SessionManager.SaveRecoveryAsync());
            Check(SessionStore.Load(SessionManager.RecoveryPath) is { Windows.Count: 2 }, "App shutdown preserves every open window, including clean file tabs");
        }
        finally
        {
            SessionManager.Enabled = false;
            foreach (var w in windows.Where(w => w.IsVisible))
            {
                foreach (var d in Docs(w)) { d.SavedText = d.Text; d.SavedLineEnding = d.LineEnding; }
                w.Close(); Pump();
            }
            SessionManager.Reset(); SessionManager.PathOverride = null;
            Directory.Delete(root, true);
        }
    }
}
