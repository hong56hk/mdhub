using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Services;

// Item 22: explorer indentation and same-level alignment of files, folders and the inline rename box.
internal static class ExplorerLayoutTests
{
    public static int Passed;
    public static void Run(bool setup = true)
    {
        if (setup) AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new MainWindow { Width = 1300, Height = 800 }; window.Show();
        void Pump() { using var timeout = new CancellationTokenSource(100); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Func<bool> done) { var deadline = DateTime.UtcNow.AddSeconds(10); while (!done() && DateTime.UtcNow < deadline) Pump(); }
        Pump();
        var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
        explorer.WatchEnabled = false;
        var tree = explorer.FindControl<TreeView>("FolderTree")!;
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-layout-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var sub = Directory.CreateDirectory(Path.Combine(root, "Alpha")).FullName;
            Directory.CreateDirectory(Path.Combine(sub, "Nested"));
            Directory.CreateDirectory(Path.Combine(sub, "Nested", "Deeper"));
            File.WriteAllText(Path.Combine(sub, "inner.md"), "x");
            File.WriteAllText(Path.Combine(sub, "Nested", "deep.md"), "x");
            Directory.CreateDirectory(Path.Combine(root, "Empty"));
            File.WriteAllText(Path.Combine(root, "top.md"), "x");
            var load = explorer.LoadRootAsync(root); Wait(() => load.IsCompleted); Pump();
            IEnumerable<TreeViewItem> Nodes(IEnumerable<TreeViewItem> items) { foreach (var i in items) { yield return i; foreach (var c in Nodes(i.Items.OfType<TreeViewItem>())) yield return c; } }
            TreeViewItem Node(string path) => Nodes(tree.Items.OfType<TreeViewItem>()).Single(i => i.Tag is FolderEntry e && e.Path == path);
            Node(sub).IsExpanded = true; Wait(() => Nodes(tree.Items.OfType<TreeViewItem>()).Any(i => i.Tag is FolderEntry e && e.Path == Path.Combine(sub, "Nested"))); Pump();
            Node(Path.Combine(sub, "Nested")).IsExpanded = true; Wait(() => Nodes(tree.Items.OfType<TreeViewItem>()).Any(i => i.Tag is FolderEntry e && e.Path.EndsWith("deep.md"))); Pump(); Pump();
            window.UpdateLayout();
            if (Environment.GetEnvironmentVariable("MDHUB_LAYOUT_DUMP") == "1")
                foreach (var n in new[] { Node(sub), Node(Path.Combine(root, "top.md")), Node(Path.Combine(sub, "inner.md")) })
                    foreach (var v in n.GetVisualDescendants().OfType<Control>().TakeWhile(v => v is not TreeViewItem).Take(25))
                        Console.WriteLine($"DUMP {n.Header} {v.GetType().Name}#{v.Name} x={v.TranslatePoint(default, tree)?.X:0.0} w={v.Bounds.Width:0.0} vis={v.IsVisible} op={v.Opacity}");
            double X(Visual v) => v.TranslatePoint(default, tree)!.Value.X;
            Control Part(TreeViewItem i, string name) => i.GetVisualDescendants().OfType<Control>().First(c => c.Name == name);
            double TextX(TreeViewItem i) => X(Part(i, "PART_HeaderPresenter").GetVisualDescendants().OfType<TextBlock>().First());
            double HeaderX(TreeViewItem i) => X(Part(i, "PART_HeaderPresenter"));
            // Item 78: a file row starts with its type icon; a folder row with its name.
            FileTypeIcon? RowIcon(TreeViewItem i) => Part(i, "PART_HeaderPresenter").GetVisualDescendants().OfType<FileTypeIcon>().FirstOrDefault();
            double IconX(TreeViewItem i) => RowIcon(i) is { } icon ? X(icon) : TextX(i);
            bool Same(double a, double b) => Math.Abs(a - b) < 0.5;
            var alpha = Node(sub); var empty = Node(Path.Combine(root, "Empty")); var top = Node(Path.Combine(root, "top.md"));
            var nested = Node(Path.Combine(sub, "Nested")); var inner = Node(Path.Combine(sub, "inner.md")); var deep = Node(Path.Combine(sub, "Nested", "deep.md"));
            Console.WriteLine($"INFO x: level0 {HeaderX(alpha)}/{HeaderX(top)}, level1 {HeaderX(nested)}/{HeaderX(inner)}, level2 {HeaderX(deep)}");
            // Item 31: a file's icon takes the chevron's slot, so it starts exactly where a sibling folder's chevron does.
            var deeper = Node(Path.Combine(sub, "Nested", "Deeper"));
            double ChevronX(TreeViewItem i) => X(Part(i, "PART_ExpandCollapseChevron"));
            foreach (var (file, folder, depth) in new[] { (top, alpha, 0), (top, empty, 0), (inner, nested, 1), (deep, deeper, 2) })
            {
                Console.WriteLine($"INFO depth {depth}: file icon {IconX(file)} folder chevron {ChevronX(folder)}");
                Check(Part(folder, "PART_ExpandCollapseChevron").IsVisible && RowIcon(file) is not null && Same(IconX(file), ChevronX(folder)), $"depth {depth}: file icon x equals the folder chevron x");
            }
            Check(Same(TextX(alpha), TextX(empty)) && Same(ChevronX(alpha), ChevronX(empty)), "level 0: folders align with each other");
            var step1 = HeaderX(inner) - HeaderX(top); var step2 = HeaderX(deep) - HeaderX(inner);
            Check(step1 >= 8 && step1 <= 12 && Same(step1, step2), $"indent per level is 8-12px and constant (got {step1:0.#}, {step2:0.#})");
            var fileSlot = Part(top, "PART_ExpandCollapseChevronContainer"); var folderSlot = Part(alpha, "PART_ExpandCollapseChevronContainer");
            Check(Same(fileSlot.Bounds.Width, 0) && Same(X(fileSlot), X(folderSlot)) && Same(folderSlot.Bounds.Width, 12) && !Part(top, "PART_ExpandCollapseChevron").IsVisible,
                "a file row has no chevron; its slot collapses so the icon takes the 12px chevron slot");
            foreach (var (row, sibling, level) in new[] { (top, alpha, 0), (inner, nested, 1), (deep, deep, 2), (nested, inner, 1) })
            {
                // Item 30: only the name becomes the box; the icon, font, text x and row height stay put.
                tree.SelectedItem = row; for (var k = 0; k < 6; k++) Pump();
                var rowText = Part(row, "PART_HeaderPresenter").GetVisualDescendants().OfType<TextBlock>().First();
                var entryName = Path.GetFileName(((FolderEntry)row.Tag!).Path);
                var rowIcon = RowIcon(row);
                var iconX = IconX(row);
                var nameX = X(rowText); var rowName = rowText.Text;
                var rowHeight = Part(row, "PART_Header").Bounds.Height;
                var (family, size, weight) = (rowText.FontFamily, rowText.FontSize, rowText.FontWeight);
                var lineHeight = rowText.TextLayout.Height;
                var kind = ((FolderEntry)row.Tag!).IsDirectory ? "folder" : "file";
                explorer.BeginRename(((FolderEntry)row.Tag!).Path); Pump(); Pump(); window.UpdateLayout();
                var box = row.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "ExplorerRenameBox");
                var presenter = box.GetVisualDescendants().OfType<TextPresenter>().First();
                var boxText = X(presenter);
                Console.WriteLine($"INFO rename level {level}: icon {iconX} name {nameX} box text {boxText}");
                Check(Same(boxText, nameX) && Same(iconX, IconX(row)) && rowName == entryName, $"inline rename text starts exactly where the name was (level {level}, {kind})");
                Check(box.Text == entryName && box.FontFamily == family && box.FontSize == size && box.FontWeight == weight && Same(presenter.TextLayout.Height, lineHeight),
                    $"rename box uses the row font ({family} {size} {weight}) with the same line height (level {level}, {kind})");
                Check(Same(Part(row, "PART_Header").Bounds.Height, rowHeight), $"row height is unchanged while renaming ({rowHeight:0.#}px, level {level}, {kind})");
                var icon = row.GetVisualDescendants().OfType<FileTypeIcon>().FirstOrDefault(b => b.Name == "ExplorerRenameIcon");
                Check(rowIcon is null ? icon is null : icon is { IsEffectivelyVisible: true } && Same(X(icon), iconX) && ReferenceEquals(icon.Data, rowIcon.Data),
                    $"the type icon stays visible at the same x while renaming (level {level}, {kind})");
                var frame = box.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
                Check(frame.BorderThickness == new Thickness(1) && frame.BorderBrush is ISolidColorBrush fb && fb.Color == ThemeService.Lookup("ExplorerRenameBorder") && box.Padding == default,
                    $"rename box has a 1px ExplorerRenameBorder border and no padding (level {level}, {kind})");
                Check(box.SelectionStart == 0 && box.SelectionEnd == FolderExplorer.RenameSelectionLength(entryName, kind == "folder"), $"the name is pre-selected without its extension (level {level}, {kind})");
                box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape }); Pump();
            }
            FocusColors(window, tree, top, Node(Path.Combine(sub, "inner.md")), Node(Path.Combine(sub, "Nested")), Node(Path.Combine(root, "Empty")), Pump);
            foreach (var d in window.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs").Items.OfType<Mdhub.Models.NoteDocument>()) d.SavedText = d.Text;
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }
    // Item 29: VS Code explorer row colours follow focus (focused selection with a border, inactive selection, hover).
    private static void FocusColors(Window window, TreeView tree, TreeViewItem top, TreeViewItem inner, TreeViewItem hoverRow, TreeViewItem folderRow, Action pump)
    {
        void Pump() { for (var k = 0; k < 4; k++) pump(); }
        var view = window.GetVisualDescendants().OfType<Mdhub.Views.MainView>().Single();
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        void Open(string p) { var t = (Task)typeof(Mdhub.Views.MainView).GetMethod("OpenPathAsync", flags)!.Invoke(view, [p])!; var end = DateTime.UtcNow.AddSeconds(10); while (!t.IsCompleted && DateTime.UtcNow < end) pump(); Pump(); }
        var tabs = window.GetVisualDescendants().OfType<ListBox>().First(c => c.Name == "Tabs");
        TextBox Editor() => view.FindControl<TextEditor>("TextEditor")!.GetVisualDescendants().OfType<TextBox>().First(b => b.IsEffectivelyVisible && b.Name != "FindBox" && b.Name != "ReplaceBox" && b.AcceptsReturn);
        Border Root(TreeViewItem i) => i.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_LayoutRoot");
        Color? Bg(TreeViewItem i) => (Root(i).Background as ISolidColorBrush)?.Color;
        Color? Line(TreeViewItem i) => (Root(i).BorderBrush as ISolidColorBrush)?.Color;
        Point At(Control c) => c.TranslatePoint(new Point(20, c.Bounds.Height / 2), window)!.Value;
        Point HeaderPos(TreeViewItem i) => i.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_HeaderPresenter").TranslatePoint(default, tree)!.Value;
        void Key(Key k, RawInputModifiers m = RawInputModifiers.None) { window.KeyPress(k, m, PhysicalKey.None, null); window.KeyRelease(k, m, PhysicalKey.None, null); Pump(); }
        void Click(Point p) { window.MouseDown(p, MouseButton.Left); window.MouseUp(p, MouseButton.Left); Pump(); }
        var parkPoint = new Point(window.Bounds.Width - 5, window.Bounds.Height - 5);
        var explorer0 = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
        var topPath = ((FolderEntry)top.Tag!).Path; var innerPath = ((FolderEntry)inner.Tag!).Path;
        try
        {
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                ThemeService.Apply(theme, persist: false); Pump();
                var dark = theme == AppTheme.Dark;
                Color C(string key) => ThemeService.Lookup(key, theme);
                Check(C("ExplorerSelectionBackground") == Color.Parse(dark ? "#04395E" : "#CCE8FF") && C("ExplorerSelectionBorder") == Color.Parse("#0078D4")
                    && C("ExplorerSelectionInactiveBackground") == Color.Parse(dark ? "#37373D" : "#E4E6F1") && C("ExplorerHoverBackground") == Color.Parse(dark ? "#2A2D2E" : "#E8E8E8"),
                    $"{theme}: explorer row colours are Theme.axaml resources with the VS Code values");
                window.MouseMove(parkPoint); Open(topPath); Open(innerPath);

                // Item 30 (VS Code): a single click opens the file but keeps focus in the explorer, so the row stays focused-blue.
                Click(At(Root(top)));
                Check(top.IsSelected && tree.IsKeyboardFocusWithin && Bg(top) == C("ExplorerSelectionBackground") && Line(top) == C("ExplorerSelectionBorder") && tabs.SelectedItem is Mdhub.Models.NoteDocument { Path: var clicked } && clicked == topPath,
                    $"{theme}: a single click opens the file and keeps the explorer focused");
                Key(Avalonia.Input.Key.Enter);
                Check(Editor().IsKeyboardFocusWithin && !tree.IsKeyboardFocusWithin && Bg(top) == C("ExplorerSelectionInactiveBackground"), $"{theme}: Enter on a file moves focus to the editor");
                Click(At(Root(inner)));
                Check(tree.IsKeyboardFocusWithin && tabs.SelectedItem is Mdhub.Models.NoteDocument { Path: var p2 } && p2 == innerPath, $"{theme}: a single click from the editor focuses the explorer and opens the file");
                window.MouseDown(At(Root(top)), MouseButton.Left); window.MouseUp(At(Root(top)), MouseButton.Left);
                window.MouseDown(At(Root(top)), MouseButton.Left, RawInputModifiers.None); window.MouseUp(At(Root(top)), MouseButton.Left); Pump();
                Check(Editor().IsKeyboardFocusWithin && tabs.SelectedItem is Mdhub.Models.NoteDocument { Path: var p3 } && p3 == topPath, $"{theme}: a double-click opens the file and moves focus to the editor");
                Click(At(Root(top)));
                Key(Avalonia.Input.Key.F2);
                Check(explorer0.IsRenaming, $"{theme}: after a single click F2 renames the row");
                explorer0.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "ExplorerRenameBox").RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape }); Pump();
                // Clicking a folder row keeps focus in the explorer.
                Click(At(Root(folderRow)));
                Check(tree.IsKeyboardFocusWithin && folderRow.IsSelected && Bg(folderRow) == C("ExplorerSelectionBackground") && Line(folderRow) == C("ExplorerSelectionBorder") && Root(folderRow).BorderThickness == new Thickness(1),
                    $"{theme}: explorer focused, the selected row has the focused background and a 1px {C("ExplorerSelectionBorder")} border");
                var focusedPos = HeaderPos(folderRow); var otherPos = HeaderPos(inner);
                Check(Line(inner) is { A: 0 } && Root(inner).BorderThickness == new Thickness(1), $"{theme}: unselected rows reserve a transparent 1px border");

                // Editor focused by a click: inactive selection, no border, nothing moves.
                window.MouseMove(parkPoint);
                Click(At(Editor()));
                Check(Editor().IsKeyboardFocusWithin && !tree.IsKeyboardFocusWithin, $"{theme}: clicking the editor moves focus there");
                var sel = (TreeViewItem)tree.SelectedItem!;
                Check(Bg(sel) == C("ExplorerSelectionInactiveBackground") && Line(sel) is { A: 0 } && Root(sel).BorderThickness == new Thickness(1),
                    $"{theme}: explorer unfocused, the selected row has the inactive background and a transparent border");
                Check(HeaderPos(folderRow) == focusedPos && HeaderPos(inner) == otherPos, $"{theme}: rows do not shift when focus changes");

                // Ctrl+Shift+E back to the explorer, then a tab switch back to the editor.
                Key(Avalonia.Input.Key.E, RawInputModifiers.Control | RawInputModifiers.Shift);
                sel = (TreeViewItem)tree.SelectedItem!;
                Check(tree.IsKeyboardFocusWithin && Bg(sel) == C("ExplorerSelectionBackground") && Line(sel) == C("ExplorerSelectionBorder"), $"{theme}: Ctrl+Shift+E focuses the explorer and the selection turns focused at once");
                var other = tabs.Items.OfType<Mdhub.Models.NoteDocument>().First(d => d.Path == innerPath);
                Click(At(tabs.ContainerFromItem(other)!));
                sel = (TreeViewItem)tree.SelectedItem!;
                Check(!tree.IsKeyboardFocusWithin && Bg(sel) == C("ExplorerSelectionInactiveBackground") && Line(sel) is { A: 0 }, $"{theme}: switching tabs takes focus from the explorer and the selection turns inactive");
                Check(sel == inner && Bg(folderRow) is null or { A: 0 }, $"{theme}: the inactive selection follows the active tab");
                Click(At(tabs.ContainerFromItem(tabs.Items.OfType<Mdhub.Models.NoteDocument>().First(d => d.Path == topPath))!));
                Check(tree.SelectedItem == top && Bg(top) == C("ExplorerSelectionInactiveBackground"), $"{theme}: switching back marks the other file");

                // Keyboard focus into the tree without a click (Tab-like): focused again.
                top.Focus(); Pump();
                Check(Bg(top) == C("ExplorerSelectionBackground"), $"{theme}: focusing a row by keyboard shows the focused selection");

                // Hover on an unselected row.
                window.MouseMove(At(Root(hoverRow))); Pump();
                Check(!hoverRow.IsSelected && Bg(hoverRow) == C("ExplorerHoverBackground") && Line(hoverRow) is { A: 0 }, $"{theme}: hovering an unselected row shows {C("ExplorerHoverBackground")} without a border");
                window.MouseMove(parkPoint); Pump();
                Check(Bg(hoverRow) is null or { A: 0 }, $"{theme}: hover clears when the pointer leaves");

                // Inline rename keeps the focused look and its own box.
                var explorer = window.GetVisualDescendants().OfType<FolderExplorer>().Single();
                tree.SelectedItem = top; top.Focus(); Pump();
                var headerBefore = HeaderPos(top).X;
                explorer.BeginRename(topPath); Pump();
                var box = top.GetVisualDescendants().OfType<TextBox>().First(b => b.Name == "ExplorerRenameBox");
                Check(box.IsKeyboardFocusWithin && Bg(top) == C("ExplorerSelectionBackground") && HeaderPos(top).X == headerBefore, $"{theme}: the row keeps the focused selection while renaming and does not move");
                Check(box.Background is ISolidColorBrush bb && bb.Color != C("ExplorerSelectionBackground") && bb.Color.A > 0, $"{theme}: the rename box keeps its own opaque background");
                box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape }); Pump();
            }
        }
        finally { ThemeService.Apply(AppTheme.Dark, persist: false); window.MouseMove(parkPoint); }
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Passed++;
        Console.WriteLine("PASS: " + message);
    }
}