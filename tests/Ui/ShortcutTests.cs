using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Services;

// Item 32: themed title bars on every window, shared dialog button style, customizable shortcuts.
internal static class ShortcutTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Key(Window target, Key k, RawInputModifiers m = RawInputModifiers.None) { target.KeyPress(k, m, PhysicalKey.None, null); target.KeyRelease(k, m, PhysicalKey.None, null); Pump(); }
        // Headless keys go to the focused element; put focus in the main window (the explorer tree) first.
        void KeyMain(Window main, Key k, RawInputModifiers m) { main.GetVisualDescendants().OfType<TreeView>().First(t => t.Name == "FolderTree").Focus(); Pump(); Key(main, k, m); }
        void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
        var sessionPath = Path.Combine(Path.GetTempPath(), $"mdhub-shortcuts-{Guid.NewGuid():N}.json");
        Shortcuts.ResetAll();
        ThemeService.Apply(AppTheme.Dark, persist: false);
        var w = new MainWindow { Width = 1100, Height = 700 };
        try
        {
            w.Show(); Pump();
            Check(ThemeService.TitleBarDark(w) == true, "Dark: the main window title bar is dark");

            // ---- Every dialog: themed title bar and the shared (find bar Replace) button style.
            bool Styled(Button b) => b.Classes.Contains("textAction") && b.CornerRadius == new CornerRadius(4) && b.Background is ISolidColorBrush { } bg && bg.Color == ThemeService.Lookup("ButtonBackground");
            void Dialog(string name, Action open, Func<Window, bool>? extra = null)
            {
                open(); Pump();
                var d = w.OwnedWindows.Last();
                var buttons = d.GetVisualDescendants().OfType<Button>().Where(b => !b.Classes.Contains("findAction")).ToList();
                Check(ThemeService.TitleBarDark(d) == true && buttons.Count > 0 && buttons.All(Styled) && (extra?.Invoke(d) ?? true), $"{name}: dark title bar and every button uses the shared style");
                d.Close(); Pump();
            }
            Dialog("Name prompt", () => _ = DialogService.PromptNameAsync(w, "New file", Path.GetTempPath(), "a.md"));
            Dialog("Go to line", () => _ = DialogService.PromptLineAsync(w, 10, 1));
            Dialog("Message", () => _ = DialogService.ShowAsync(w, "Unsaved", "Save?", "Save", "DontSave", "Cancel"));

            _ = DialogService.ShowShortcutsAsync(w); Pump();
            var dlg = w.OwnedWindows.Single();
            var close = dlg.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ShortcutsClose");
            var resetAll = dlg.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResetAllShortcuts");
            Check(ThemeService.TitleBarDark(dlg) == true && Styled(close) && Styled(resetAll), "Shortcuts: dark title bar; Close and Reset all use the shared button style");
            var closePresenter = close.GetVisualDescendants().OfType<ContentPresenter>().First();
            var closeAt = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), dlg)!.Value;
            dlg.MouseMove(closeAt); Pump();
            Check(closePresenter.Background is ISolidColorBrush { } hover && hover.Color == ThemeService.Lookup("HoverBackground"), "Close shows the shared hover background");
            dlg.MouseMove(new Point(5, 5)); Pump();

            ThemeService.Apply(AppTheme.Light, persist: false); Pump();
            Check(ThemeService.TitleBarDark(w) == false && ThemeService.TitleBarDark(dlg) == false, "Switching to Light turns every open title bar light, live");
            Check(Styled(close), "Light: the Close button keeps the shared style with the Light colours");
            ThemeService.Apply(AppTheme.Dark, persist: false); Pump();
            Check(ThemeService.TitleBarDark(w) == true && ThemeService.TitleBarDark(dlg) == true, "Switching back to Dark turns them dark again");

            // ---- Rebinding.
            Grid Row(string id) => dlg.GetVisualDescendants().OfType<Grid>().Single(g => g.Classes.Contains("shortcut-row") && Equals(g.Tag, id));
            Button RowButton(string id, string name) => Row(id).Children.OfType<Button>().Single(b => b.Name == name);
            string Keys(string id) => Row(id).Children.OfType<TextBlock>().Single(t => t.Classes.Contains("shortcut-keys")).Text!;
            bool Marked(string id) => Row(id).Children.OfType<TextBlock>().Single(t => t.Name == "ShortcutCustomMark").IsVisible && Row(id).Classes.Contains("customized");
            var conflict = dlg.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ShortcutConflict");
            var conflictText = dlg.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "ShortcutConflictText");
            var reassign = dlg.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ShortcutReassign");
            var conflictCancel = dlg.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ShortcutConflictCancel");

            Check(!Marked("file.save") && !RowButton("file.save", "ShortcutReset").IsVisible && RowButton("file.save", "ShortcutEdit").IsVisible && !resetAll.IsEnabled,
                "Default entries: no mark, no reset icon, Reset all disabled");
            Check(!Row("edit.copy").Children.OfType<Button>().Any(), "Shortcuts built into the editor cannot be edited");
            Click(RowButton("sidebar.search", "ShortcutEdit"));
            Check(Row("sidebar.search").Classes.Contains("recording") && Keys("sidebar.search") == Loc.T("ShortcutRecording"), "The edit icon starts recording");
            Key(dlg, Avalonia.Input.Key.LeftCtrl, RawInputModifiers.Control);
            Check(Row("sidebar.search").Classes.Contains("recording"), "Modifier keys alone keep recording");
            Key(dlg, Avalonia.Input.Key.Escape);
            Check(!Row("sidebar.search").Classes.Contains("recording") && Keys("sidebar.search") == "Ctrl+Shift+F" && dlg.IsVisible, "Esc cancels recording without closing the dialog");

            // Clicking the row itself also records.
            Row("sidebar.search").BringIntoView(); Pump();
            var rowAt = Row("sidebar.search").TranslatePoint(new Point(20, 8), dlg)!.Value;
            dlg.MouseDown(rowAt, MouseButton.Left); dlg.MouseUp(rowAt, MouseButton.Left); Pump();
            Check(Row("sidebar.search").Classes.Contains("recording"), "Clicking an entry starts recording");
            Key(dlg, Avalonia.Input.Key.F, RawInputModifiers.Control | RawInputModifiers.Alt);
            Check(Keys("sidebar.search") == "Ctrl+Alt+F" && Marked("sidebar.search") && RowButton("sidebar.search", "ShortcutReset").IsVisible && resetAll.IsEnabled,
                "A free combination is bound at once; the entry is marked and gets a reset icon");

            // Menu hints follow the live binding.
            IEnumerable<MenuItem> Items(IEnumerable<MenuItem> items) { foreach (var i in items) { yield return i; foreach (var c in Items(i.Items.OfType<MenuItem>())) yield return c; } }
            var menu = w.GetVisualDescendants().OfType<Menu>().First();
            MenuItem MenuFor(string id) => Items(menu.Items.OfType<MenuItem>()).First(i => ShortcutKeys.GetId(i) == id);
            Click(RowButton("file.quickOpen", "ShortcutEdit"));
            Key(dlg, Avalonia.Input.Key.P, RawInputModifiers.Control | RawInputModifiers.Alt);
            Check(MenuFor("file.quickOpen").InputGesture is { Key: Avalonia.Input.Key.P } g && g.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt) && ShortcutKeys.MenuText(MenuFor("file.quickOpen")) == "Ctrl+Alt+P",
                "The menu hint shows the new gesture");

            // The window-level handlers use the live bindings (the modal dialog is closed first, as a user would).
            Click(close);
            var search = w.GetVisualDescendants().OfType<GlobalSearch>().Single();
            var explorer = w.GetVisualDescendants().OfType<FolderExplorer>().Single();
            KeyMain(w, Avalonia.Input.Key.E, RawInputModifiers.Control | RawInputModifiers.Shift);
            KeyMain(w, Avalonia.Input.Key.F, RawInputModifiers.Control | RawInputModifiers.Shift);
            Check(!search.IsEffectivelyVisible, "The old Ctrl+Shift+F no longer opens folder search");
            KeyMain(w, Avalonia.Input.Key.F, RawInputModifiers.Control | RawInputModifiers.Alt);
            Check(search.IsEffectivelyVisible, "The new Ctrl+Alt+F opens folder search");
            KeyMain(w, Avalonia.Input.Key.E, RawInputModifiers.Control | RawInputModifiers.Shift);
            Check(explorer.IsEffectivelyVisible, "Ctrl+Shift+E still shows the explorer");
            var qo = w.GetVisualDescendants().OfType<QuickOpen>().Single();
            KeyMain(w, Avalonia.Input.Key.P, RawInputModifiers.Control);
            Check(!qo.IsOpen, "The old Ctrl+P no longer opens Quick Open");
            KeyMain(w, Avalonia.Input.Key.P, RawInputModifiers.Control | RawInputModifiers.Alt);
            Check(qo.IsOpen, "The new Ctrl+Alt+P opens Quick Open");
            Key(w, Avalonia.Input.Key.Escape, RawInputModifiers.None);

            _ = DialogService.ShowShortcutsAsync(w); Pump();
            dlg = w.OwnedWindows.Single();
            close = dlg.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ShortcutsClose");
            resetAll = dlg.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResetAllShortcuts");
            conflict = dlg.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "ShortcutConflict");
            conflictText = dlg.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "ShortcutConflictText");
            reassign = dlg.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ShortcutReassign");
            conflictCancel = dlg.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ShortcutConflictCancel");
            Check(Keys("sidebar.search") == "Ctrl+Alt+F" && Marked("file.quickOpen") && resetAll.IsEnabled, "Reopening the dialog shows the custom bindings");

            // ---- Conflicts.
            Click(RowButton("file.save", "ShortcutEdit"));
            Key(dlg, Avalonia.Input.Key.F, RawInputModifiers.Control);
            Check(conflict.IsVisible && reassign.IsVisible && conflictText.Text == Loc.F("ShortcutConflict", "Ctrl+F", Shortcuts.Get("find.find").DisplayLabel) && Keys("file.save") == Loc.T("ShortcutRecording"),
                "A clash warns, names the other entry and offers Reassign");
            Check(Styled(reassign) && Styled(conflictCancel), "Reassign and Cancel use the shared button style");
            Click(conflictCancel);
            Check(!conflict.IsVisible && Keys("file.save") == "Ctrl+S" && Keys("find.find") == "Ctrl+F" && !Marked("file.save"), "Cancel keeps both bindings");
            Click(RowButton("file.save", "ShortcutEdit"));
            Key(dlg, Avalonia.Input.Key.F, RawInputModifiers.Control);
            Click(reassign);
            Check(!conflict.IsVisible && Keys("file.save") == "Ctrl+F" && Keys("find.find") == Loc.T("ShortcutUnassigned") && Marked("file.save") && Marked("find.find") && !Shortcuts.Conflicts().Any(),
                "Reassign moves the gesture; the other entry shows Not assigned and is marked");
            Click(RowButton("view.wordWrap", "ShortcutEdit"));
            Key(dlg, Avalonia.Input.Key.C, RawInputModifiers.Control);
            Check(conflict.IsVisible && !reassign.IsVisible && conflictText.Text == Loc.F("ShortcutConflictBuiltIn", "Ctrl+C", Shortcuts.Get("edit.copy").DisplayLabel), "A gesture built into the editor warns without Reassign");
            Key(dlg, Avalonia.Input.Key.Escape);
            Check(!conflict.IsVisible && Keys("view.wordWrap") == "Alt+Z", "Esc dismisses the warning and keeps the binding");

            // ---- Persistence (settings are saved right away).
            SessionManager.PathOverride = sessionPath; SessionManager.Enabled = true;
            Click(RowButton("editor.goToLine", "ShortcutEdit"));
            Key(dlg, Avalonia.Input.Key.G, RawInputModifiers.Control | RawInputModifiers.Alt);
            var saved = SessionStore.Load(sessionPath)?.CustomShortcuts;
            Check(saved is not null && saved["editor.goToLine"].SequenceEqual(["Command+Alt+G"]) && saved["file.save"].SequenceEqual(["Command+F"]) && saved["find.find"].Count == 0 && saved.ContainsKey("sidebar.search"),
                "Custom bindings are written to the settings file");
            var exported = Shortcuts.Export();
            Shortcuts.Import(null);
            Check(!Shortcuts.AnyCustomized && Keys("file.save") == "Ctrl+S", "The open dialog refreshes when bindings change");
            Shortcuts.Import(SessionStore.Load(sessionPath)!.CustomShortcuts, ShortcutKeys.IsKeyName);
            Check(Shortcuts.Export().Count == exported.Count && Keys("editor.goToLine") == "Ctrl+Alt+G" && Keys("file.save") == "Ctrl+F", "Saved bindings load back");

            // ---- Reset one.
            Click(RowButton("file.save", "ShortcutReset"));
            Check(Keys("file.save") == "Ctrl+S" && !Marked("file.save") && !RowButton("file.save", "ShortcutReset").IsVisible && !SessionStore.Load(sessionPath)!.CustomShortcuts!.ContainsKey("file.save"),
                "Reset restores one entry, hides its icon and saves");
            Click(RowButton("find.find", "ShortcutReset"));
            Check(Keys("find.find") == "Ctrl+F" && !Marked("find.find"), "The unassigned entry resets to its default");

            // ---- Reset all, with confirmation.
            Click(resetAll);
            var confirm = dlg.OwnedWindows.Single();
            Check(confirm.Title == Loc.T("ShortcutResetAll") && ThemeService.TitleBarDark(confirm) == true && confirm.GetVisualDescendants().OfType<Button>().All(Styled), "Reset all asks first (themed, shared buttons)");
            Click(confirm.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.T("Cancel"))));
            Check(Shortcuts.AnyCustomized && dlg.OwnedWindows.Count == 0, "Cancel keeps the custom bindings");
            Click(resetAll);
            Click(dlg.OwnedWindows.Single().GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.T("ResetAll"))));
            Check(!Shortcuts.AnyCustomized && !resetAll.IsEnabled && Keys("sidebar.search") == "Ctrl+Shift+F" && !Marked("editor.goToLine") && SessionStore.Load(sessionPath)!.CustomShortcuts is null,
                "Confirming resets every shortcut and saves");
            Check(ShortcutKeys.MenuText(MenuFor("file.quickOpen")) == "Ctrl+P", "Menu hints return to the defaults");

            // ---- Localized in every language.
            var keys = new[] { "ShortcutEdit", "ShortcutReset", "ShortcutCustomized", "ShortcutRecording", "ShortcutConflict", "ShortcutConflictBuiltIn", "ShortcutReassign", "ShortcutResetAll", "ShortcutResetAllConfirm", "ResetAll", "ShortcutUnassigned", "ShortcutBuiltIn" };
            Check(Loc.Languages.All(l => keys.All(k => Loc.Table(l.Code).ContainsKey(k))) && Loc.Languages.Where(l => l.Code != Loc.English).All(l => keys.All(k => Loc.Table(l.Code)[k] != Loc.En[k])),
                "Every new string is translated in all 4 languages");
            Check(Loc.Languages.All(l => Loc.Table(l.Code)["ShortcutConflict"].Contains("{0}") && Loc.Table(l.Code)["ShortcutConflict"].Contains("{1}")), "Conflict messages keep both placeholders");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(w.OwnedWindows.Count == 0, "Close dismisses the Shortcuts dialog");
            Loc.Set("ja"); Pump();
            _ = DialogService.ShowShortcutsAsync(w); Pump();
            var jdlg = w.OwnedWindows.Single();
            Check(jdlg.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResetAllShortcuts").Content as string == Loc.Ja["ShortcutResetAll"], "The dialog uses the current language");
            jdlg.Close(); Pump();
            Loc.Set("en");
        }
        finally
        {
            SessionManager.Enabled = false; SessionManager.PathOverride = null;
            Shortcuts.ResetAll();
            w.Close(); Pump();
            if (File.Exists(sessionPath)) File.Delete(sessionPath);
        }
        Console.WriteLine($"shortcut tests passed: {passed}");
    }
}
