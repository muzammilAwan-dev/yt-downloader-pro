using System.Reflection;
using System.Xml.Linq;
using YTDLPHost.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

// Token/math regression only. No Application startup, preferences, windows or downloads.
// Actual rendered drawing brushes/interaction states are verified separately.
internal static class PaletteChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var palettes = new[] { "Dark", "Light" }.ToDictionary(name => name, name =>
            (Dictionary<string, string>)typeof(ThemeService).GetField(name + "Palette", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!);
        check(palettes["Dark"].Keys.Order().SequenceEqual(palettes["Light"].Keys.Order()), "Both palettes expose identical semantic keys");
        using var source = typeof(PaletteChecks).Assembly.GetManifestResourceStream("PaletteDefaults.xaml")!;
        var defaults = XDocument.Load(source);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var brushes = defaults.Descendants(wpf + "SolidColorBrush").Where(e => e.Attribute(x + "Key") != null)
            .ToDictionary(e => (string)e.Attribute(x + "Key")!, e => (string)e.Attribute("Color")!);
        check(brushes.Keys.Order().SequenceEqual(palettes["Dark"].Keys.Order()), "Default dictionary and palettes expose identical brush keys");
        foreach (var brush in brushes)
            check(Parse(brush.Value) == Parse(palettes["Dark"][brush.Key]), $"Dark default matches palette: {brush.Key}");
        var colors = defaults.Descendants(wpf + "Color").ToDictionary(e => (string)e.Attribute(x + "Key")!, e => e.Value);
        check(Parse(colors["AccentColor"]) == Parse(palettes["Dark"]["AccentBrush"]), "Sweep accent matches dark default");
        var clear = Parse(colors["AccentClearColor"]);
        var accent = Parse(palettes["Dark"]["AccentBrush"]);
        check(clear.A == 0 && clear.R == accent.R && clear.G == accent.G && clear.B == accent.B, "Sweep clear endpoint retains accent RGB");

        // Inspect the actual source templates: opaque pressed fills, not a faded label.
        foreach (var (styleName, pressedKey) in new[] { ("ActionButtonStyle", "SurfacePressedBrush"), ("AccentButtonStyle", "AccentPressedBrush") })
        {
            var style = defaults.Descendants(wpf + "Style").Single(e => (string?)e.Attribute(x + "Key") == styleName);
            var pressed = style.Descendants(wpf + "Trigger").Single(e => (string?)e.Attribute("Property") == "IsPressed");
            check(pressed.Descendants(wpf + "Setter").Any(e => (string?)e.Attribute("Property") == "Background" &&
                (string?)e.Attribute("TargetName") == "Bd" && (string?)e.Attribute("Value") == "{DynamicResource " + pressedKey + "}"), styleName + " uses shared solid pressed fill");
            check(!pressed.Descendants(wpf + "Setter").Any(e => (string?)e.Attribute("Property") == "Opacity"), styleName + " pressed state does not fade text");
        }

        foreach (var (name, palette) in palettes)
        {
            var textRatios = new List<double>();
            var uiRatios = new List<double>();
            Color C(string key) => Parse(palette[key]);
            void Text(string fg, string bg)
            {
                double ratio = Contrast(C(fg), C(bg)); textRatios.Add(ratio);
                check(ratio >= 4.5, $"{name} {fg}/{bg} ordinary-text benchmark: {ratio:F3}:1");
            }
            void Ui(string fg, string bg)
            {
                double ratio = Contrast(C(fg), C(bg)); uiRatios.Add(ratio);
                check(ratio >= 3, $"{name} {fg}/{bg} focus/progress benchmark: {ratio:F3}:1");
            }
            var translucentKeys = new HashSet<string>
            {
                "CardContourBrush", "HairlineBrush", "AccentSubtleBrush", "AccentSubtleHoverBrush", "AccentBorderBrush",
                "SuccessSubtleBrush", "WarningSubtleBrush", "ErrorSubtleBrush", "ErrorSoftBrush", "ErrorBorderBrush"
            };
            foreach (var color in palette)
            {
                byte alpha = Parse(color.Value).A;
                check(translucentKeys.Contains(color.Key) ? alpha > 0 && alpha < 255 : alpha == 255, name + " semantic opacity: " + color.Key);
            }
            foreach (var fg in new[] { "TextPrimaryBrush", "TextSecondaryBrush", "TextMutedBrush", "AccentBrush", "AccentHoverBrush" })
            foreach (var bg in new[] { "BackgroundBrush", "SurfaceBrush", "SurfaceHoverBrush", "LogBackgroundBrush" }) Text(fg, bg);
            foreach (var fg in new[] { "TextPrimaryBrush", "TextSecondaryBrush" })
            foreach (var bg in new[] { "BorderBrush", "SurfacePressedBrush" }) Text(fg, bg);
            foreach (var fg in new[] { "SuccessBrush", "WarningBrush", "ErrorBrush" })
            foreach (var bg in new[] { "BackgroundBrush", "SurfaceBrush", "SurfaceHoverBrush" }) Text(fg, bg);
            foreach (var bg in new[] { "AccentBrush", "AccentHoverBrush", "AccentPressedBrush" })
            {
                Text("OnAccentBrush", bg);
                Ui(bg, "SurfaceBrush");
            }
            foreach (var bg in new[] { "BackgroundBrush", "SurfaceBrush", "SurfaceHoverBrush", "SurfacePressedBrush" }) Ui("AccentBrush", bg);
            foreach (var bg in new[] { "TrackBrush", "SurfaceBrush" })
            foreach (var fg in new[] { "AccentBrush", "WarningBrush", "SuccessBrush", "ErrorBrush" }) Ui(fg, bg);
            foreach (var baseKey in new[] { "BackgroundBrush", "SurfaceBrush" })
            foreach (var tintKey in new[] { "ErrorSoftBrush", "ErrorSubtleBrush", "AccentSubtleBrush", "AccentSubtleHoverBrush" })
            {
                var composite = Composite(C(tintKey), C(baseKey), 1);
                foreach (var fg in new[] { "TextPrimaryBrush", tintKey.StartsWith("Error") ? "ErrorBrush" : "TextSecondaryBrush" })
                {
                    double ratio = Contrast(C(fg), composite); textRatios.Add(ratio);
                    check(ratio >= 4.5, $"{name} {fg} on composited {tintKey}/{baseKey}: {ratio:F3}:1");
                }
                if (tintKey == "ErrorSoftBrush")
                foreach (var fg in new[] { "AccentBrush", "AccentHoverBrush" })
                {
                    double ratio = Contrast(C(fg), composite); textRatios.Add(ratio);
                    check(ratio >= 4.5, $"{name} recovery link {fg} on composited {tintKey}/{baseKey}: {ratio:F3}:1");
                }
            }
            // Existing icon-template pressed opacity is a meaningful-UI, not ordinary-text sample.
            var dangerBackground = Composite(C("ErrorSubtleBrush"), C("SurfaceBrush"), 1);
            double dangerRatio = Contrast(Composite(C("ErrorBrush"), dangerBackground, .75), dangerBackground);
            check(dangerRatio >= 3, $"{name} pressed destructive icon benchmark: {dangerRatio:F3}:1");
            Console.WriteLine($"Palette {name}: {textRatios.Count} token text pairs min {textRatios.Min():F3}:1; {uiRatios.Count} focus/progress pairs min {uiRatios.Min():F3}:1. Not native conformance.");
        }
    }

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static Color Composite(Color fg, Color bg, double opacity)
    {
        double a = fg.A / 255.0 * opacity;
        return Color.FromRgb((byte)Math.Round(fg.R * a + bg.R * (1 - a)),
            (byte)Math.Round(fg.G * a + bg.G * (1 - a)), (byte)Math.Round(fg.B * a + bg.B * (1 - a)));
    }
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte x) { double c = x / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        double x = Luminance(a), y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}
