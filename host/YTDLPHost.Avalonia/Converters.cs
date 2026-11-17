using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using YTDLPHost.Models;

namespace YTDLPHost.UI;

/// <summary>Avalonia counterparts of the WPF converters (host/YTDLPHost/Converters). Same colours, same texts.</summary>
public static class StatusConverters
{
    private static IBrush Rgb(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));

    private static readonly IBrush Gray = Rgb(0x71, 0x71, 0x71);
    private static readonly IBrush Blue = Rgb(0x3E, 0xA6, 0xFF);
    private static readonly IBrush Orange = Rgb(0xFF, 0x98, 0x00);
    private static readonly IBrush Green = Rgb(0x4C, 0xAF, 0x50);
    private static readonly IBrush Red = Rgb(0xF4, 0x43, 0x36);
    private static readonly IBrush LightGray = Rgb(0xAA, 0xAA, 0xAA);

    /// <summary>The small status dot in front of each title.</summary>
    public static readonly IValueConverter DotBrush = new FuncValueConverter<DownloadStatus, IBrush>(status => status switch
    {
        DownloadStatus.Queued => Gray,
        DownloadStatus.Downloading => Blue,
        DownloadStatus.Paused => Orange,
        DownloadStatus.Completed => Green,
        DownloadStatus.Error => Red,
        DownloadStatus.Cancelled => LightGray,
        _ => Gray
    });

    /// <summary>The progress bar fill: green when completed, red on error, accent blue otherwise.</summary>
    public static readonly IValueConverter ProgressBrush = new FuncValueConverter<DownloadStatus, IBrush>(status => status switch
    {
        DownloadStatus.Completed => Green,
        DownloadStatus.Error => Red,
        _ => Blue
    });

    /// <summary>True while a download is running: the status dot blinks then.</summary>
    public static readonly IValueConverter IsDownloading = new FuncValueConverter<DownloadStatus, bool>(status => status == DownloadStatus.Downloading);
}

public static class TextConverters
{
    /// <summary>Status bar, right side.</summary>
    public static readonly IValueConverter ActiveDownloads = new FuncValueConverter<int, string>(count =>
        count == 1 ? "1 active download" : $"{count} active downloads");
}

public static class NumberConverters
{
    /// <summary>A whole number shown in an editable combo box and read back from what was typed; text that is not a number leaves the setting unchanged.</summary>
    public static readonly IValueConverter IntText = new IntTextConverter();
}

public sealed class IntTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int number ? number.ToString(CultureInfo.InvariantCulture) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            ? number
            : BindingOperations.DoNothing;
}
