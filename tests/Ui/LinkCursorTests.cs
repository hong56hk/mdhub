using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Mdhub;
using Mdhub.Components;
using Mdhub.Views;

// Item 107: the Markdown preview shows a hand cursor over rendered links (ordinary, autolink, #page=)
// and keeps the usual I-beam or default cursor over plain text and non-link images.
internal static class LinkCursorTests
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run()
    {
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        void Pump() { using var t = new CancellationTokenSource(100); Dispatcher.UIThread.MainLoop(t.Token); }

        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "mdhub-linkcursor-" + Guid.NewGuid().ToString("N"))).FullName;
        var w = new MainWindow { Width = 1400, Height = 900 };
        try
        {
            using (var bitmap = new RenderTargetBitmap(new PixelSize(80, 80))) bitmap.Save(Path.Combine(root, "pic.png"));
            File.WriteAllText(Path.Combine(root, "note.md"),
                "Hello [docs](https://example.com/docs) and <https://example.org/auto>.\n\nSee [page two](notes.pdf#page=2).\n\nJust plain words.\n\n![photo](pic.png)\n");

            w.Show(); Pump();
            var view = w.GetVisualDescendants().OfType<MainView>().Single();
            var open = (Task)typeof(MainView).GetMethod("OpenPathAsync", Flags)!.Invoke(view, [Path.Combine(root, "note.md")])!;
            for (var i = 0; i < 40 && !open.IsCompleted; i++) Pump();
            Pump(); Pump();

            var md = w.GetVisualDescendants().OfType<MdEditor>().First(e => e.IsEffectivelyVisible);
            for (var i = 0; i < 20 && md.GetVisualDescendants().OfType<Image>().All(img => img.Bounds.Width <= 0); i++) Pump();
            w.UpdateLayout(); Pump();

            Point? AtLabel(string label)
            {
                foreach (var tb in md.GetVisualDescendants().OfType<TextBlock>())
                {
                    if (tb.Inlines is not { Count: > 0 } inlines || tb.TextLayout is null) continue;
                    var text = string.Concat(Flatten(inlines));
                    var at = text.IndexOf(label, StringComparison.Ordinal);
                    if (at < 0) continue;
                    var origin = tb.TranslatePoint(new Point(0, 0), w);
                    if (origin is null) continue;
                    var r = tb.TextLayout.HitTestTextPosition(at + label.Length / 2);
                    return origin.Value + new Point(r.X + 1, r.Y + r.Height / 2);
                }
                return null;
            }
            static IEnumerable<string> Flatten(InlineCollection inlines)
            {
                foreach (var inline in inlines)
                {
                    if (inline is Run run) yield return run.Text ?? "";
                    else if (inline is Span span) foreach (var s in Flatten(span.Inlines)) yield return s;
                    else yield return "";
                }
            }

            var docs = AtLabel("docs");
            var auto = AtLabel("https://example.org/auto");
            var page = AtLabel("page two");
            var plain = AtLabel("Just plain words");
            var image = md.GetVisualDescendants().OfType<Image>().FirstOrDefault(img => img.IsEffectivelyVisible && img.Bounds.Width > 0);
            Check(docs is not null && auto is not null && page is not null && plain is not null && image is not null,
                "the preview rendered a normal link, an autolink, a #page link, plain text and a non-link image");
            var photo = image!.TranslatePoint(new Point(image.Bounds.Width / 2, image.Bounds.Height / 2), w);
            Check(photo is not null, "the non-link image has a hit point");

            // At rest, selectable text still uses the I-beam and the image has no custom cursor.
            var texts = md.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && t.Inlines is { Count: > 0 }).ToList();
            Check(texts.Count > 0 && texts.All(t => t.Cursor?.ToString() == new Cursor(StandardCursorType.Ibeam).ToString()), "selectable preview text starts with the I-beam");
            Check(image.Cursor is null || Kind(image.Cursor) == "Arrow", "a non-link image keeps the default cursor at rest");

            string CursorAt(Point p)
            {
                w.MouseMove(p); Pump();
                var hit = w.InputHitTest(p);
                for (Visual? v = hit as Visual; v is not null; v = v.GetVisualParent())
                    if (v is InputElement ie && ie.Cursor is { } c) return Kind(c);
                return "Arrow";
            }

            Check(CursorAt(docs!.Value) == "Hand", $"a normal link shows the hand cursor (got {CursorAt(docs.Value)}, hit={Describe(w.InputHitTest(docs.Value))})");
            Check(CursorAt(auto!.Value) == "Hand", $"an autolink shows the hand cursor (got {CursorAt(auto.Value)}, hit={Describe(w.InputHitTest(auto.Value))})");
            Check(CursorAt(page!.Value) == "Hand", $"a #page link shows the hand cursor (got {CursorAt(page.Value)}, hit={Describe(w.InputHitTest(page.Value))})");
            Check(CursorAt(plain!.Value) == "Ibeam", $"plain text keeps the I-beam (got {CursorAt(plain.Value)}, hit={Describe(w.InputHitTest(plain.Value))})");
            Check(CursorAt(photo!.Value) == "Arrow", $"a non-link image keeps the default cursor (got {CursorAt(photo.Value)}, hit={Describe(w.InputHitTest(photo.Value))})");

            Check(CursorAt(docs.Value) == "Hand" && CursorAt(plain.Value) == "Ibeam", "leaving a link restores the I-beam over plain text");
            Check(CursorAt(docs.Value) == "Hand" && CursorAt(photo.Value) == "Arrow", "leaving a link restores the default cursor over a non-link image");
        }
        finally
        {
            try { w.Close(); Pump(); } catch { }
            try { Directory.Delete(root, true); } catch { }
        }
    }

    static string Describe(IInputElement? hit)
    {
        if (hit is not Control c) return hit?.GetType().Name ?? "null";
        return $"{c.GetType().Name}:{c.Name} {c.Bounds}";
    }

    static string Kind(Cursor? cursor)
    {
        if (cursor is null) return "Arrow";
        object? Probe(object o, int depth)
        {
            if (depth > 3) return null;
            foreach (var f in o.GetType().GetFields(Flags | BindingFlags.Public))
            {
                var v = f.GetValue(o);
                if (v is StandardCursorType s) return s;
                if (v is not null && !f.FieldType.IsPrimitive && f.FieldType != typeof(string) && Probe(v, depth + 1) is { } inner) return inner;
            }
            return null;
        }
        return Probe(cursor, 0)?.ToString() ?? cursor.ToString() ?? "Arrow";
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}