using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Localization;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;
using IOPath = System.IO.Path;

// Item 52 (VS Code): binary detection, plain-text tabs, the binary placeholder and Open with default app.
internal static class BinaryFileTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        var root = IOPath.Combine(IOPath.GetTempPath(), $"mdhub-binary-{Guid.NewGuid():N}"); Directory.CreateDirectory(root);
        string F(string name, byte[] bytes) { var p = IOPath.Combine(root, name); File.WriteAllBytes(p, bytes); return p; }
        var launched = new List<string>();
        DefaultApp.LauncherOverride = launched.Add;
        var w = new MainWindow { Width = 1300, Height = 700 };
        var startLanguage = Loc.Info.Code;
        try
        {
            // --- Detection ---
            var nul = F("zero.dat", [(byte)'a', (byte)'b', 0, (byte)'c']);
            var fewControls = F("few.dat", [.. System.Text.Encoding.ASCII.GetBytes(new string('x', 95)), 1, 2, 3, 4, 5]);       // 5%
            var manyControls = F("many.dat", [.. System.Text.Encoding.ASCII.GetBytes(new string('x', 85)), .. Enumerable.Repeat((byte)1, 15)]); // 15%
            var lateNul = F("late.dat", [.. System.Text.Encoding.ASCII.GetBytes(new string('x', 9000)), 0]);
            var json = F("data.json", [(byte)'{', 0, 1, (byte)'}']);
            var yml = F("c.yml", [1, 2, 3]);
            var csv = F("t.csv", [0]);
            var extless = F("README", System.Text.Encoding.UTF8.GetBytes("plain readme\n"));
            var png = F("pic.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));
            Check(FileSupport.Detect(nul) == SupportedFileKind.Unsupported, "A NUL byte in the first 8 KB means binary");
            Check(FileSupport.Detect(fewControls) == SupportedFileKind.Text, "5% control characters is still text");
            Check(FileSupport.Detect(manyControls) == SupportedFileKind.Unsupported, "More than 10% control characters means binary");
            Check(FileSupport.Detect(lateNul) == SupportedFileKind.Text, "Only the first 8 KB is sampled (a NUL after it does not count)");
            Check(new[] { json, yml, csv }.All(p => FileSupport.Detect(p) == SupportedFileKind.Text) && new[] { ".txt", ".log", ".ini", ".xml", ".cs", ".yaml" }.All(e => FileSupport.IsTextByExtension("x" + e)), "Text extensions (.json .yaml/.yml .log .csv .ini .xml .cs …) skip the check");
            Check(FileSupport.Detect(extless) == SupportedFileKind.Text && FileSupport.Detect(png) == SupportedFileKind.Image, "Extensionless text is text; images stay images");

            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            var tabs = w.GetVisualDescendants().OfType<FileTabs>().Single().FindControl<ListBox>("Tabs")!;
            var placeholder = w.GetVisualDescendants().OfType<BinaryPlaceholder>().Single();
            var editor = w.GetVisualDescendants().OfType<TextEditor>().Single();
            var image = w.GetVisualDescendants().OfType<ImageViewer>().Single();
            var md = w.GetVisualDescendants().OfType<MdEditor>().Single();
            var bar = w.GetVisualDescendants().OfType<TextFormatter>().Single();
            var status = w.GetVisualDescendants().OfType<BottomStatusBar>().Single();
            TextBlock S(string name) => status.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == name);
            void Open(string path) => Wait((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(v, [path])!);
            NoteDocument Active() => (NoteDocument)tabs.SelectedItem!;
            var confirms = new List<(string Title, string Message)>();
            var answer = true;
            typeof(MainView).GetProperty("ConfirmOverride", Flags)!.SetValue(v, (Func<string, string, string, Task<bool>>)((t, m, c) => { confirms.Add((t, m)); return Task.FromResult(answer); }));

            // --- Plain text ---
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            TextFileStore.AnsiCodePageOverride = 950;
            var big5Text = "**not bold** \u8003\u8eca\r\n";
            var log = F("app.log", System.Text.Encoding.GetEncoding(950).GetBytes(big5Text));
            Open(log);
            Check(Active().Path == log && Active().Text == big5Text && !Active().IsMarkdown && !Active().IsBinary, "A .log opens as plain text with Big5 detection (bug 39)");
            Check(editor.IsVisible && !md.IsVisible && !placeholder.IsVisible && !bar.FindControl<Button>("TogglePreviewButton")!.IsEnabled, "Plain text: no preview, Toggle Preview disabled");
            Check(!((MarkdownTextBox)Active().Editor).HighlightMarkdown, "Plain text: no Markdown highlighting");
            Check(S("FileKindLabel").Text == Loc.T("KindPlain") && S("EncodingLabel").Text == "Big5", $"Status bar: '{S("FileKindLabel").Text} · {S("EncodingLabel").Text}'");
            TextFileStore.AnsiCodePageOverride = null;

            // --- Binary placeholder ---
            Open(nul);
            var bin = Active();
            Check(bin.IsBinary && bin.Path == nul && w.OwnedWindows.Count == 0, "A binary opens a placeholder tab (no error dialog)");
            Check(placeholder.IsVisible && !editor.IsVisible && !image.IsVisible && !md.IsVisible, "Only the placeholder shows");
            var texts = placeholder.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Check(texts.Contains("This file is not displayed because it is binary or uses an unsupported encoding.") && texts.Contains("zero.dat"), "Placeholder text and file name");
            var openDefault = placeholder.FindControl<Button>("OpenDefaultButton")!; var openText = placeholder.FindControl<Button>("OpenAsTextButton")!;
            Check(openDefault.Content as string == "Open with default app" && openText.Content as string == "Open anyway as text", "Two buttons: Open with default app / Open anyway as text");
            var xaml = File.ReadAllText(IOPath.Combine(Repo(), "Components", "BinaryPlaceholder.axaml"));
            Check(!System.Text.RegularExpressions.Regex.IsMatch(xaml, "#[0-9A-Fa-f]{6}") && xaml.Contains("{DynamicResource EditorBackground}") && xaml.Contains("{DynamicResource TextSecondary}"), "Placeholder uses theme resources only");
            Check(S("FileKindLabel").Text == "Binary" && S("EncodingLabel").Text == "" && !bar.FindControl<Button>("TogglePreviewButton")!.IsEnabled, "Status bar says Binary; no encoding; no preview");
            ThemeService.Apply(AppTheme.Light, persist: false); Pump();
            var lightBg = ((Avalonia.Media.ISolidColorBrush)((Border)placeholder.GetVisualDescendants().OfType<Border>().First()).Background!).Color;
            ThemeService.Apply(AppTheme.Dark, persist: false); Pump();
            var darkBg = ((Avalonia.Media.ISolidColorBrush)((Border)placeholder.GetVisualDescendants().OfType<Border>().First()).Background!).Color;
            Check(lightBg == ThemeService.Lookup("EditorBackground", AppTheme.Light) && darkBg == ThemeService.Lookup("EditorBackground", AppTheme.Dark), "Placeholder follows the theme");

            openDefault.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
            Check(launched.SequenceEqual([nul]) && confirms.Count == 0, "Open with default app launches through the launcher (no confirm for data files)");

            openText.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump(); Pump();
            var asText = Active();
            Check(!asText.IsBinary && asText.Path == nul && asText.Text == "ab\0c" && !_documentsContains(v, bin) && editor.IsVisible, "Open anyway as text replaces the placeholder in place");
            Check(!asText.IsReadOnlyText && !asText.Editor.IsReadOnly, "Exactly-decoded text stays editable");
            var lossy = F("lossy.bin", [0, 0x92, 0x81, 0xFF, 0xFE]);
            Open(lossy);
            placeholder.FindControl<Button>("OpenAsTextButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump(); Pump();
            Check(Active().Path == lossy && Active().IsReadOnlyText && Active().Editor.IsReadOnly && S("EncodingLabel").Text.EndsWith(Loc.T("ReadOnly")), "Lossy decoding opens read-only so saving cannot corrupt the file");
            var before = File.ReadAllBytes(lossy);
            Wait((Task)typeof(MainView).GetMethod("SaveAsync", Flags)!.Invoke(v, [Active(), false])!);
            Check(File.ReadAllBytes(lossy).SequenceEqual(before), "Saving a read-only text tab leaves the bytes untouched");

            // --- Executables ---
            var exe = F("tool.exe", [0x4D, 0x5A, 0, 0]);
            var bat = F("run.bat", System.Text.Encoding.ASCII.GetBytes("@echo hi\r\n"));
            launched.Clear(); confirms.Clear(); answer = false;
            Wait((Task)typeof(MainView).GetMethod("OpenWithDefaultAppAsync", Flags | BindingFlags.Public)!.Invoke(v, [exe])!);
            Check(launched.Count == 0 && confirms.Count == 1 && confirms[0].Message.Contains("tool.exe"), "An .exe asks first; declining launches nothing");
            answer = true;
            Wait((Task)typeof(MainView).GetMethod("OpenWithDefaultAppAsync", Flags | BindingFlags.Public)!.Invoke(v, [bat])!);
            Check(launched.SequenceEqual([bat]) && confirms.Count == 2, "A .bat asks first; confirming launches it");
            Check(new[] { ".exe", ".bat", ".cmd", ".ps1", ".app", ".sh" }.All(e => DefaultApp.IsExecutable("x" + e)) && !DefaultApp.IsExecutable("x.pdf"), "Executable list: .exe .bat .cmd .ps1 .app .sh");

            // --- Large files ---
            var big = IOPath.Combine(root, "big.log");
            using (var fs = File.Create(big)) { fs.SetLength(FileSupport.LargeTextBytes + 1); }
            File.WriteAllBytes(IOPath.Combine(root, "x"), []);
            confirms.Clear(); answer = false; var count = tabs.ItemCount;
            Open(big);
            Check(confirms.Count == 1 && confirms[0].Message == "This file is large and may be slow. Open anyway?" && tabs.ItemCount == count, "A file over 50 MB asks first; declining opens nothing");
            var small = F("small.log", System.Text.Encoding.ASCII.GetBytes("ok"));
            confirms.Clear(); Open(small);
            Check(confirms.Count == 0 && Active().Path == small, "Small files open without asking");

            // --- Explorer ---
            Wait((Task)typeof(MainView).GetMethod("LoadRootAsync", Flags)!.Invoke(v, [root])!);
            var tree = w.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "FolderTree");
            TreeViewItem Item(string p) => tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is FolderEntry e && e.Path == p);
            foreach (var p in new[] { nul, log, png })
            {
                var menu = Item(p).ContextMenu!;
                Check(menu.Items.OfType<MenuItem>().First().Name == "OpenDefaultMenuItem" && menu.Items.OfType<MenuItem>().First().Header as string == "Open with default app", $"{IOPath.GetFileName(p)}: context menu has 'Open with default app'");
            }
            launched.Clear();
            Item(log).ContextMenu!.Items.OfType<MenuItem>().First().RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent)); Pump();
            Check(launched.SequenceEqual([log]), "The context menu item launches the file");
            var many = Item(manyControls);
            tree.SelectedItem = many; Pump();
            Check(Active() is { IsBinary: true } d && d.Path == manyControls && launched.Count == 1, "Single click on a binary shows the placeholder (no launch)");
            var dir = Directory.CreateDirectory(IOPath.Combine(root, "sub")).FullName;
            Wait((Task)typeof(MainView).GetMethod("LoadRootAsync", Flags)!.Invoke(v, [root])!);
            tree = w.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "FolderTree");
            Check(!Item(dir).ContextMenu!.Items.OfType<MenuItem>().Any(m => m.Name == "OpenDefaultMenuItem"), "Folders have no 'Open with default app'");
            var explorer = w.GetVisualDescendants().OfType<FolderExplorer>().Single();
            var row = Item(manyControls);
            var header = row.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_Header") ?? row;
            var center = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), w)!.Value;
            launched.Clear();
            w.MouseDown(center, MouseButton.Left); w.MouseUp(center, MouseButton.Left); Pump();
            w.MouseDown(center, MouseButton.Left); w.MouseUp(center, MouseButton.Left); Pump();
            Check(launched.SequenceEqual([manyControls]), "Double-click on a binary launches the default app");
            launched.Clear();
            var textRow = Item(small); var th = textRow.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_Header");
            var tc = th.TranslatePoint(new Point(th.Bounds.Width / 2, th.Bounds.Height / 2), w)!.Value;
            w.MouseDown(tc, MouseButton.Left); w.MouseUp(tc, MouseButton.Left); Pump();
            w.MouseDown(tc, MouseButton.Left); w.MouseUp(tc, MouseButton.Left); Pump();
            Check(launched.Count == 0 && Active().Path == small, "Double-click on a text file still opens it in mdhub");

            // --- Localization ---
            string[] keys = ["BinaryNotDisplayed", "OpenWithDefaultApp", "OpenAnywayAsText", "LargeFileConfirm", "LargeFileTitle", "OpenAnyway", "RunProgramTitle", "RunProgramConfirm", "RunProgram", "KindBinary", "ReadOnly", "OpenedWithDefaultApp", "UnableToOpenDefaultApp", "OpenedReadOnly"];
            foreach (var code in Loc.Languages.Select(l => l.Code).Where(c => c != Loc.English))
            {
                var table = Loc.Table(code);
                Check(keys.All(k => table.TryGetValue(k, out var s) && !string.IsNullOrWhiteSpace(s) && s != Loc.Table("en")[k]), $"{code}: all item 52 strings are translated");
                Loc.Set(code); Open(nul); Pump();
                Check(placeholder.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == table["BinaryNotDisplayed"]) && (string?)placeholder.FindControl<Button>("OpenDefaultButton")!.Content == table["OpenWithDefaultApp"], $"{code}: the placeholder is localized");
            }
            Console.WriteLine($"Binary file tests: {passed} passed");
        }
        finally
        {
            DefaultApp.LauncherOverride = null;
            TextFileStore.AnsiCodePageOverride = null;
            Loc.Set(startLanguage);
            w.Close();
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static bool _documentsContains(MainView v, NoteDocument d) =>
        ((System.Collections.ObjectModel.ObservableCollection<NoteDocument>)typeof(MainView).GetField("_documents", Flags)!.GetValue(v)!).Contains(d);

    static string Repo([System.Runtime.CompilerServices.CallerFilePath] string path = "") => IOPath.GetFullPath(IOPath.Combine(IOPath.GetDirectoryName(path)!, "..", ".."));
}