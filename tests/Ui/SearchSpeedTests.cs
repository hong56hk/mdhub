using Mdhub.Services;
using Mdhub.Text;

// Item 46: headless checks for the parallel, streaming, .gitignore-aware workspace search.
internal static class SearchSpeedTests
{
    public static void Run()
    {
        var ignore = GitIgnore.FromLines(@"C:\r", ["# comment", "bin/", "*.log", "!keep.log", "/top.txt", "docs/**/gen", "a?c.md", "[xy].md"]);
        Check(ignore.IsIgnored(@"C:\r\sub\bin", true) && !ignore.IsIgnored(@"C:\r\sub\bin", false), "dir-only pattern matches folders at any depth only");
        Check(ignore.IsIgnored(@"C:\r\x\a.log", false) && !ignore.IsIgnored(@"C:\r\x\keep.log", false), "glob and negation");
        Check(ignore.IsIgnored(@"C:\r\top.txt", false) && !ignore.IsIgnored(@"C:\r\sub\top.txt", false), "leading slash anchors to the .gitignore folder");
        Check(ignore.IsIgnored(@"C:\r\docs\gen", true) && ignore.IsIgnored(@"C:\r\docs\a\b\gen", true), "** matches zero or more folders");
        Check(ignore.IsIgnored(@"C:\r\abc.md", false) && !ignore.IsIgnored(@"C:\r\abbc.md", false) && ignore.IsIgnored(@"C:\r\y.md", false) && !ignore.IsIgnored(@"C:\r\z.md", false), "? and [..] classes");
        Check(!ignore.IsIgnored(@"C:\other\x.log", false), "rules do not apply outside their folder");
        Check(WorkspaceSearch.LooksBinary([0x41, 0, 0x42]) && !WorkspaceSearch.LooksBinary([0xFF, 0xFE, 0x41, 0]) && !WorkspaceSearch.LooksBinary("text"u8), "NUL sniff flags binary but not UTF-16 with a BOM");

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-speed-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, ".gitignore"), "out/\n*.tmpl\n");
            var outDir = Directory.CreateDirectory(Path.Combine(root, "out")).FullName;
            File.WriteAllText(Path.Combine(outDir, "x.md"), "needle");
            File.WriteAllText(Path.Combine(root, "skip.tmpl"), "needle");
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
            File.WriteAllText(Path.Combine(nested, ".gitignore"), "local.md\n!../skip.tmpl\n");
            File.WriteAllText(Path.Combine(nested, "local.md"), "needle");
            File.WriteAllBytes(Path.Combine(root, "blob.dat"), [.. "needle"u8, 0, 1, 2]);
            for (var i = 0; i < 300; i++) File.WriteAllText(Path.Combine(root, $"f{i:D3}.md"), $"line\nneedle {i}\n");
            var streamed = new System.Collections.Concurrent.ConcurrentBag<string>();
            var options = new FindOptions(false, false, false, false);
            var result = WorkspaceSearch.SearchAsync(root, "needle", options, new Dictionary<string, string>(), CancellationToken.None, f => streamed.Add(f.Path)).GetAwaiter().GetResult();
            Check(result.Files.Count == 300 && result.Count == 300, $"ignored folders, ignored globs, nested .gitignore and binary files are skipped (files={result.Files.Count})");
            Check(streamed.Count == 300 && streamed.Distinct().Count() == 300, "every matching file is streamed exactly once");
            Check(result.Files.Select(f => f.Path).SequenceEqual(result.Files.Select(f => f.Path).Order(StringComparer.OrdinalIgnoreCase)), "final result is sorted by path");
            Check(result.Files.All(f => f.Hits.Single().Line == 2), "line numbers survive parallel matching");
            var open = new Dictionary<string, string> { [Path.Combine(nested, "local.md")] = "needle" };
            var withOpen = WorkspaceSearch.SearchAsync(root, "needle", options, open, CancellationToken.None).GetAwaiter().GetResult();
            Check(withOpen.Files.Count == 301, "an ignored file that is open in an editor is still searched");
            for (var i = 0; i < 30; i++) File.WriteAllText(Path.Combine(root, $"many{i}.txt"), string.Concat(Enumerable.Repeat("needle\n", 200)));
            var limited = WorkspaceSearch.SearchAsync(root, "needle", options, new Dictionary<string, string>(), CancellationToken.None).GetAwaiter().GetResult();
            Check(limited.Limited && limited.Count == WorkspaceSearch.MaxResults, $"parallel workers stop at exactly MaxResults (count={limited.Count})");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            Check(WorkspaceSearch.SearchAsync(root, "needle", options, new Dictionary<string, string>(), cancel.Token).GetAwaiter().GetResult().Cancelled, "cancelled search reports Cancelled");
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}