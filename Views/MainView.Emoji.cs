using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;

namespace Mdhub.Views;

public partial class MainView
{
    private bool CanInsertEmoji(NoteDocument document) => !_busy && _active == document
        && !document.IsViewOnly && !document.IsReadOnlyText && !document.Editor.IsReadOnly
        && TextEncodings.SupportsEmoji(document.Encoding);

    private void ShowEmojiPicker(Control target)
    {
        if (_active is not { } document || !CanInsertEmoji(document)) return;
        BuildEmojiPicker(document).ShowAt(target);
    }

    internal Flyout BuildEmojiPicker(NoteDocument document)
    {
        var grid = new WrapPanel { Width = 320 };
        var flyout = new Flyout
        {
            Content = new StackPanel { Spacing = 8, Children =
            {
                new TextBlock { Text = Loc.T("EmojiInput") }, grid
            } }
        };
        foreach (var emoji in "😀 😃 😄 😁 😆 😅 😂 🙂 😉 😊 😍 🥰 😎 🤔 😢 😭 😡 😱 😴 🤗 👍 👎 👏 🙌 👋 🙏 💪 ✌️ ❤️ 💔 ⭐ ✨ 🔥 🎉 🎊 🎁 ✅ ❌ ⚠️ ❓ 💡 📌 📝 📚 💻 🚀 🌍 🌈 ☀️ 🌙 🌸 🌳 🐱 🐶 🦋 🍎 🍕 ☕ ⚽ 🎵 👩‍💻 👨‍💻 👍🏽 🏳️‍🌈".Split(' '))
        {
            var button = new Button { Content = emoji, Width = 40, Height = 40, Padding = new Thickness(2), FontSize = 22 };
            AutomationProperties.SetName(button, emoji);
            button.Click += (_, _) =>
            {
                flyout.Hide();
                InsertEmoji(document, emoji);
            };
            grid.Children.Add(button);
        }
        return flyout;
    }

    internal void InsertEmoji(NoteDocument document, string emoji)
    {
        // A flyout can outlive a tab/encoding change. Never edit the wrong or newly read-only document.
        if (!CanInsertEmoji(document)) return;
        if (!TextEditor.IsVisible) SetViewMode(ViewMode.Split);
        document.Editor.Focus();
        document.RestoreSelection();
        var start = Math.Min(document.Editor.SelectionStart, document.Editor.SelectionEnd);
        document.Editor.SelectedText = emoji;
        document.Editor.SelectionStart = document.Editor.SelectionEnd = start + emoji.Length;
        document.Editor.Focus();
        UpdateStatus();
    }
}
