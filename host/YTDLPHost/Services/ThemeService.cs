using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Switches the whole app between a dark and a light palette at runtime.
    ///
    /// Palette: "Deep ink" (dark) and "Mist" (light), chosen with the owner (dark must read properly dark; light must not be a flat white sheet).
    ///  Fluent/Windows principles guide roles, not a conformance claim:
    ///  - Barely cool, opaque neutrals: near-black ink with lifted cards in dark, a cool gray canvas with soft off-white cards in light.
    ///    Depth comes from surface steps, not shadows or glows.
    ///  - One accent color, used for the live state (progress, links, the primary button); status colors only ever mean status.
    ///  - Dark uses lighter accents; light uses deeper ones. Relevant contrast pairs/states have regression checks.
    ///  - Solid pressed-state brushes keep labels opaque; disabled controls retain their existing treatment.
    ///
    /// Every theme color is a brush resource in App.xaml. Mutable brushes update in place; frozen brushes are replaced.
    /// Theme brush consumers use DynamicResource so replacement also updates existing controls.
    /// </summary>
    public static class ThemeService
    {
        public static ThemeMode Mode { get; private set; } = ThemeMode.Dark;
        public static bool IsDark { get; private set; } = true;

        /// <summary>Raised on the UI thread after the palette changed (the window uses it to recolor the title bar).</summary>
        public static event Action? Changed;

        // The colours are defined once, in Core (ThemePalette), and shared with the Avalonia app.
        private static IReadOnlyDictionary<string, string> DarkPalette => ThemePalette.Dark;
        private static IReadOnlyDictionary<string, string> LightPalette => ThemePalette.Light;

        public static ThemeMode Parse(string? text) => ThemePalette.Parse(text);

        /// <summary>Applies the saved choice at startup and, for "match Windows", follows Windows while the app runs.</summary>
        public static void Initialize(string? saved)
        {
            Mode = Parse(saved);
            Apply();

            SystemEvents.UserPreferenceChanged += (s, e) =>
            {
                if (Mode != ThemeMode.System) return;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(Apply));
            };
        }

        public static void SetMode(ThemeMode mode)
        {
            Mode = mode;
            Apply();
        }

        /// <summary>Hex value of a palette entry in the active theme (used for the native title bar).</summary>
        public static string Hex(string key) => (IsDark ? DarkPalette : LightPalette)[key];

        private static bool SystemPrefersDark()
        {
            try
            {
                // 0 = apps use the dark theme, 1 = light (also the value Windows assumes when the setting is missing).
                var value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return value is int light && light == 0;
            }
            catch
            {
                return true;
            }
        }

        private static void Apply()
        {
            IsDark = Mode switch
            {
                ThemeMode.Dark => true,
                ThemeMode.Light => false,
                _ => SystemPrefersDark()
            };

            var app = System.Windows.Application.Current;
            if (app == null) return;

            foreach (var entry in IsDark ? DarkPalette : LightPalette)
            {
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(entry.Value);
                if (app.Resources[entry.Key] is System.Windows.Media.SolidColorBrush brush && !brush.IsFrozen)
                {
                    brush.Color = color; // same brush instance, so every control that uses it updates at once
                }
                else
                {
                    app.Resources[entry.Key] = new System.Windows.Media.SolidColorBrush(color);
                }
            }

            // The progress bar (moving highlight and tonal fill) reads these accent colors as dynamic resources.
            var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Hex("AccentBrush"));
            app.Resources["AccentColor"] = accent;
            app.Resources["AccentClearColor"] = System.Windows.Media.Color.FromArgb(0, accent.R, accent.G, accent.B);

            // Lighter tint of the same accent for the end of the progress fill (30 % toward white in dark, 14 % in light, so the bar keeps >= 3:1 on its track).
            double lift = IsDark ? 0.30 : 0.14;
            app.Resources["AccentLiftColor"] = System.Windows.Media.Color.FromRgb(
                (byte)Math.Round(accent.R + (255 - accent.R) * lift),
                (byte)Math.Round(accent.G + (255 - accent.G) * lift),
                (byte)Math.Round(accent.B + (255 - accent.B) * lift));

            Changed?.Invoke();
        }
    }
}
