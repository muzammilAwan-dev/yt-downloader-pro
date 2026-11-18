using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace YTDLPHost.Services
{
    public enum ThemeMode { Dark, Light, System }

    /// <summary>
    /// Switches the whole app between a dark and a light palette at runtime.
    ///
    /// Owner-approved graphite/porcelain palette; Fluent/Windows principles guide roles, not a conformance claim:
    ///  - Barely cool, opaque neutrals; depth comes from surface steps, not shadows or glows.
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
            ["BackgroundBrush"] = "#181A1E",
            ["SurfaceBrush"] = "#22252A",
            ["SurfaceHoverBrush"] = "#2C3037",
            ["SurfacePressedBrush"] = "#3B424D",
            ["BorderBrush"] = "#343B45",
            ["BorderStrongBrush"] = "#4D5663",
            ["CardContourBrush"] = "#0AFFFFFF",
            ["HairlineBrush"] = "#14FFFFFF",
            ["TrackBrush"] = "#171A1F",
            ["LogBackgroundBrush"] = "#15181D",

            ["TextPrimaryBrush"] = "#F2F4F7",
            ["TextSecondaryBrush"] = "#B8BEC9",
            ["TextMutedBrush"] = "#9EA6B3",

            ["AccentBrush"] = "#7BAAF7",
            ["AccentHoverBrush"] = "#94BAFA",
            ["AccentPressedBrush"] = "#6D9CE9",
            ["OnAccentBrush"] = "#111C2E",
            ["AccentSubtleBrush"] = "#267BAAF7",
            ["AccentSubtleHoverBrush"] = "#3A7BAAF7",
            ["AccentBorderBrush"] = "#597BAAF7",

            ["SuccessBrush"] = "#86C89A",
            ["SuccessSubtleBrush"] = "#2686C89A",
            ["WarningBrush"] = "#E3B86C",
            ["WarningSubtleBrush"] = "#26E3B86C",
            ["ErrorBrush"] = "#F3949D",
            ["ErrorSubtleBrush"] = "#26F3949D",
            ["ErrorSoftBrush"] = "#14F3949D",
            ["ErrorBorderBrush"] = "#4DF3949D",
        };

        private static readonly Dictionary<string, string> LightPalette = new()
        {
            ["BackgroundBrush"] = "#F2F3F5",
            ["SurfaceBrush"] = "#FCFCFD",
            ["SurfaceHoverBrush"] = "#E9ECF0",
            ["SurfacePressedBrush"] = "#DCE1E8",
            ["BorderBrush"] = "#D9DEE6",
            ["BorderStrongBrush"] = "#B5BDC8",
            ["CardContourBrush"] = "#08000000",
            ["HairlineBrush"] = "#0F000000",
            ["TrackBrush"] = "#DFE3E9",
            ["LogBackgroundBrush"] = "#F5F6F8",

            ["TextPrimaryBrush"] = "#20242A",
            ["TextSecondaryBrush"] = "#545C69",
            ["TextMutedBrush"] = "#5F6976",

            ["AccentBrush"] = "#2463C4",
            ["AccentHoverBrush"] = "#1D53A8",
            ["AccentPressedBrush"] = "#19498F",
            ["OnAccentBrush"] = "#FFFFFF",
            ["AccentSubtleBrush"] = "#1F2463C4",
            ["AccentSubtleHoverBrush"] = "#332463C4",
            ["AccentBorderBrush"] = "#592463C4",

            ["SuccessBrush"] = "#247343",
            ["SuccessSubtleBrush"] = "#1F247343",
            ["WarningBrush"] = "#8B5E18",
            ["WarningSubtleBrush"] = "#1F8B5E18",
            ["ErrorBrush"] = "#AF3541",
            ["ErrorSubtleBrush"] = "#1FAF3541",
            ["ErrorSoftBrush"] = "#0FAF3541",
            ["ErrorBorderBrush"] = "#4DAF3541",
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

            // The moving "working" highlight in the progress bar reads these two as dynamic resources.
            var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(Hex("AccentBrush"));
            app.Resources["AccentColor"] = accent;
            app.Resources["AccentClearColor"] = System.Windows.Media.Color.FromArgb(0, accent.R, accent.G, accent.B);

            Changed?.Invoke();
        }
    }
}
