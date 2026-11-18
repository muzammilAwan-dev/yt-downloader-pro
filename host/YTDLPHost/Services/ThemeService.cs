using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace YTDLPHost.Services
{
    public enum ThemeMode { Dark, Light, System }

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

        private static readonly Dictionary<string, string> DarkPalette = new()
        {
            ["BackgroundBrush"] = "#0E0F11",
            ["SurfaceBrush"] = "#16181B",
            ["SurfaceHoverBrush"] = "#1F2227",
            ["SurfacePressedBrush"] = "#2C3138",
            ["BorderBrush"] = "#262A31",
            ["BorderStrongBrush"] = "#424954",
            ["CardContourBrush"] = "#0AFFFFFF",
            ["HairlineBrush"] = "#14FFFFFF",
            ["TrackBrush"] = "#0A0B0D",
            ["LogBackgroundBrush"] = "#0B0C0E",

            ["TextPrimaryBrush"] = "#EEF0F3",
            ["TextSecondaryBrush"] = "#B4BAC5",
            ["TextMutedBrush"] = "#8D95A3",

            ["AccentBrush"] = "#72A5F6",
            ["AccentHoverBrush"] = "#8DB6F9",
            ["AccentPressedBrush"] = "#5E92E4",
            ["OnAccentBrush"] = "#0D1829",
            ["AccentSubtleBrush"] = "#2672A5F6",
            ["AccentSubtleHoverBrush"] = "#3A72A5F6",
            ["AccentBorderBrush"] = "#5972A5F6",

            ["SuccessBrush"] = "#86C89A",
            ["SuccessSubtleBrush"] = "#2686C89A",
            ["WarningBrush"] = "#E6BA6A",
            ["WarningSubtleBrush"] = "#26E6BA6A",
            ["ErrorBrush"] = "#F4939C",
            ["ErrorSubtleBrush"] = "#26F4939C",
            ["ErrorSoftBrush"] = "#14F4939C",
            ["ErrorBorderBrush"] = "#4DF4939C",
        };

        private static readonly Dictionary<string, string> LightPalette = new()
        {
            ["BackgroundBrush"] = "#E3E6EB",
            ["SurfaceBrush"] = "#F5F6F8",
            ["SurfaceHoverBrush"] = "#E9ECF0",
            ["SurfacePressedBrush"] = "#D9DEE5",
            ["BorderBrush"] = "#CBD1DA",
            ["BorderStrongBrush"] = "#A5AEBB",
            ["CardContourBrush"] = "#08000000",
            ["HairlineBrush"] = "#0F000000",
            ["TrackBrush"] = "#D3D8E0",
            ["LogBackgroundBrush"] = "#EDEFF2",

            ["TextPrimaryBrush"] = "#181C22",
            ["TextSecondaryBrush"] = "#3E4754",
            ["TextMutedBrush"] = "#515B69",

            ["AccentBrush"] = "#1D58B0",
            ["AccentHoverBrush"] = "#174A95",
            ["AccentPressedBrush"] = "#123C7C",
            ["OnAccentBrush"] = "#FFFFFF",
            ["AccentSubtleBrush"] = "#1F1D58B0",
            ["AccentSubtleHoverBrush"] = "#331D58B0",
            ["AccentBorderBrush"] = "#591D58B0",

            ["SuccessBrush"] = "#1F6B3E",
            ["SuccessSubtleBrush"] = "#1F1F6B3E",
            ["WarningBrush"] = "#855400",
            ["WarningSubtleBrush"] = "#1F855400",
            ["ErrorBrush"] = "#A02A36",
            ["ErrorSubtleBrush"] = "#1FA02A36",
            ["ErrorSoftBrush"] = "#0FA02A36",
            ["ErrorBorderBrush"] = "#4DA02A36",
        };

        public static ThemeMode Parse(string? text) => text?.Trim().ToLowerInvariant() switch
        {
            "light" => ThemeMode.Light,
            "system" => ThemeMode.System,
            _ => ThemeMode.Dark
        };

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
