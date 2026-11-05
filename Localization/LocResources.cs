using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Media;

namespace Mdhub.Localization;

// Publishes the current language's strings as application resources so XAML DynamicResource lookups switch live in
// every window, and sets a CJK font chain (default font first, then that language's fonts) so nothing renders as boxes.
public static class LocResources
{
    private static Application? _app;
    private static ResourceDictionary? _current;
    // Menu items built in code (context menus are not in the tree until opened) and the key of their header.
    private static readonly ConditionalWeakTable<MenuItem, string> MenuKeys = new();

    public static void Install(Application app)
    {
        _app = app;
        Loc.Changed -= Apply;
        Loc.Changed += Apply;
        Apply();
    }

    private static void Apply()
    {
        if (_app is null) return;
        var next = new ResourceDictionary();
        foreach (var key in Loc.En.Keys) next[key] = Loc.T(key);
        if (FontFor(Loc.Info) is { } font) next["ContentControlThemeFontFamily"] = font;
        // One swap per change, so every window re-reads its resources once.
        if (_current is not null) _app.Resources.MergedDictionaries.Remove(_current);
        _app.Resources.MergedDictionaries.Add(_current = next);
    }

    public static FontFamily? FontFor(LanguageInfo language) =>
        language.Fonts.Length == 0 ? null : new FontFamily(DefaultFontName + ", " + language.Fonts);

    private static string DefaultFontName
    {
        get
        {
            try { return FontManager.Current.DefaultFontFamily.Name; }
            catch (Exception) { return "Segoe UI"; }
        }
    }

    // Settings > Language: switch now and remember the choice.
    public static void Choose(string code)
    {
        if (Loc.Normalize(code) is not { } normalized) return;
        Loc.Chosen = normalized;
        Loc.Set(normalized);
        Services.SessionManager.SaveSettings();
    }

    public static MenuItem Item(string key) => Track(new MenuItem(), key);

    // Item 100: an existing (e.g. XAML) item labelled from a key; Refresh relabels it when its menu opens.
    // DynamicResource headers in a closed ContextMenu miss the resource swap and stay in the old language.
    public static MenuItem Track(MenuItem item, string key)
    {
        item.Header = Loc.T(key);
        MenuKeys.AddOrUpdate(item, key);
        return item;
    }

    // Refreshes the headers of code-built items (call when the menu opens).
    public static void Refresh(ItemsControl menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (MenuKeys.TryGetValue(item, out var key)) item.Header = Loc.T(key);
            Refresh(item);
        }
    }

    // Item 45: the padding every context menu gets, set as a local value so it applies from the very first measure
    // (a style can land after the popup first lays out, which left the first open without padding).
    public static readonly Thickness MenuPadding = new(2, 4, 2, 6);
    // Bug 55: the item padding. It used to come only from the Fluent MenuItem theme, which resolves when the item
    // joins the popup's tree; on a menu's first open the items could be laid out before that, so the text touched
    // the left edge until the second open. A local value is there before the first measure.
    public static readonly Thickness MenuItemPadding = new(21, 2, 11, 2);
    public static void ApplyItemPadding(ItemsControl menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.Padding = MenuItemPadding;
            ApplyItemPadding(item);
        }
    }

    // Bug 55 (root cause): Avalonia 12.1.4 regression AvaloniaUI/Avalonia#22573 (fixed by PR #22624, not yet released).
    // A popup with PlacementMode.Pointer (every right-clicked ContextMenu) keeps the window size of its first layout
    // pass. Fluent menu items share the shortcut column (SharedSizeGroup), which settles only after that pass, so on
    // a menu's first open the native popup window stayed narrower than the menu and the menu was centered in it:
    // the border, the left padding and the right end of the hints were clipped. The second open reuses the settled
    // size and looked right. Workaround from the issue: open at the pointer with AnchorAndGravity placement (a
    // 1x1 PlacementRect at the click, top-left anchor, bottom-right gravity), which still resizes the popup.
    public static readonly PlacementMode MenuPlacement = PlacementMode.AnchorAndGravity;
    private static readonly ConditionalWeakTable<ContextMenu, object> Pointed = new();
    private static bool _pointerHooked;
    private static void HookPointerPlacement()
    {
        if (_pointerHooked) return;
        _pointerHooked = true;
        // At the event's source, before the owner's ContextMenu handler (which runs later in the bubble): put the
        // anchor rectangle at the click, relative to the source, which ContextMenu uses as its placement target.
        Control.ContextRequestedEvent.AddClassHandler<Control>((source, e) =>
        {
            if (!ReferenceEquals(source, e.Source)) return;
            for (Visual? v = source; v is not null; v = v.GetVisualParent())
            {
                if (v is not Control { ContextMenu: { } menu }) continue;
                if (Pointed.TryGetValue(menu, out _))
                {
                    // Bug 94b: a source smaller than the click area (the editor reports its text presenter, which is only
                    // as big as the text) clamped a click below the text onto the presenter's edge, so the menu opened
                    // above the pointer. Then open the menu on its owner (which contains the click) at the click.
                    var owner = (Control)v;
                    var outside = e.TryGetPosition((Control)source, out var at) && !new Rect(((Control)source).Bounds.Size).Contains(at);
                    var target = outside && e.TryGetPosition(owner, out var p) && new Rect(owner.Bounds.Size).Contains(p) ? owner : (Control)source;
                    menu.PlacementRect = e.TryGetPosition(target, out var point) ? AtPointer(point, target.Bounds.Size) : null;
                    // Item 101: retarget instead of opening here. The owner's own ContextMenu handler (later in this
                    // bubble) then opens it on the owner through the normal route, raising Opening, so a click below
                    // the text runs every opening handler (Undo/Redo state, spelling, pane activation) like any other.
                    if (target != source && !e.Handled) e.Source = owner;
                }
                return;
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    // The positioner drops an anchor rectangle outside the target (falling back to the whole target), so a click in a
    // source's padding (the editor reports the text presenter as source) is clamped onto its edge.
    public static Rect AtPointer(Point point, Size bounds) =>
        new(Math.Clamp(point.X, 0, Math.Max(0, bounds.Width - 1)), Math.Clamp(point.Y, 0, Math.Max(0, bounds.Height - 1)), 1, 1);

    // Item 100: ContextMenu.Open(control) does not raise Opening (only a ContextRequested on the owner does), so a menu
    // opened in code would keep the headers of the language it was last opened in. Relabel and pad first.
    public static void Open(ContextMenu menu, Control owner)
    {
        ApplyItemPadding(menu);
        Refresh(menu);
        menu.Open(owner);
    }

    public static ContextMenu Localized(this ContextMenu menu)
    {
        HookPointerPlacement();
        Pointed.AddOrUpdate(menu, menu);
        menu.Placement = MenuPlacement;
        menu.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.TopLeft;
        menu.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.BottomRight;
        menu.Closed += (_, _) => menu.PlacementRect = null;
        menu.Padding = MenuPadding;
        ApplyItemPadding(menu);
        // Fluent's template puts a 4px margin above and below the items; drop it so the padding is 10px on every side.
        // TemplateApplied runs before the popup's first measure, so the first open matches later ones.
        menu.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Avalonia.Controls.Presenters.ItemsPresenter>("PART_ItemsPresenter") is { } items) items.Margin = default;
        };
        // Items added after creation get the padding before the popup lays them out.
        menu.Opening += (_, _) => { ApplyItemPadding(menu); Refresh(menu); };
        return menu;
    }
}