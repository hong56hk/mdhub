using System.Diagnostics;
using Mdhub.Services;
using Mdhub.Text;

// Item 46: stopwatch harness for global search. Usage: --search-bench [root] [query]
internal static class SearchBench
{
    public static void Run(string[] args)
    {
        var index = Array.IndexOf(args, "--search-bench");
        var root = args.Length > index + 1 ? Path.GetFullPath(args[index + 1]) : Directory.GetCurrentDirectory();
        var query = args.Length > index + 2 ? args[index + 2] : "class";
        var options = new FindOptions(false, false, false, false);
        var buffers = new Dictionary<string, string>();
        WorkspaceSearch.SearchAsync(root, query, options, buffers, CancellationToken.None).GetAwaiter().GetResult(); // warm-up
        var totals = new List<double>();
        WorkspaceSearchResult? result = null;
        for (var run = 0; run < 5; run++)
        {
            var watch = Stopwatch.StartNew();
            result = WorkspaceSearch.SearchAsync(root, query, options, buffers, CancellationToken.None).GetAwaiter().GetResult();
            totals.Add(watch.Elapsed.TotalMilliseconds);
        }
        totals.Sort();
        Console.WriteLine($"BENCH root={root} query={query} files={result!.Files.Count} hits={result.Count} skipped={result.Skipped} median={totals[2]:F1}ms min={totals[0]:F1}ms max={totals[4]:F1}ms");
    }
}
