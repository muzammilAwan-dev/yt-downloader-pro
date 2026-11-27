using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>
/// Puts the colours of <see cref="ThemePalette"/> (the one list both UIs share) into the application as Dark and Light theme dictionaries.
/// The styles use {DynamicResource ...Brush}, so changing the requested theme variant recolours the open window at once.
/// </summary>
public static class ThemeResources
{
    public static void Install(Application app)
    {
        app.Resources.ThemeDictionaries[ThemeVariant.Dark] = Build(ThemePalette.Dark);
        app.Resources.ThemeDictionaries[ThemeVariant.Light] = Build(ThemePalette.Light);
    }

    /// <summary>"System" is Avalonia's default variant, which follows the operating system's app mode.</summary>
    public static ThemeVariant ToVariant(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => ThemeVariant.Light,
        ThemeMode.System => ThemeVariant.Default,
        _ => ThemeVariant.Dark
    };

    private static ResourceDictionary Build(IReadOnlyDictionary<string, string> palette)
    {
        var dictionary = new ResourceDictionary();
        foreach (var (key, hex) in palette) dictionary[key] = new SolidColorBrush(Color.Parse(hex));

        // Derived from the palette, so both themes stay in step with their accent: the progress fill runs from the accent to its lighter hover tone.
        var from = Color.Parse(palette["AccentBrush"]);
        var to = Color.Parse(palette["AccentHoverBrush"]);
        dictionary["ProgressFillBrush"] = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) }
        };

        // Dark surfaces show depth by being lighter, so they need no shadow; on a light canvas a soft one lifts the cards.
        dictionary["CardShadow"] = ReferenceEquals(palette, ThemePalette.Light) ? BoxShadows.Parse("0 1 3 0 #140F172A, 0 1 1 0 #0A0F172A") : default(BoxShadows);
        dictionary["ToastShadow"] = BoxShadows.Parse(ReferenceEquals(palette, ThemePalette.Light) ? "0 6 20 0 #2E0F172A" : "0 6 20 0 #66000000");
        return dictionary;
    }
}
