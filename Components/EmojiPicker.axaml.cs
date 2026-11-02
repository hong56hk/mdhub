using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.Media;
using System.Globalization;
using System.Text;
using Mdhub.Localization;
using Mdhub.Services;

namespace Mdhub.Components;

public partial class EmojiPicker : UserControl
{
    private static readonly (string? Id, string Icon)[] Categories =
    [
        (null, "☰"), ("Recent", "🕘"), ("Smileys", "😀"), ("People", "👋"), ("Nature", "🌿"), ("Food", "☕"),
        ("Travel", "✈️"), ("Activities", "⚽"), ("Objects", "💡"), ("Symbols", "❤️"), ("Flags", "🏳️")
    ];
    private string? _category;
    private readonly Dictionary<int, bool> _glyphs = [];
    public event Action<string>? EmojiSelected;
    public event Action? DismissRequested;

    public EmojiPicker()
    {
        InitializeComponent();
        foreach (var category in Categories)
        {
            var button = new ToggleButton { Name = "EmojiCategory" + (category.Id ?? "All"), Content = category.Icon, Tag = category.Id };
            button.Classes.Add("emojiCategory");
            button.Click += (_, _) => { _category = category.Id; Refresh(); };
            EmojiCategories.Children.Add(button);
        }
        EmojiSearchBox.TextChanged += (_, _) => Refresh();
        EmojiSearchBox.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Down && EmojiGrid.Children.FirstOrDefault() is Button first)
            {
                e.Handled = true;
                first.Focus();
            }
        }, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            DismissRequested?.Invoke();
        }, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => { Loc.Changed += Refresh; EmojiRecents.Changed += RefreshRecents; };
        DetachedFromVisualTree += (_, _) => { Loc.Changed -= Refresh; EmojiRecents.Changed -= RefreshRecents; };
        Refresh();
    }

    public void FocusSearch() => Dispatcher.UIThread.Post(() => EmojiSearchBox.Focus(), DispatcherPriority.Loaded);

    private void Refresh()
    {
        foreach (var button in EmojiCategories.Children.OfType<ToggleButton>())
        {
            var id = button.Tag as string;
            button.IsChecked = id == _category;
            var label = Loc.T("EmojiCategory" + (id ?? "All"));
            ToolTip.SetTip(button, label);
            AutomationProperties.SetName(button, label);
        }
        var results = EmojiCatalog.Search(EmojiSearchBox.Text, _category == "Recent" ? null : _category);
        if (_category == "Recent")
        {
            var matching = results.ToDictionary(e => e.Value, StringComparer.Ordinal);
            results = EmojiRecents.Values.Where(matching.ContainsKey).Select(value => matching[value]).ToList();
        }
        var matches = results.Where(CanDisplay).ToList();
        EmojiCategoryHeading.Text = $"{Loc.T("EmojiCategory" + (_category ?? "All"))} · {matches.Count}";
        EmojiGrid.Children.Clear();
        foreach (var emoji in matches)
        {
            var button = new Button { Content = emoji.Value, Tag = emoji };
            button.Classes.Add("emojiCell");
            ToolTip.SetTip(button, emoji.Name);
            AutomationProperties.SetName(button, emoji.Name);
            button.Click += (_, _) => EmojiSelected?.Invoke(emoji.Value);
            EmojiGrid.Children.Add(button);
        }
        EmojiScroll.Offset = default;
        EmojiScroll.IsVisible = matches.Count > 0;
        EmojiNoResults.IsVisible = matches.Count == 0;
        EmojiNoResults.Text = Loc.T(_category == "Recent" && EmojiRecents.Values.Count == 0 ? "EmojiNoRecents" : "EmojiNoResults");
    }

    private void RefreshRecents()
    {
        if (_category != "Recent") return;
        var focused = IsKeyboardFocusWithin;
        Refresh();
        if (focused) EmojiSearchBox.Focus();
    }

    private bool CanDisplay(EmojiEntry emoji)
    {
        foreach (var rune in emoji.Value.EnumerateRunes())
        {
            // Joiners and presentation selectors shape an emoji rather than supplying visible glyphs.
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.NonSpacingMark) continue;
            if (!_glyphs.TryGetValue(rune.Value, out var supported))
                _glyphs[rune.Value] = supported = FontManager.Current.TryMatchCharacter(rune.Value,
                    FontStyle.Normal, FontWeight.Normal, FontStretch.Normal, FontFamily, CultureInfo.CurrentCulture, out _);
            if (!supported) return false;
        }
        return true;
    }
}
