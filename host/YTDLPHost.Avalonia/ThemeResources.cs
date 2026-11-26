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
        return dictionary;
    }
}
