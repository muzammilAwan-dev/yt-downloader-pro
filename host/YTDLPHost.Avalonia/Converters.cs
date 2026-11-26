using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using YTDLPHost.Native;

namespace YTDLPHost.UI;

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

public static class LinkStateConverters
{
    /// <summary>Dot in front of a browser of the Browser integration panel: green linked, red not linked, orange out of date, grey not installed.</summary>
    public static readonly IValueConverter DotBrush = new FuncValueConverter<BrowserLinkState, IBrush>(state => state switch
    {
        BrowserLinkState.Linked => new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)),
        BrowserLinkState.NotLinked => new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)),
        BrowserLinkState.OutOfDate => new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00)),
        _ => new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x71))
    });
}
