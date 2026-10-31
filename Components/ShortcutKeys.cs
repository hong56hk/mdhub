using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Mdhub.Services;

namespace Mdhub.Components;

// Avalonia side of the shortcut table: KeyGestures, matching key events, and the menu-item attached id.
// Everything reads the live gestures (Shortcuts.Gestures), so custom bindings apply everywhere at once.
public sealed class ShortcutKeys : AvaloniaObject
{
    public static readonly AttachedProperty<string?> IdProperty = AvaloniaProperty.RegisterAttached<ShortcutKeys, MenuItem, string?>("Id");
    public static string? GetId(MenuItem item) => item.GetValue(IdProperty);
    public static void SetId(MenuItem item, string? value) => item.SetValue(IdProperty, value);

    public static bool IsMac => OperatingSystem.IsMacOS();

    public static KeyModifiers Modifiers(ShortcutMods mods)
    {
        var result = KeyModifiers.None;
        if (mods.HasFlag(ShortcutMods.Command)) result |= IsMac ? KeyModifiers.Meta : KeyModifiers.Control;
        if (mods.HasFlag(ShortcutMods.Shift)) result |= KeyModifiers.Shift;
        if (mods.HasFlag(ShortcutMods.Alt)) result |= KeyModifiers.Alt;
        return result;
    }

    public static bool IsKeyName(string name) => Enum.TryParse<Key>(name, out _);

    public static KeyGesture? Gesture(ShortcutGesture g) => Enum.TryParse<Key>(g.Key, out var key) ? new KeyGesture(key, Modifiers(g.Mods)) : null;
    public static KeyGesture? Gesture(string id) => Shortcuts.Get(id).Gestures.FirstOrDefault(g => !g.Hidden) is { Key: not null } g ? Gesture(g) : null;

    private static readonly HashSet<Key> ModifierKeys =
    [
        Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin,
        Key.System, Key.None, Key.ImeProcessed, Key.DeadCharProcessed
    ];

    // The gesture a key press records in the Shortcuts dialog; null while only modifiers are held.
    public static ShortcutGesture? FromKey(Key key, KeyModifiers modifiers)
    {
        if (ModifierKeys.Contains(key)) return null;
        var mods = ShortcutMods.None;
        if (modifiers.HasFlag(IsMac ? KeyModifiers.Meta : KeyModifiers.Control)) mods |= ShortcutMods.Command;
        if (modifiers.HasFlag(KeyModifiers.Shift)) mods |= ShortcutMods.Shift;
        if (modifiers.HasFlag(KeyModifiers.Alt)) mods |= ShortcutMods.Alt;
        return new ShortcutGesture(key.ToString(), mods);
    }

    // The id whose gesture exactly matches this key event (modifiers must be equal), or null.
    public static string? Match(KeyEventArgs e, Func<Shortcut, bool>? filter = null)
    {
        foreach (var s in Shortcuts.All)
        {
            if (filter is not null && !filter(s)) continue;
            foreach (var g in s.Gestures)
                if (Enum.TryParse<Key>(g.Key, out var key) && key == e.Key && Modifiers(g.Mods) == e.KeyModifiers) return s.Id;
        }
        return null;
    }

    // Menu items show their gesture (display only: InputGesture never fires a command, so no double-handling).
    private static readonly ConditionalWeakTable<MenuItem, TextBlock> GestureText = new();
    private static readonly List<WeakReference<MenuItem>> MenuItems = [];
    private static bool _listening;

    public static void ApplyGestures(IEnumerable<MenuItem> items)
    {
        if (!_listening) { _listening = true; Shortcuts.Changed += RefreshMenus; }
        foreach (var item in items)
        {
            if (GetId(item) is not null)
            {
                MenuItems.RemoveAll(w => !w.TryGetTarget(out _));
                MenuItems.Add(new WeakReference<MenuItem>(item));
                // Avalonia prints Oem keys by enum name (Ctrl+OemQuotes): show the table's text instead.
                item.TemplateApplied += (_, e) =>
                {
                    if (e.NameScope.Find<TextBlock>("PART_InputGestureText") is { } block)
                    {
                        GestureText.AddOrUpdate(item, block);
                        block.Text = MenuText(item);
                    }
                };
                Refresh(item);
            }
            ApplyGestures(item.Items.OfType<MenuItem>());
        }
    }

    public static string MenuText(MenuItem item) => GetId(item) is { } id ? Shortcuts.Format(id, IsMac).Split(" / ")[0] : "";

    private static void Refresh(MenuItem item)
    {
        if (GetId(item) is not { } id) return;
        item.InputGesture = Gesture(id);
        if (GestureText.TryGetValue(item, out var block)) block.Text = MenuText(item);
    }

    private static void RefreshMenus()
    {
        foreach (var weak in MenuItems.ToList())
            if (weak.TryGetTarget(out var item)) Refresh(item);
    }
}
