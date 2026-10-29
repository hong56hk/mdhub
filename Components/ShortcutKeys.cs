using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Mdhub.Services;

namespace Mdhub.Components;

// Avalonia side of the shortcut table: KeyGestures, matching key events, and the menu-item attached id.
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

    public static KeyGesture Gesture(ShortcutGesture g) => new(Enum.Parse<Key>(g.Key), Modifiers(g.Mods));
    public static KeyGesture Gesture(string id) => Gesture(Shortcuts.Get(id).Gestures[0]);

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
    public static void ApplyGestures(IEnumerable<MenuItem> items)
    {
        foreach (var item in items)
        {
            if (GetId(item) is { } id)
            {
                item.InputGesture = Gesture(id);
                // Avalonia prints Oem keys by enum name (Ctrl+OemQuotes): show the table's text instead.
                var text = Shortcuts.Format(id, IsMac).Split(" / ")[0];
                item.TemplateApplied += (_, e) =>
                {
                    if (e.NameScope.Find<TextBlock>("PART_InputGestureText") is { } block)
                        block.Text = text;
                };
            }
            ApplyGestures(item.Items.OfType<MenuItem>());
        }
    }
}