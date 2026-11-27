using System.Collections.Generic;

namespace YTDLPHost.Services
{
    public enum ThemeMode { Dark, Light, System }

    /// <summary>
    /// The colors of both themes, in one place for every UI. Palette: "Deep ink" (dark) and "Mist" (light), chosen with the owner
    /// (dark must read properly dark; light must not be a flat white sheet). Keys are the brush names used by the UIs
    /// (BackgroundBrush, SurfaceBrush, AccentBrush ...); values are #RRGGBB or #AARRGGBB.
    ///  - Barely cool, opaque neutrals: near-black ink with lifted cards in dark, a cool gray canvas with soft off-white cards in light.
    ///  - One accent color for the live state (progress, links, the primary button); status colors only ever mean status.
    ///  - Dark uses lighter accents; light uses deeper ones, so text contrast holds in both.
    /// </summary>
    public static class ThemePalette
    {
        public static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
        {
            ["BackgroundBrush"] = "#0E0F11",
            ["SurfaceBrush"] = "#16181B",
            ["SurfaceHoverBrush"] = "#1F2227",
            ["SurfacePressedBrush"] = "#2C3138",
            ["BorderBrush"] = "#262A31",
            ["BorderStrongBrush"] = "#424954",
            ["CardContourBrush"] = "#0AFFFFFF",
            ["HairlineBrush"] = "#14FFFFFF",
            ["TrackBrush"] = "#272B32",
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

        public static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
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

        public static IReadOnlyDictionary<string, string> For(bool isDark) => isDark ? Dark : Light;

        public static ThemeMode Parse(string? text) => text?.Trim().ToLowerInvariant() switch
        {
            "light" => ThemeMode.Light,
            "system" => ThemeMode.System,
            _ => ThemeMode.Dark
        };
    }
}
