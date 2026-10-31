using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 75: the file tab context menu (Close tab / Close other tabs).
internal static class TabMenuTests
{
    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Settle() { for (var i = 0; i < 8; i++) Pump(); }
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        SessionManager.Enabled = false;
        var w = new MainWindow { Width = 1100, Height = 760 };
        try
        {
            w.Show(); Settle();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            var tabs = w.GetVisualDescendants().OfType<FileTabs>().Single();
            var list = tabs.FindControl<ListBox>("Tabs")!;
            List<NoteDocument> Docs() => list.Items.OfType<NoteDocument>().ToList();
            NoteDocument Open(string text, string name) { var d = (NoteDocument)typeof(MainView).GetMethod("AddDocument", flags)!.Invoke(view, [null, text, name, null])!; Settle(); return d; }
            foreach (var d in Docs()) d.SavedText = d.Text;
            var a = Open("a", "a.md"); var b = Open("b", "b.md"); var c = Open("c", "c.md");
            foreach (var d in new[] { a, b, c }) d.SavedText = d.Text;
            var menu = list.ContextMenu!;
            Control TabOf(NoteDocument d) => list.GetVisualDescendants().OfType<ListBoxItem>().First(i => i.DataContext == d).GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == d.Title);
            void RightClick(Control target) { target.RaiseEvent(new ContextRequestedEventArgs()); Settle(); }
            MenuItem Item(string name) => menu.Items.OfType<MenuItem>().Single(i => i.Name == name);
            void Click(string name) { Item(name).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.Close(); Settle(); }
            void Answer(string choice)
            {
                for (var i = 0; i < 40 && w.OwnedWindows.Count == 0; i++) Pump();
                var dialog = w.OwnedWindows.Last();
                dialog.GetVisualDescendants().OfType<Button>().First(x => x.Content as string == Loc.T(choice) || x.Content as string == choice).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Settle();
            }

            Check(menu is not null && menu.Items.OfType<MenuItem>().Select(i => i.Name).SequenceEqual(["TabCloseMenuItem", "TabCloseOthersMenuItem"]), "The tab strip has a context menu: Close tab, Close other tabs");
            Check(tabs.SelectedDocument == c, "c is active");
            RightClick(TabOf(a));
            Check(menu.IsOpen && tabs.MenuTarget == a, "Right-clicking a tab opens the menu for that tab");
            Check(Item("TabCloseMenuItem").Header as string == Loc.T("CloseTab") && Item("TabCloseOthersMenuItem").Header as string == Loc.T("CloseOtherTabs"), "Localized headers");
            Check(Item("TabCloseMenuItem").InputGesture?.ToString() == "Ctrl+W" && Item("TabCloseOthersMenuItem").InputGesture is null, $"Close tab shows its Ctrl+W hint ({Item("TabCloseMenuItem").InputGesture})");
            Check(Item("TabCloseMenuItem").Padding == LocResources.MenuItemPadding && Item("TabCloseOthersMenuItem").IsEnabled, "Standard menu padding; Close other tabs enabled with several tabs");
            Click("TabCloseMenuItem");
            Check(!Docs().Contains(a) && Docs().Contains(c) && tabs.SelectedDocument == c, "Close tab closes the clicked tab, not the active one");

            // Empty strip: no menu.
            RightClick(list);
            Check(!menu.IsOpen, "Right-clicking the empty strip opens no menu");

            // Dirty tab: the unsaved-changes prompt, Cancel keeps it.
            b.Editor.Text = "b changed"; Settle();
            Check(b.IsDirty, "b is dirty");
            RightClick(TabOf(c)); Click("TabCloseOthersMenuItem");
            Answer("Cancel");
            Check(Docs().Contains(b) && Docs().Contains(c), "Close other tabs prompts for a dirty tab; Cancel keeps it open");
            RightClick(TabOf(c)); Click("TabCloseOthersMenuItem");
            Answer("Discard");
            Check(Docs().SequenceEqual([c]) && tabs.SelectedDocument == c, "Discard closes it; only the clicked tab stays and is active");

            RightClick(TabOf(c));
            Check(menu.IsOpen && !Item("TabCloseOthersMenuItem").IsEnabled && Item("TabCloseMenuItem").IsEnabled, "With one tab, Close other tabs is disabled");
            menu.Close(); Settle();

            Check(Loc.Languages.Where(l => l.Code != Loc.English).All(l => Loc.Table(l.Code).TryGetValue("CloseOtherTabs", out var v) && v != Loc.En["CloseOtherTabs"]), "Close other tabs is translated in all 8 languages");
            foreach (var d in Docs()) d.SavedText = d.Text;
        }
        finally { foreach (var o in w.OwnedWindows.ToList()) o.Close(); w.Close(); }
        Console.WriteLine($"Tab menu tests: {passed} passed");
    }
}