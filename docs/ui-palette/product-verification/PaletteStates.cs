using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using YTDLPHost;
using YTDLPHost.Services;

// Actual built window/templates, with explicit fixture-only read-only DP overrides.
// These inspect rendered hover/pressed branches, NOT physical pointer interaction.
internal static class PaletteStates
{
    private static readonly List<object> Results = new();
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    internal static void Run(string output)
    {
        Directory.CreateDirectory(output);
        var app = new SafeCompiledApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var queue = (PreviewQueue)typeof(Program).GetMethod("CreateQueue", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        ThemeService.SetMode(ThemeMode.Dark);
        var window = new MainWindow { DataContext = queue };
        typeof(Program).GetMethod("SetFixtureChrome", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { window });
        window.Loaded += (_, _) =>
        {
            var material = typeof(MainWindow).GetField("_windowMaterial", Private)!.GetValue(window)!;
            material.GetType().GetMethod("RestoreSolid", Private)!.Invoke(material, new object[] { "Palette-state bitmap verification" });
            ((IDisposable)material).Dispose();
        };
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                await Task.Delay(250);
                var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                var pressedKey = (DependencyPropertyKey)typeof(ButtonBase).GetField("IsPressedPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                foreach (var theme in new[] { ThemeMode.Dark, ThemeMode.Light })
                {
                    ThemeService.SetMode(theme); await Task.Delay(150);
                    string name = theme.ToString().ToLowerInvariant();
                    var buttons = Walk(window).OfType<Button>().Where(b => b.IsVisible).ToList();
                    var pause = buttons.First(b => AutomationProperties.GetName(b) == "Pause");
                    var resume = buttons.First(b => AutomationProperties.GetName(b) == "Resume");
                    var selected = (RadioButton)window.FindName("AllTab");
                    Capture(window, output, name + "-default-selected.png");
                    Read(resume, theme, "Primary default"); Read(selected, theme, "Selected filter");
                    pause.SetValue(hoverKey, true); await Task.Delay(100);
                    Read(pause, theme, "Secondary injected hover"); Capture(window, output, name + "-secondary-hover.png");
                    pause.SetValue(pressedKey, true); await Task.Delay(100);
                    Read(pause, theme, "Secondary injected pressed"); Capture(window, output, name + "-secondary-pressed.png");
                    pause.SetValue(pressedKey, false); pause.SetValue(hoverKey, false);
                    resume.SetValue(hoverKey, true); await Task.Delay(100);
                    Read(resume, theme, "Primary injected hover"); Capture(window, output, name + "-primary-hover.png");
                    resume.SetValue(pressedKey, true); await Task.Delay(100);
                    Read(resume, theme, "Primary injected pressed"); Capture(window, output, name + "-primary-pressed.png");
                    resume.SetValue(pressedKey, false); resume.SetValue(hoverKey, false);
                    Read(resume, theme, "Primary restored default");
                }
                Results.Add(new { Check = "Synthetic task states unchanged", Statuses = queue.Downloads.Select(d => d.Task.Status.ToString()).ToArray() });
                window.Close(); app.Shutdown();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "states-error.txt"), ex.ToString()); app.Shutdown(1); }
            finally { File.WriteAllText(Path.Combine(output, "state-results.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true })); }
        };
        app.Run(window);
    }

    private static void Capture(MainWindow window, string output, string file)
    {
        typeof(Program).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { window, output, file });
        Results.Add(new { Check = "Native bitmap", File = file, Scope = "Actual built WPF window/templates; fixture-injected hover/pressed state; no pointer/commands" });
    }
    private static void Read(Control control, ThemeMode theme, string state)
    {
        control.UpdateLayout();
        var border = (Border)control.Template.FindName("Bd", control);
        var label = Walk(control).OfType<TextBlock>().First(t => t.IsVisible && !t.FontFamily.ToString().Contains("Icons") && !t.FontFamily.ToString().Contains("MDL2"));
        var fg = ((SolidColorBrush)label.Foreground).Color;
        var bg = ((SolidColorBrush)border.Background).Color;
        Results.Add(new { Check = state, Theme = theme.ToString(), Foreground = fg.ToString(), PaintedBackground = bg.ToString(), control.Opacity, control.IsMouseOver, Pressed = control is ButtonBase b && b.IsPressed, Contrast = Contrast(fg, bg), Scope = "Actual painted template-border and inherited label brushes; solid/opaque, not glyph pixels" });
    }
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte x) { double c = x / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
}
