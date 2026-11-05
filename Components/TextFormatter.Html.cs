using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Mdhub.Localization;

namespace Mdhub.Components;

public partial class TextFormatter
{
    private void BuildHtmlButtons()
    {
        HtmlFormatBar.Children.Clear();
        var divider = new Border
        {
            Name = "HtmlFormatDivider", Width = 1, Height = 20,
            Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center
        };
        divider.Bind(Border.BackgroundProperty, this.GetResourceObservable("TextSecondary"));
        HtmlFormatBar.Children.Add(divider);
        Button Button(string label, string key, string? action)
        {
            var button = new Button { Content = label, Tag = action, Classes = { "tool" } };
            ToolTip.SetTip(button, Loc.T(key)); AutomationProperties.SetName(button, Loc.T(key));
            if (action is not null) button.Click += (_, _) => FormatRequested?.Invoke(action);
            HtmlFormatBar.Children.Add(button);
            return button;
        }
        Button IconButton(string name, string key, string? action, string geometry)
        {
            var button = Button("", key, action);
            button.Classes.Add("icon");
            var icon = new Avalonia.Controls.Shapes.Path
            {
                Name = name, Width = 16, Height = 14, Stretch = Stretch.None,
                StrokeThickness = 1.25, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
                Data = Geometry.Parse(geometry)
            };
            icon.Bind(Avalonia.Controls.Shapes.Shape.StrokeProperty, new Avalonia.Data.Binding("Foreground")
            {
                RelativeSource = new Avalonia.Data.RelativeSource(Avalonia.Data.RelativeSourceMode.FindAncestor)
                { AncestorType = typeof(Avalonia.Controls.Presenters.ContentPresenter) }
            });
            button.Content = icon;
            return button;
        }
        foreach (var alignment in new[] { "left", "center", "right" })
        {
            var key = "HtmlAlign" + char.ToUpperInvariant(alignment[0]) + alignment[1..];
            var shortLine = alignment switch { "center" => "M 4,5 H 12 M 4,11 H 12", "right" => "M 7,5 H 15 M 7,11 H 15", _ => "M 1,5 H 9 M 1,11 H 9" };
            IconButton("HtmlAlign" + alignment + "Icon", key, "html-align-" + alignment, "M 1,2 H 15 M 1,8 H 15 " + shortLine);
        }
        Button("U", "HtmlUnderline", "html-underline");
        Button("x²", "HtmlSuperscript", "html-sup");
        Button("x₂", "HtmlSubscript", "html-sub");
        // IconButton("HtmlLineBreakIcon", "HtmlLineBreak", "html-break", "M 13.5,2 V 8 H 2.5 M 6,4.5 L 2.5,8 L 6,11.5");
        // Button("div", "HtmlContainer", "html-div");
        IconButton("HtmlTableIcon", "HtmlTable", "html-table",
            "M 3,1.5 H 13 A 1.5,1.5 0 0 1 14.5,3 V 11 A 1.5,1.5 0 0 1 13,12.5 H 3 A 1.5,1.5 0 0 1 1.5,11 V 3 A 1.5,1.5 0 0 1 3,1.5 Z M 1.5,5.2 H 14.5 M 1.5,8.8 H 14.5 M 5.8,1.5 V 12.5 M 10.2,1.5 V 12.5");
        var color = IconButton("HtmlTextColorIcon", "HtmlTextColor", null,
            "M 4,9.5 L 8,1.5 L 12,9.5 M 5.5,6.5 H 10.5");
        var colorGlyph = (Control)color.Content!;
        color.Content = null;
        color.Content = new Grid
        {
            Width = 16, Height = 14,
            Children =
            {
                colorGlyph,
                new Border
                {
                    Name = "HtmlTextColorUnderline", Width = 14, Height = 2.5,
                    Background = PaletteBrush("HtmlTextColorDefault"), VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
        color.Flyout = ColorFlyout();
        var image = IconButton("HtmlImageIcon", "HtmlImage", null,
            "M 3,1.5 H 13 A 1.5,1.5 0 0 1 14.5,3 V 11 A 1.5,1.5 0 0 1 13,12.5 H 3 A 1.5,1.5 0 0 1 1.5,11 V 3 A 1.5,1.5 0 0 1 3,1.5 Z M 1.5,10 L 5.5,6 L 8.5,9 M 6.5,11 L 10.5,7 L 14.5,11 M 9.5,4.3 A 1.1,1.1 0 1 0 11.7,4.3 A 1.1,1.1 0 1 0 9.5,4.3 Z");
        image.Flyout = ImageFlyout();
    }

    // The swatch palette lives in Styles/Theme.axaml (HtmlColorSwatch0..31); the hex text is what gets written into the note.
    private static ISolidColorBrush PaletteBrush(string key) => (ISolidColorBrush)Application.Current!.FindResource(key)!;
    private static string Hex(ISolidColorBrush brush) => $"#{brush.Color.R:x2}{brush.Color.G:x2}{brush.Color.B:x2}";

    private Flyout ColorFlyout()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = Loc.T("HtmlTextColor") });
        var input = new TextBox { Name = "HtmlColorValue", Text = Hex(PaletteBrush("HtmlTextColorDefault")), PlaceholderText = "#RRGGBB", Width = 240 };
        var colors = new Avalonia.Controls.Primitives.UniformGrid { Columns = 8, Width = 240 };
        for (var i = 0; Application.Current?.TryFindResource("HtmlColorSwatch" + i, out var res) == true && res is ISolidColorBrush brush; i++)
        {
            var hex = Hex(brush);
            var swatch = new Button { Width = 26, Height = 26, Margin = new Thickness(2), Background = brush };
            AutomationProperties.SetName(swatch, hex);
            ToolTip.SetTip(swatch, hex);
            swatch.Click += (_, _) => input.Text = hex;
            colors.Children.Add(swatch);
        }
        panel.Children.Add(colors); panel.Children.Add(input);
        var apply = new Button { Content = Loc.T("HtmlApply") };
        var flyout = new Flyout { Content = panel };
        apply.Click += (_, _) =>
        {
            if (!Color.TryParse(input.Text, out var chosen)) return;
            flyout.Hide();
            FormatRequested?.Invoke($"html-color:#{chosen.R:X2}{chosen.G:X2}{chosen.B:X2}");
        };
        panel.Children.Add(apply);
        return flyout;
    }

    private Flyout ImageFlyout()
    {
        var panel = new StackPanel { Spacing = 6, Width = 280 };
        var source = new TextBox { Name = "HtmlImageSource", PlaceholderText = Loc.T("HtmlImageSource") };
        var alt = new TextBox { Name = "HtmlImageAlt", PlaceholderText = Loc.T("HtmlImageAlt") };
        var width = new NumericUpDown { Name = "HtmlImageWidth", Minimum = 1, Maximum = 4096, Value = 300, FormatString = "0" };
        panel.Children.Add(new TextBlock { Text = Loc.T("HtmlImageSource") }); panel.Children.Add(source);
        panel.Children.Add(new TextBlock { Text = Loc.T("HtmlImageAlt") }); panel.Children.Add(alt);
        panel.Children.Add(new TextBlock { Text = Loc.T("HtmlImageWidth") }); panel.Children.Add(width);
        AutomationProperties.SetName(source, Loc.T("HtmlImageSource"));
        AutomationProperties.SetName(alt, Loc.T("HtmlImageAlt"));
        AutomationProperties.SetName(width, Loc.T("HtmlImageWidth"));
        var insert = new Button { Content = Loc.T("HtmlApply") };
        var flyout = new Flyout { Content = panel };
        insert.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(source.Text)) return;
            flyout.Hide();
            FormatRequested?.Invoke("html-image:" + System.Text.Json.JsonSerializer.Serialize(new[] { source.Text, alt.Text ?? "", ((int)(width.Value ?? 300)).ToString(System.Globalization.CultureInfo.InvariantCulture) }));
        };
        panel.Children.Add(insert);
        return flyout;
    }
}
