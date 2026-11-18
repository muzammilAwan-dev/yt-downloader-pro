using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace YTDLPHost.Services
{
    public enum ThemeMode { Dark, Light, System }

    /// <summary>
    /// Switches the whole app between a dark and a light palette at runtime.
    ///
    /// Design rules behind the two palettes (Material dark-theme guidance, Fluent 2 and WCAG):
    ///  - Neutral grays, never pure black or pure white; depth comes from slightly lighter surfaces, not from shadows or glows.
    ///  - One accent color, used for the live state (progress, links, the primary button); status colors only ever mean status.
    ///  - Dark theme uses lighter, softer accents; light theme uses deeper ones. Every text color is at least 4.5:1 on every surface.
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
            ["BackgroundBrush"] = "#181818",
            ["SurfaceBrush"] = "#212121",
            ["SurfaceHoverBrush"] = "#2A2A2A",
            ["BorderBrush"] = "#333333",
            ["BorderStrongBrush"] = "#474747",
            ["CardContourBrush"] = "#0AFFFFFF",
            ["HairlineBrush"] = "#14FFFFFF",
            ["TrackBrush"] = "#151515",
            ["LogBackgroundBrush"] = "#141414",

            ["TextPrimaryBrush"] = "#F5F5F5",
            ["TextSecondaryBrush"] = "#B3B3B3",
            ["TextMutedBrush"] = "#919191",

            ["AccentBrush"] = "#4EA1F3",
            ["AccentHoverBrush"] = "#6BB2F6",
            ["OnAccentBrush"] = "#0B1A2B",
            ["AccentSubtleBrush"] = "#264EA1F3",
            ["AccentSubtleHoverBrush"] = "#3A4EA1F3",
            ["AccentBorderBrush"] = "#594EA1F3",

            ["SuccessBrush"] = "#6CCB5F",
            ["SuccessSubtleBrush"] = "#266CCB5F",
            ["WarningBrush"] = "#F0B44C",
            ["WarningSubtleBrush"] = "#26F0B44C",
            ["ErrorBrush"] = "#FF7B86",
            ["ErrorSubtleBrush"] = "#26FF7B86",
            ["ErrorSoftBrush"] = "#14FF7B86",
            ["ErrorBorderBrush"] = "#4DFF7B86",
        };

        private static readonly Dictionary<string, string> LightPalette = new()
        {
            ["BackgroundBrush"] = "#F3F3F3",
            ["SurfaceBrush"] = "#FFFFFF",
            ["SurfaceHoverBrush"] = "#F0F0F0",
            ["BorderBrush"] = "#E1E1E1",
            ["BorderStrongBrush"] = "#C8C8C8",
            ["CardContourBrush"] = "#08000000",
            ["HairlineBrush"] = "#0F000000",
            ["TrackBrush"] = "#E4E4E4",
            ["LogBackgroundBrush"] = "#F7F7F7",

            ["TextPrimaryBrush"] = "#1A1A1A",
            ["TextSecondaryBrush"] = "#555555",
            ["TextMutedBrush"] = "#6B6B6B",

            ["AccentBrush"] = "#0F6CBD",
            ["AccentHoverBrush"] = "#115EA3",
            ["OnAccentBrush"] = "#FFFFFF",
            ["AccentSubtleBrush"] = "#1F0F6CBD",
            ["AccentSubtleHoverBrush"] = "#330F6CBD",
            ["AccentBorderBrush"] = "#590F6CBD",

            ["SuccessBrush"] = "#107C10",
            ["SuccessSubtleBrush"] = "#1F107C10",
            ["WarningBrush"] = "#9D5D00",
            ["WarningSubtleBrush"] = "#1F9D5D00",
            ["ErrorBrush"] = "#C42B1C",
            ["ErrorSubtleBrush"] = "#1FC42B1C",
            ["ErrorSoftBrush"] = "#0FC42B1C",
            ["ErrorBorderBrush"] = "#4DC42B1C",
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
