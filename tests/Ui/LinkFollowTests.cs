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

// Item 111: follow ordinary Markdown links (paths, folders, http/mailto, anchors, blocked executables, missing targets).
internal static class LinkFollowTests
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static void SetStatusDuration(TimeSpan t) => typeof(MainView).GetProperty("TransientStatusDuration", StaticFlags)!.SetValue(null, t);

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var t = new CancellationTokenSource(50); Dispatcher.UIThread.MainLoop(t.Token); }
        void Wait(Task t) { var end = DateTime.UtcNow.AddSeconds(15); while (!t.IsCompleted && DateTime.UtcNow < end) Pump(); t.GetAwaiter().GetResult(); Pump(); Pump(); }
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }

        ResolutionTests(Check);
        UiTests(Check, Pump, Wait);
    }

    static void ResolutionTests(Action<bool, string> Check)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-linkres-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var from = Path.Combine(root, "sub", "note.md");
            Directory.CreateDirectory(Path.GetDirectoryName(from)!);
            File.WriteAllText(from, "# Home\n\nSee [other](../other.md#hello-world).\n");
            var other = Path.Combine(root, "other.md");
            File.WriteAllText(other, "intro\n\n## Hello World\n\nbody\n");
            var spaced = Path.Combine(root, "my file.md");
            File.WriteAllText(spaced, "spaced");
            var pic = Path.Combine(root, "pic.png");
            File.WriteAllBytes(pic, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg=="));
            var folder = Path.Combine(root, "docs");
            Directory.CreateDirectory(folder);
            var pdf = Path.Combine(root, "sub", "report.pdf");
            File.WriteAllBytes(pdf, [0x25, 0x50, 0x44, 0x46]); // existence only; page links use TryParsePageLink
            File.WriteAllText(Path.Combine(root, "run.bat"), "echo hi");
            File.WriteAllText(Path.Combine(root, "script.ps1"), "1");
            File.WriteAllText(Path.Combine(root, "app.exe"), "MZ");
            File.WriteAllText(Path.Combine(root, "page.html"), "<html>");

            Check(PdfNotes.MarkdownHrefAt("[open](<my file.md>)", 1) == "my file.md"
                  && PdfNotes.MarkdownHrefAt("[t](<my file.md> \"title\")", 1) == "my file.md",
                "angle-bracket destinations keep spaces and drop a title");
            Check(PdfNotes.MarkdownHrefAt("See <my file.md> here", 5) == "my file.md",
                "angle-bracket autolinks with spaces are recognised as paths");

            var decoded = MarkdownLinks.Resolve("other%20name.md", Path.Combine(root, "dummy.md"));
            Check(MarkdownLinks.Decode("my%20file.md") == "my file.md" && MarkdownLinks.Decode("a%2Bb") == "a+b",
                "destinations are URL-decoded (%20 and plus-safe)");

            var rel = MarkdownLinks.Resolve("../my%20file.md", from);
            Check(rel.Kind == MarkdownLinkKind.LocalFile && rel.Path is not null && Paths(rel.Path, spaced),
                $"a relative encoded path resolves against the current file folder ({rel.Path})");

            var abs = MarkdownLinks.Resolve(spaced.Replace("\\", "/"), from);
            Check(abs.Kind == MarkdownLinkKind.LocalFile && Paths(abs.Path, spaced), "an absolute local path is accepted");

            var angled = MarkdownLinks.Resolve("<../my file.md>", from);
            Check(angled.Kind == MarkdownLinkKind.LocalFile && Paths(angled.Path, spaced), "angle-bracket path destinations resolve with spaces");

            var http = MarkdownLinks.Resolve("https://example.com/a?b=1#frag", from);
            Check(http.Kind == MarkdownLinkKind.Web && http.Uri == new Uri("https://example.com/a?b=1#frag"), "https routes to the system browser");
            var mail = MarkdownLinks.Resolve("mailto:dev@example.com", from);
            Check(mail.Kind == MarkdownLinkKind.Mail && mail.Uri == new Uri("mailto:dev@example.com"), "mailto routes to the mail app");

            foreach (var name in new[] { "run.bat", "script.ps1", "app.exe", "evil.js", "setup.msi" })
            {
                var blocked = MarkdownLinks.Resolve(name, Path.Combine(root, "note.md"));
                Check(blocked.Kind == MarkdownLinkKind.Blocked, $"{name} is blocked and is not executed");
            }
            var jsScheme = MarkdownLinks.Resolve("javascript:alert(1)", from);
            Check(jsScheme.Kind == MarkdownLinkKind.Blocked, "javascript: and other unsafe schemes are blocked");

            var dir = MarkdownLinks.Resolve("docs", Path.Combine(root, "note.md"));
            Check(dir.Kind == MarkdownLinkKind.LocalFolder && Paths(dir.Path, folder), "a folder destination is classified for explorer reveal");

            var app = MarkdownLinks.Resolve("page.html", Path.Combine(root, "note.md"));
            // .html is a text extension mdhub can open
            Check(app.Kind == MarkdownLinkKind.LocalFile, "extensions mdhub can open are local files, not the OS default app");

            var missing = MarkdownLinks.Resolve("no-such.md", from);
            Check(missing.Kind == MarkdownLinkKind.Missing, "a missing file is reported as missing");

            var heading = MarkdownLinks.Resolve("#hello-world", other);
            Check(heading.Kind == MarkdownLinkKind.Heading && heading.Fragment == "hello-world"
                  && MarkdownLinks.FindHeadingLine(File.ReadAllText(other), "hello-world") == 3,
                "a same-file #anchor finds the heading line");
            var cross = MarkdownLinks.Resolve("../other.md#hello-world", from);
            Check(cross.Kind == MarkdownLinkKind.LocalFile && Paths(cross.Path, other) && cross.Fragment == "hello-world"
                  && MarkdownLinks.FindHeadingLine(File.ReadAllText(other), cross.Fragment) == 3,
                "a cross-file heading fragment is kept on the resolved path");

            var page = MarkdownLinks.Resolve("report.pdf#page=2", from);
            Check(page.Kind == MarkdownLinkKind.LocalFile && page.Page == 2 && Paths(page.Path, pdf),
                "existing PDF #page=N behaviour is preserved");

            var fileUri = MarkdownLinks.Resolve(new Uri(spaced).AbsoluteUri, from, root);
            Check(fileUri.Kind == MarkdownLinkKind.LocalFile && Paths(fileUri.Path, spaced), "file:// inside the workspace is opened");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    static bool Paths(string? a, string b) => a is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    static void UiTests(Action<bool, string> Check, Action Pump, Action<Task> Wait)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-linkfollow-" + Guid.NewGuid().ToString("N"))).FullName;
        var w = new MainWindow { Width = 1400, Height = 900 };
        Uri? launched = null;
        string? defaultApp = null;
        SetStatusDuration(TimeSpan.FromSeconds(30));
        DialogService.LaunchOverride = uri => { launched = uri; return Task.CompletedTask; };
        DefaultApp.LauncherOverride = path => defaultApp = path;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllText(Path.Combine(root, "home.md"),
                "# Home\n\nSee [other](other.md#hello-world) and [here](#home) and [folder](docs) and [missing](no-such.md) and [run](run.bat) and [web](https://example.com/docs) and [mail](mailto:dev@example.com) and [again](other.md).\n");
            File.WriteAllText(Path.Combine(root, "other.md"), "intro\n\n## Hello World\n\nbody\n");
            File.WriteAllText(Path.Combine(root, "run.bat"), "echo hi");

            w.Show(); Pump();
            var v = w.GetVisualDescendants().OfType<MainView>().Single();
            Task Invoke(string m, params object?[] args) => (Task)typeof(MainView).GetMethod(m, Flags)!.Invoke(v, args)!;
            IEnumerable<NoteDocument> AllDocs() => (IEnumerable<NoteDocument>)typeof(MainView).GetProperty("AllDocuments", Flags)!.GetValue(v)!;
            NoteDocument? Active() => (NoteDocument?)typeof(MainView).GetProperty("ActiveDocument", Flags)?.GetValue(v)
                ?? AllDocs().FirstOrDefault(d => d.Path is not null && Path.GetFileName(d.Path) == (v.GetVisualDescendants().OfType<ListBox>().FirstOrDefault(c => c.Name == "Tabs")?.SelectedItem as NoteDocument)?.Path);
            // Active document: use the selected tab in the visible editor.
            NoteDocument? Selected() => w.GetVisualDescendants().OfType<ListBox>().Where(c => c.Name == "Tabs" && c.IsEffectivelyVisible).Select(c => c.SelectedItem).OfType<NoteDocument>().FirstOrDefault();
            string? Status() => w.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "Status")?.Text;

            Wait(Invoke("LoadRootAsync", root));
            Wait(Invoke("OpenPathAsync", Path.Combine(root, "home.md")));
            Pump(); Pump();
            var home = AllDocs().Single(d => Paths(d.Path, Path.Combine(root, "home.md")));

            Wait(Invoke("FollowLinkAsync", "https://example.com/docs", home.Path));
            Check(launched == new Uri("https://example.com/docs"), "https is launched through the mocked browser");
            launched = null;
            Wait(Invoke("FollowLinkAsync", "mailto:dev@example.com", home.Path));
            Check(launched == new Uri("mailto:dev@example.com"), "mailto is launched through the mocked mail handler");

            Wait(Invoke("FollowLinkAsync", "run.bat", home.Path));
            Check(defaultApp is null && Status() == Loc.T("LinkBlocked"), "a blocked executable is skipped and a status message is shown");

            Wait(Invoke("FollowLinkAsync", "no-such.md", home.Path));
            Check(Status() == Loc.T("LinkTargetMissing"), "a missing target shows the localized status message");

            Wait(Invoke("FollowLinkAsync", "docs", home.Path));
            Pump(); Pump();
            var explorer = w.GetVisualDescendants().OfType<FolderExplorer>().Single();
            var selected = explorer.GetVisualDescendants().OfType<Avalonia.Controls.TreeView>().First().SelectedItem as Avalonia.Controls.TreeViewItem;
            Check(selected?.Tag is FolderEntry entry && Paths(entry.Path, Path.Combine(root, "docs")),
                $"a folder link reveals and selects the folder in the explorer ({(selected?.Tag as FolderEntry)?.Path})");

            Wait(Invoke("FollowLinkAsync", "other.md", home.Path));
            Pump(); Pump();
            var other = AllDocs().Single(d => Paths(d.Path, Path.Combine(root, "other.md")));
            Check(Selected() == other, "following a file link opens it in a tab");
            var before = AllDocs().Count();
            Wait(Invoke("FollowLinkAsync", "other.md", home.Path));
            Pump();
            Check(AllDocs().Count() == before && Selected() == other, "following a link to an already-open file focuses its tab");

            Wait(Invoke("OpenPathAsync", Path.Combine(root, "home.md")));
            Pump();
            Wait(Invoke("FollowLinkAsync", "#home", home.Path));
            Pump(); Pump();
            Check(Selected() == home && LineOf(home) == 1, $"a same-file #anchor scrolls to the heading (line {LineOf(home)})");

            Wait(Invoke("FollowLinkAsync", "other.md#hello-world", home.Path));
            Pump(); Pump();
            other = AllDocs().Single(d => Paths(d.Path, Path.Combine(root, "other.md")));
            Check(Selected() == other && LineOf(other) == 3, $"a cross-file heading link opens the file and scrolls to the heading (line {LineOf(other)})");

            foreach (var lang in Loc.Languages)
            {
                var table = Loc.Table(lang.Code);
                Check(table.TryGetValue("LinkTargetMissing", out var missing) && missing.Length > 0
                      && table.TryGetValue("LinkBlocked", out var blocked) && blocked.Length > 0
                      && (lang.Code == Loc.English || (missing != Loc.En["LinkTargetMissing"] && blocked != Loc.En["LinkBlocked"])),
                    $"{lang.Code}: missing-target and blocked messages exist");
            }
        }
        finally
        {
            Loc.Set(Loc.English);
            DialogService.LaunchOverride = null;
            DefaultApp.LauncherOverride = null;
            SetStatusDuration(TimeSpan.FromSeconds(1.5));
            try { w.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static int LineOf(NoteDocument doc)
    {
        var text = doc.Editor.Text ?? "";
        var caret = Math.Clamp(doc.Editor.CaretIndex, 0, text.Length);
        return Mdhub.Text.EditorCommands.LineCount(text[..caret]);
    }
}