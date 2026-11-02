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
using Mdhub.Models;
using Mdhub.Rendering;
using Avalonia.Input;

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
            var preview = (Control)typeof(MainView).GetProperty("MdEditor", Flags)!.GetValue(v)!;
            Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [note])!);
            var editor = v.GetVisualDescendants().OfType<TextEditor>().Single(c => c.Name == "TextEditor");
            var splitter = v.GetVisualDescendants().OfType<GridSplitter>().Single(c => c.Name == "PreviewSplitter");
            var editButton = bar.FindControl<Button>("EditorOnlyButton")!;
            var viewButton = bar.FindControl<Button>("ViewerOnlyButton")!;
            var sync = bar.FindControl<Button>("SyncButton")!;
            void Click(Button target) { target.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); }
            void Mode(int mode)
            {
                Check(editor.IsVisible == (mode != 2) && preview.IsVisible == (mode != 0) && splitter.IsVisible == (mode == 1), $"Mode {mode}: correct panes and splitter");
                Check(editButton.Classes.Contains("on") == (mode == 0) && button.Classes.Contains("on") == (mode == 1) && viewButton.Classes.Contains("on") == (mode == 2), "Exactly the selected mode is highlighted");
                if (mode == 2) Check(Grid.GetColumn(preview) == 2 && Grid.GetColumnSpan(preview) == 3 && preview.Bounds.Width > 0, "Viewer fills the content area");
            }
            void Key(Avalonia.Input.RawInputModifiers mods)
            {
                w.KeyPress(Avalonia.Input.Key.M, mods, Avalonia.Input.PhysicalKey.None, null);
                w.KeyRelease(Avalonia.Input.Key.M, mods, Avalonia.Input.PhysicalKey.None, null);
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
                Check(AutomationProperties.GetName(sync) == Loc.T("Sc.view.previewCaret") && ((string)ToolTip.GetTip(sync)!).Contains(Shortcuts.Format("view.previewCaret", ShortcutKeys.IsMac)), "Sync name and tooltip follow language and shortcut");
            }
            Click(button);
            var md = (MdEditor)preview;
            var document = (NoteDocument)typeof(MainView).GetField("_active", Flags)!.GetValue(v)!;
            var source = string.Concat(Enumerable.Range(0, 90).Select(i => $"Paragraph {i} with **bold words** and tail.\n\n"))
                + "```\r\nfirst code line\r\nsecond code line\r\n```\r\n";
            document.Editor.Text = source;
            for (var i = 0; i < 15; i++) Pump();
            T Prop<T>(string name) => (T)typeof(MdEditor).GetProperty(name, Flags)!.GetValue(md)!;
            void SyncKey()
            {
                w.KeyPress(Avalonia.Input.Key.G, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
                w.KeyRelease(Avalonia.Input.Key.G, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.None, null);
                Pump();
            }
            Check(sync.IsEnabled && !sync.Focusable && sync.Content is Avalonia.Controls.Shapes.Path, "Sync uses SVG geometry and preserves pane focus");
            Check(sync.TranslatePoint(default, bar)!.Value.X < editButton.TranslatePoint(default, bar)!.Value.X, "Sync sits left of view modes");
            document.Editor.Focus();
            document.Editor.CaretIndex = source.IndexOf("Paragraph 70", StringComparison.Ordinal);
            var centers = Prop<int>("PreviewCenters");
            Click(sync);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Prop<int>("PreviewCenters") == centers && DateTime.UtcNow < deadline) Pump();
            Check(Prop<int>("PreviewCenters") > centers && md.FindControl<ScrollViewer>("PreviewScroll")!.Offset.Y > 0, "Sync button scrolls preview to source caret");
            centers = Prop<int>("PreviewCenters");
            SyncKey();
            deadline = DateTime.UtcNow.AddSeconds(5);
            while (Prop<int>("PreviewCenters") == centers && DateTime.UtcNow < deadline) Pump();
            Check(Prop<int>("PreviewCenters") > centers && document.Editor.IsFocused, "Ctrl+Shift+G syncs from editor and keeps focus");
            var selection = Prop<PreviewSelection>("Selection");
            var item = selection.Items.OfType<TextBlock>().First(t => t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().FirstOrDefault()?.Text?.StartsWith("Paragraph 80 ") == true);
            Check((bool)typeof(MdEditor).GetMethod("BeginPreviewEdit", Flags)!.Invoke(md, [item])!, "Open formatted preview edit");
            var box = Prop<TextBox>("PreviewEditBox");
            box.CaretIndex = "Paragraph 80 with bold ".Length;
            document.Editor.CaretIndex = 0;
            var editorScroll = document.Editor.GetVisualDescendants().OfType<ScrollViewer>().First();
            editorScroll.Offset = default;
            Pump(); // Let opening the preview edit settle before measuring its scroll position.
            var previewScroll = md.FindControl<ScrollViewer>("PreviewScroll")!;
            var previewOffset = previewScroll.Offset;
            void CheckEditorCentered()
            {
                var presenter = document.Editor.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().First();
                var rect = presenter.TextLayout.HitTestTextPosition(document.Editor.CaretIndex);
                var point = presenter.TranslatePoint(rect.Center, editorScroll)!.Value;
                Check(Math.Abs(point.Y - (editorScroll.Padding.Top + editorScroll.Viewport.Height / 2)) < 1, "Preview sync centers the source caret line in the editor");
            }
            Click(sync);
            var expected = source.IndexOf("Paragraph 80", StringComparison.Ordinal) + "Paragraph 80 with **bold ".Length;
            Check(document.Editor.CaretIndex == expected && editorScroll.Offset.Y > 0, "Preview caret maps through bold markup and scrolls source");
            CheckEditorCentered();
            Check(previewScroll.Offset == previewOffset, "Preview-to-editor sync preserves preview scroll position");
            Check(box.IsFocused && Prop<bool>("IsEditingPreview"), "Button leaves preview editing active");
            box.Text = "NEW " + box.Text;
            box.CaretIndex = "NEW Paragraph 80 with bold ".Length;
            document.Editor.CaretIndex = 0;
            SyncKey();
            Check(document.Editor.CaretIndex == expected && box.IsFocused && document.Text == source, "Shortcut maps pending preview edits without committing or stealing focus");
            CheckEditorCentered();
            typeof(MdEditor).GetMethod("CancelPreviewEdit", Flags)!.Invoke(md, []);
            var codeBlock = Prop<IEnumerable<Control>>("PreviewBlocks").First(c => PreviewTags.Kind(c) == "pre");
            Check((bool)typeof(MdEditor).GetMethod("BeginPreviewEdit", Flags)!.Invoke(md, [codeBlock])!, "Open code preview edit");
            box = Prop<TextBox>("PreviewEditBox");
            box.CaretIndex = "first code line\nsecond".Length;
            SyncKey();
            Check(document.Editor.CaretIndex == source.IndexOf("second code line", StringComparison.Ordinal) + 6, "Code caret maps normalized newlines back to CRLF source");
            typeof(MdEditor).GetMethod("CancelPreviewEdit", Flags)!.Invoke(md, []);
            typeof(MdEditor).GetMethod("SetPreviewSelection", Flags)!.Invoke(md, [new PreviewPosition(selection.IndexOf(item), 0), new PreviewPosition(selection.IndexOf(item), 5)]);
            md.FocusPreview();
            SyncKey();
            Check(document.Editor.CaretIndex == source.IndexOf("Paragraph 80", StringComparison.Ordinal) + 5, "Rendered preview selection caret also syncs to source");
            CheckEditorCentered();
            var codePosition = new PreviewPosition(selection.IndexOf(codeBlock), "first code line".Length + PreviewSelection.LineBreakLength + 6);
            typeof(MdEditor).GetMethod("SetPreviewSelection", Flags)!.Invoke(md, [codePosition, codePosition]);
            SyncKey();
            Check(document.Editor.CaretIndex == source.IndexOf("second code line", StringComparison.Ordinal) + 6, "Rendered code selection accounts for platform line break widths");
            Click(editButton);
            Check(!sync.IsEnabled, "Sync button disabled without both panes");
            document.Editor.Text = "# hi"; // keep teardown from opening an unsaved-changes prompt
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
