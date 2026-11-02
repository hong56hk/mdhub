using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

internal static class EmojiPickerTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        SessionManager.Enabled = false;
        Shortcuts.ResetAll(); Shortcuts.ReleaseAll();
        EmojiRecents.Import([]);
        var window = new MainWindow { Width = 1200, Height = 800 };
        void Pump() { using var timeout = new CancellationTokenSource(60); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 4; i++) Pump(); }
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); Console.WriteLine("PASS " + message); }
        Flyout? flyout = null;
        try
        {
            window.Show(); Settle();
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            NoteDocument Active() => (NoteDocument)typeof(MainView).GetField("_active", Flags)!.GetValue(view)!;
            var document = Active();
            var toolbar = window.GetVisualDescendants().OfType<TextFormatter>().Single();
            var anchor = toolbar.FindControl<Button>("EmojiButton")!;
            document.Editor.Text = "hello world";
            document.Editor.Focus();
            document.Editor.SelectionStart = 6; document.Editor.SelectionEnd = 11;
            flyout = (Flyout)typeof(MainView).GetMethod("BuildEmojiPicker", Flags)!.Invoke(view, [document])!;
            var panel = (EmojiPicker)flyout.Content!;
            var search = panel.FindControl<TextBox>("EmojiSearchBox")!;
            var grid = panel.FindControl<UniformGrid>("EmojiGrid")!;
            var categories = panel.FindControl<StackPanel>("EmojiCategories")!;
            var empty = panel.FindControl<TextBlock>("EmojiNoResults")!;
            List<Button> Buttons() => grid.Children.OfType<Button>().ToList();
            void Query(string text) { search.Text = text; Settle(); }
            void Category(string id) { categories.Children.OfType<ToggleButton>().Single(b => b.Name == "EmojiCategory" + id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); }
            void Choose(string emoji) { var button = Buttons().Single(b => Equals(b.Content, emoji)); button.Focus(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle(); }
            flyout.ShowAt(anchor); Settle();
            Check(flyout.IsOpen && search.IsKeyboardFocusWithin, "Opening the picker focuses its search input");
            Check(Buttons().Count > 250 && categories.Children.Count == 11 && categories.Children[1].Name == "EmojiCategoryRecent", "Recent appears immediately after All, followed by the nine categories");
            Check(EmojiCatalog.Entries.Select(e => e.Value).Distinct().Count() == EmojiCatalog.Entries.Count, "Emoji entries have unique sequences");
            Check(Buttons().GroupBy(b => Math.Round(b.Bounds.Y, 2)).All(row => row.Count() <= 9)
                && Buttons().Take(9).Select(b => b.Bounds.X).Distinct().Count() == 9, "The rendered grid contains at most nine emojis per row");
            Check(categories.Children.OfType<ToggleButton>().All(b => b.HorizontalContentAlignment == Avalonia.Layout.HorizontalAlignment.Center
                && b.VerticalContentAlignment == Avalonia.Layout.VerticalAlignment.Center), "Category icons are centered in both directions");
            Category("Recent");
            Check(Buttons().Count == 0 && empty.IsVisible && empty.Text!.Contains("Recently used"), "Recent has a helpful empty state");
            Category("All");
            Query("grateful");
            Check(Buttons().Any(b => Equals(b.Content, "🙏")) && !Buttons().Any(b => Equals(b.Content, "🍕")), "Meaning search finds gratitude without requiring an emoji name");
            Query("happy birthday");
            Check(Buttons().Any(b => Equals(b.Content, "🎂")) && Buttons().Any(b => Equals(b.Content, "🎉")), "Multiple meaning keywords find birthday and celebration emojis");
            Query("CoFfEe");
            Check(Buttons().Any(b => Equals(b.Content, "☕")) && EmojiCatalog.Search("cafe").Any(e => e.Value == "☕"), "Search ignores case and accepts common aliases");
            Query(""); Category("Nature");
            Check(Buttons().Count > 20 && Buttons().All(b => ((EmojiEntry)b.Tag!).Category == "Nature"), "A category filters the emoji grid");
            Query("dog pet");
            Check(Buttons().Count == 1 && Equals(Buttons()[0].Content, "🐶"), "Meaning search combines with the selected category");
            Category("Food");
            Check(Buttons().Count == 0 && empty.IsVisible, "An empty category search shows a clear no-results message");
            Query("");
            Check(Buttons().Any(b => Equals(b.Content, "🍕")) && !empty.IsVisible, "Clearing search restores the category results");
            Category("All"); Query("technologist");
            Choose("👩‍💻");
            Check(document.Text == "hello 👩‍💻" && document.Editor.CaretIndex == document.Text.Length && flyout.IsOpen,
                "Selecting an emoji replaces the saved editor selection and keeps the panel open");
            Choose("👩‍💻");
            Check(document.Text == "hello 👩‍💻👩‍💻" && flyout.IsOpen, "Repeated selection inserts at the updated cursor without closing the panel");
            Query("thumbs up"); Choose("👍🏽");
            Check(document.Text == "hello 👩‍💻👩‍💻👍🏽", "Insertion preserves skin tones and joined emoji sequences");
            var before = document.Text;
            document.Editor.IsReadOnly = true;
            Choose("👍🏽");
            Check(document.Text == before, "An open picker cannot edit a document that becomes read-only");
            document.Editor.IsReadOnly = false;
            document.Encoding = TextEncodings.Strict(1252)!;
            Choose("👍🏽");
            Check(document.Text == before, "An open picker respects an encoding change");
            document.Encoding = TextFileStore.DefaultEncoding;
            Query(""); Category("Recent");
            Check(Buttons().Select(b => (string)b.Content!).SequenceEqual(["👍🏽", "👩‍💻"]), "Recent lists successful insertions newest first without duplicates");
            Query("technologist");
            Check(Buttons().Count == 1 && Equals(Buttons()[0].Content, "👩‍💻"), "Meaning search filters Recent");
            Choose("👩‍💻"); Query("");
            Check(Buttons().Select(b => (string)b.Content!).SequenceEqual(["👩‍💻", "👍🏽"]) && flyout.IsOpen && panel.IsKeyboardFocusWithin,
                "Reusing a recent emoji promotes it and keeps the picker open and focused");
            var saved = SessionStore.Deserialize(SessionStore.Serialize(new SessionData { RecentEmojis = [.. EmojiRecents.Values] }))!;
            EmojiRecents.Import(saved.RecentEmojis.Concat(["not an emoji", "👩‍💻"]));
            Check(EmojiRecents.Values.SequenceEqual(["👩‍💻", "👍🏽"]), "Recent history round-trips through saved settings and ignores invalid or duplicate values");
            search.Focus();
            search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Settle();
            Check(!flyout.IsOpen && document.Editor.IsFocused, "Escape closes the picker and returns focus to the editor");
            flyout.ShowAt(anchor); Settle();
            window.MouseDown(new Point(30, 160), MouseButton.Left); window.MouseUp(new Point(30, 160), MouseButton.Left); Settle();
            Check(!flyout.IsOpen, "Clicking outside closes the picker");
            void EmojiShortcut()
            {
                window.KeyPress(Key.OemPeriod, RawInputModifiers.Control, PhysicalKey.None, null);
                window.KeyRelease(Key.OemPeriod, RawInputModifiers.Control, PhysicalKey.None, null);
                Settle();
            }
            Flyout? ShortcutPicker() => (Flyout?)typeof(MainView).GetField("_emojiPicker", Flags)!.GetValue(view);
            document.Editor.Focus(); EmojiShortcut();
            var openedByShortcut = ShortcutPicker();
            Check(openedByShortcut?.IsOpen == true && ((EmojiPicker)openedByShortcut.Content!).FindControl<TextBox>("EmojiSearchBox")!.IsFocused
                && Shortcuts.Get("editor.emoji").Format(false) == "Ctrl+.", "Ctrl+. opens the picker with search focused and is listed in Shortcuts");
            var shortcutSearch = ((EmojiPicker)openedByShortcut!.Content!).FindControl<TextBox>("EmojiSearchBox")!;
            shortcutSearch.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Settle();
            anchor.Focus(); EmojiShortcut();
            Check(ShortcutPicker()?.IsOpen == true, "Ctrl+. can reopen the picker from toolbar focus");
            ShortcutPicker()!.Hide(); Settle();
            document.Editor.IsReadOnly = true; document.Editor.Focus(); EmojiShortcut();
            Check(ShortcutPicker()?.IsOpen != true, "The shortcut respects read-only documents");
            document.Editor.IsReadOnly = false;
            var viewer = toolbar.FindControl<Button>("ViewerOnlyButton")!;
            viewer.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
            flyout.ShowAt(anchor); Settle(); Category("All"); Query("coffee"); Choose("☕");
            Check(document.Text.EndsWith("☕", StringComparison.Ordinal) && flyout.IsOpen && panel.IsKeyboardFocusWithin,
                "Insertion from preview-only mode reveals the source and retains picker focus");
            Category("Smileys"); Query("");
            if (Environment.GetEnvironmentVariable("MDHUB_EMOJI_PREVIEW") is { Length: > 0 } preview)
            {
                using var frame = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(panel.Bounds.Width), (int)Math.Ceiling(panel.Bounds.Height)));
                frame.Render(panel);
                frame.Save(preview, PngBitmapEncoderOptions.Default);
            }
            flyout.Hide();
            Console.WriteLine("Emoji picker tests passed.");
        }
        finally
        {
            flyout?.Hide();
            var view = window.GetVisualDescendants().OfType<MainView>().FirstOrDefault();
            if (view is not null) { typeof(MainView).GetField("_allowClose", Flags)!.SetValue(view, true); view.StopPreview(); }
            window.Close();
        }
    }
}
