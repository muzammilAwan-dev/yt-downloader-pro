using System;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Sizes the main window so it is always entirely on the screen's work area (the screen minus the taskbar), whatever the display and its
    /// scaling. Microsoft's window guidance: a resizable window may be designed for a bigger screen but must be sized down at display time
    /// to fit. Pure arithmetic, so WPF and Avalonia share it and it can be tested. Sizes are device-independent units (DIPs).
    /// </summary>
    public static class WindowFit
    {
        /// <summary>Title bar, borders and a little air: what a window takes around its client area.</summary>
        public const double FrameAllowance = 48;

        /// <summary>A window never takes more than this share of the work area, so it never looks "full screen but not maximised".</summary>
        public const double MaxShare = 0.92;

        public readonly record struct Fit(double Width, double Height, double MinWidth, double MinHeight);

        /// <param name="desiredWidth">The size the design is made for (client area).</param>
        /// <param name="workWidthPixels">Work area in physical pixels.</param>
        /// <param name="scale">Display scaling, e.g. 1.25 for 125 %.</param>
        public static Fit Compute(double desiredWidth, double desiredHeight, double minWidth, double minHeight,
                                  double workWidthPixels, double workHeightPixels, double scale)
        {
            if (scale <= 0) scale = 1;
            double workW = workWidthPixels / scale, workH = workHeightPixels / scale;

            double maxW = Math.Max(1, Math.Min(workW * MaxShare, workW - FrameAllowance / 2));
            double maxH = Math.Max(1, Math.Min(workH * MaxShare, workH - FrameAllowance));

            double width = Math.Min(desiredWidth, maxW);
            double height = Math.Min(desiredHeight, maxH);

            // On a very small screen the usual minimum would itself be too big: the window may shrink below it rather than leave the screen.
            return new Fit(width, height, Math.Min(minWidth, width), Math.Min(minHeight, height));
        }
    }
}
