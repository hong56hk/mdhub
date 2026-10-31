using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Models;
using Mdhub.Services;
using Mdhub.Views;

// Item 78: VS Code-style file-type icons (Material Icon Theme glyphs) in the explorer, tabs, Quick Open and global search.
internal static class FileIconTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var passed = 0;
        void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
        void Pump() { using var timeout = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(timeout.Token); }
        void Await(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); }
        bool Until(Func<bool> ok) { var end = DateTime.UtcNow.AddSeconds(8); while (!ok() && DateTime.UtcNow < end) Pump(); return ok(); }

        // Mapping: exact name before extension, case-insensitive, fallback, binary.
        Check(FileIcons.For("README.md") == FileIcons.Readme && FileIcons.For("readme.MD") == FileIcons.Readme && FileIcons.For(@"C:\x\ReadMe.md") == FileIcons.Readme, "README.md matches by name before its .md extension, any case");
        Check(FileIcons.For("notes.md") == FileIcons.Markdown && FileIcons.For("NOTES.MD") == FileIcons.Markdown && FileIcons.For("a.markdown") == FileIcons.Markdown, ".md (any case) gets the Markdown icon");
        Check(FileIcons.For("package.json") == FileIcons.NodeJs && FileIcons.For("other.json") == FileIcons.Json && FileIcons.For("PACKAGE.JSON") == FileIcons.NodeJs, "package.json matches by name; other .json files by extension");
        Check(FileIcons.For("LICENSE") == FileIcons.License && FileIcons.For("license") == FileIcons.License && FileIcons.For(".gitignore") == FileIcons.Git && FileIcons.For("Dockerfile") == FileIcons.Docker && FileIcons.For("dockerfile") == FileIcons.Docker, "LICENSE, .gitignore and Dockerfile match by exact name (case-insensitive)");
        var byExtension = new (string Ext, FileIcon Icon)[]
        {
            ("png", FileIcons.Image), ("jpg", FileIcons.Image), ("jpeg", FileIcons.Image), ("gif", FileIcons.Image), ("webp", FileIcons.Image), ("bmp", FileIcons.Image), ("ico", FileIcons.Image),
            ("svg", FileIcons.Svg), ("pdf", FileIcons.Pdf), ("txt", FileIcons.Text), ("json", FileIcons.Json), ("yaml", FileIcons.Yaml), ("yml", FileIcons.Yaml),
            ("cs", FileIcons.CSharp), ("js", FileIcons.JavaScript), ("ts", FileIcons.TypeScript), ("html", FileIcons.Html), ("css", FileIcons.Css),
            ("zip", FileIcons.Archive), ("7z", FileIcons.Archive), ("rar", FileIcons.Archive), ("tar", FileIcons.Archive), ("gz", FileIcons.Archive),
            ("docx", FileIcons.Word), ("xlsx", FileIcons.Excel), ("pptx", FileIcons.PowerPoint), ("mp3", FileIcons.Audio), ("wav", FileIcons.Audio), ("mp4", FileIcons.Video), ("mov", FileIcons.Video), ("exe", FileIcons.Exe),
        };
        foreach (var (ext, icon) in byExtension) Check(FileIcons.For("file." + ext) == icon && FileIcons.For("FILE." + ext.ToUpperInvariant()) == icon, $".{ext} -> {icon.Id} (case-insensitive)");
        Check(FileIcons.For("a.unknownext") == FileIcons.File && FileIcons.For("Makefile") == FileIcons.File && FileIcons.For(null) == FileIcons.File, "Unknown names fall back to the generic file icon");
        Check(FileIcons.For("blob", isBinary: true) == FileIcons.Binary && FileIcons.For("data.bin", isBinary: true) == FileIcons.Binary && FileIcons.For("data.bin") == FileIcons.File, "A binary file (item 52) with no other match gets the binary icon");
        Check(FileIcons.For("pic.png", isBinary: true) == FileIcons.Image && FileIcons.For("app.exe", isBinary: true) == FileIcons.Exe, "A name or extension match wins over the binary icon");
        Check(FileIcons.All.Select(i => i.Id).Distinct().Count() == FileIcons.All.Count && FileIcons.All.All(i => i.Geometry.Bounds.Width > 2 && i.Geometry.Bounds.Right <= FileIcon.Size + 0.5 && i.Geometry.Bounds.Bottom <= FileIcon.Size + 0.5 && i.Geometry.Bounds.Left >= -0.5 && i.Geometry.Bounds.Top >= -0.5),
            "Every bundled icon is offline geometry inside the 16px icon box: " + string.Join(", ", FileIcons.All.Select(i => $"{i.Id}={i.Geometry.Bounds}")));
        Check(ReferenceEquals(FileIcons.Markdown.Geometry, FileIcons.For("x.md").Geometry), "Icons share one geometry instance");
        var before = FileIcons.CacheCount;
        for (var i = 0; i < 5000; i++) FileIcons.For(Path.Combine("C:/big", $"f{i % 7}.md"));
        Check(FileIcons.CacheCount - before <= 7, $"Lookups are cached per file name for large folders (+{FileIcons.CacheCount - before} entries for 5000 lookups)");
        foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            Check(FileIcons.All.All(i => Application.Current!.TryFindResource(i.BrushKey, ThemeService.Variant(theme), out var b) && b is ISolidColorBrush), $"{theme}: every icon has a theme colour resource");
        Check(FileIcons.All.Any(i => ThemeService.Lookup(i.BrushKey, AppTheme.Dark) != ThemeService.Lookup(i.BrushKey, AppTheme.Light)), "Icon colours have dark and light variants");

        // The same icon in all four places.
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-icons-" + Guid.NewGuid().ToString("N"))).FullName;
        var window = new MainWindow { Width = 1200, Height = 800 }; window.Show(); Pump();
        try
        {
            var view = window.GetVisualDescendants().OfType<MainView>().Single();
            T Find<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            File.WriteAllText(Path.Combine(root, "README.md"), "needle\n");
            File.WriteAllText(Path.Combine(root, "notes.md"), "needle\n");
            File.WriteAllText(Path.Combine(root, "package.json"), "{\"needle\": 1}\n");
            File.WriteAllText(Path.Combine(root, "app.ts"), "// needle\n");
            File.WriteAllBytes(Path.Combine(root, "blob"), [1, 0, 2, 0, 3, 0, 0, 0]);
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            Await((Task)typeof(MainView).GetMethod("LoadRootAsync", Flags)!.Invoke(view, [root])!);
            var tree = Find<TreeView>("FolderTree");
            TreeViewItem Row(string name) => tree.Items.OfType<TreeViewItem>().Single(i => i.Tag is FolderEntry e && Path.GetFileName(e.Path) == name);
            FileTypeIcon? RowIcon(Control c) => c.GetVisualDescendants().OfType<FileTypeIcon>().FirstOrDefault();
            Until(() => RowIcon(Row("README.md")) is not null);
            var expected = new Dictionary<string, FileIcon> { ["README.md"] = FileIcons.Readme, ["notes.md"] = FileIcons.Markdown, ["package.json"] = FileIcons.NodeJs, ["app.ts"] = FileIcons.TypeScript, ["blob"] = FileIcons.Binary };
            foreach (var (name, icon) in expected)
                Check(RowIcon(Row(name)) is { } ri && ReferenceEquals(ri.Data, icon.Geometry) && ri.Fill is ISolidColorBrush f && f.Color == ThemeService.Lookup(icon.BrushKey), $"Explorer: {name} shows the {icon.Id} icon in its theme colour");
            var folder = Row("docs");
            Check(folder.Header is string h && h == "docs" && RowIcon(folder) is null, "Explorer: folders keep a plain name with no folder icon (chevron only)");
            var chevron = folder.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_ExpandCollapseChevron");
            var icn = RowIcon(Row("notes.md"))!;
            Check(Math.Abs(icn.TranslatePoint(default, tree)!.Value.X - chevron.TranslatePoint(default, tree)!.Value.X) < 0.5, "Explorer: the file icon sits in the chevron's slot (item 31 alignment)");

            Await((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(view, [Path.Combine(root, "README.md")])!);
            Await((Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(view, [Path.Combine(root, "app.ts")])!);
            var tabs = Find<ListBox>("Tabs");
            FileTypeIcon? TabIcon(string title) => tabs.GetVisualDescendants().OfType<ListBoxItem>().Where(i => i.DataContext is NoteDocument d && d.Path is { } p && Path.GetFileName(p) == title).Select(RowIcon).FirstOrDefault();
            Until(() => TabIcon("app.ts") is not null);
            Check(TabIcon("README.md") is { } t1 && ReferenceEquals(t1.Data, FileIcons.Readme.Geometry) && TabIcon("app.ts") is { } t2 && ReferenceEquals(t2.Data, FileIcons.TypeScript.Geometry), "Tabs: the same icons as the explorer rows");
            Check(ReferenceEquals(TabIcon("README.md")!.Data, RowIcon(Row("README.md"))!.Data), "Tabs and explorer share the icon resource");

            var qo = window.GetVisualDescendants().OfType<QuickOpen>().Single();
            window.KeyPress(Key.P, RawInputModifiers.Control, PhysicalKey.None, null); window.KeyRelease(Key.P, RawInputModifiers.Control, PhysicalKey.None, null); Pump();
            qo.FindControl<TextBox>("QuickOpenInput")!.Text = "package"; Pump(); Await(qo.Pending);
            var list = qo.FindControl<ListBox>("QuickOpenList")!;
            Until(() => list.Items.OfType<ListBoxItem>().Any());
            var qoItem = list.Items.OfType<ListBoxItem>().First();
            Check(qoItem.Content is Control qc && qc.GetLogicalDescendants().OfType<FileTypeIcon>().FirstOrDefault() is { } qi && ReferenceEquals(qi.Data, FileIcons.NodeJs.Geometry), "Quick Open: package.json shows the same NodeJs icon");
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null); Pump();

            var search = Find<GlobalSearch>("GlobalSearch");
            view.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift }); Pump();
            Find<TextBox>("GlobalQuery").Text = "needle"; Pump(); Await(search.SearchAsync()); Pump();
            var results = Find<TreeView>("GlobalResults");
            FileTypeIcon? GroupIcon(string name) => results.Items.OfType<TreeViewItem>().Where(i => i.Tag is SearchFile f && Path.GetFileName(f.Path) == name).Select(i => (i.Header as Control)?.GetLogicalDescendants().OfType<FileTypeIcon>().FirstOrDefault()).FirstOrDefault();
            foreach (var (name, icon) in expected.Where(e => e.Key != "blob"))
                Check(GroupIcon(name) is { } gi && ReferenceEquals(gi.Data, icon.Geometry), $"Global search: {name} shows the {icon.Id} icon");

            ThemeService.Apply(AppTheme.Light, persist: false); Pump();
            Check(RowIcon(Row("notes.md"))!.Fill is ISolidColorBrush lf && lf.Color == ThemeService.Lookup(FileIcons.Markdown.BrushKey, AppTheme.Light) && TabIcon("README.md")!.Fill is ISolidColorBrush tf && tf.Color == ThemeService.Lookup(FileIcons.Readme.BrushKey, AppTheme.Light),
                "Switching to the light theme recolours the icons live");
            foreach (var d in (IList<NoteDocument>)typeof(MainView).GetField("_documents", Flags)!.GetValue(view)!) d.SavedText = d.Text;
        }
        finally
        {
            ThemeService.Apply(AppTheme.Dark, persist: false);
            window.Close(); Pump();
            try { Directory.Delete(root, true); } catch { }
        }
        Console.WriteLine($"File icon tests passed: {passed}");
    }
}