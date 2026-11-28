using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using YTDLPHost.Models;

namespace YTDLPHost.Converters
{
    /// <summary>
    /// Maps a download status to its theme brush. Brushes come from App.xaml so the palette lives in one place;
    /// the fallbacks only apply if a resource is missing.
    /// </summary>
    [ValueConversion(typeof(DownloadStatus), typeof(System.Windows.Media.Brush))]
    public class StatusToColorConverter : IValueConverter
    {
        private static System.Windows.Media.Brush Theme(string key, string fallbackHex)
        {
            if (System.Windows.Application.Current?.TryFindResource(key) is System.Windows.Media.Brush brush) return brush;
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallbackHex);
            var fallback = new SolidColorBrush(color);
            fallback.Freeze();
            return fallback;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DownloadStatus status)
            {
                return status switch
                {
                    DownloadStatus.Queued => Theme("TextMutedBrush", "#7F879B"),
                    DownloadStatus.Downloading => Theme("AccentBrush", "#8B7CFF"),
                    DownloadStatus.Paused => Theme("WarningBrush", "#FFB84D"),
                    DownloadStatus.Completed => Theme("SuccessBrush", "#3DDC97"),
                    DownloadStatus.Error => Theme("ErrorBrush", "#FF6B7A"),
                    DownloadStatus.Cancelled => Theme("TextSecondaryBrush", "#A4ABBD"),
                    _ => Theme("TextMutedBrush", "#7F879B")
                };
            }
            return Theme("TextMutedBrush", "#7F879B");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(DownloadStatus), typeof(string))]
    public class StatusToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DownloadStatus status)
            {
                return status switch
                {
                    DownloadStatus.Queued => "In queue",
                    DownloadStatus.Downloading => "Downloading",
                    DownloadStatus.Paused => "Paused",
                    DownloadStatus.Completed => "Completed",
                    DownloadStatus.Error => "Failed",
                    DownloadStatus.Cancelled => "Cancelled",
                    _ => "Unknown"
                };
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(DownloadStatus), typeof(bool))]
    public class StatusToSpinConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is DownloadStatus.Downloading;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
