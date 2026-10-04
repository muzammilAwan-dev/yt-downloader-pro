using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using YTDLPHost.ViewModels;
using YTDLPHost.Services;

// EXPLICIT ALIASES: Resolves the ambiguous references between WPF and WinForms
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using MessageBox = System.Windows.MessageBox;

namespace YTDLPHost
{
    /// <summary>
    /// Interaction logic for the main application window.
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        /// <summary>
        /// Makes the native title bar match the dark UI (Windows 10 20H1+ / Windows 11). Failures are harmless: older builds just keep the light bar.
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                int on = 1;
                DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
                int caption = 0x0F0F0F;                               // COLORREF 0x00BBGGRR = #0F0F0F, Windows 11 only
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int)); // DWMWA_CAPTION_COLOR
                int text = 0xE6E6E6;
                DwmSetWindowAttribute(hwnd, 36, ref text, sizeof(int));    // DWMWA_TEXT_COLOR
            }
            catch { /* dwmapi unavailable - keep the default title bar */ }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.RequestScrollToItem += OnRequestScrollToItem;
            }
        }

        private void OnRequestScrollToItem(object? sender, DownloadItemViewModel item)
        {
            // The ListBox will automatically show the item due to selection behavior
        }

        /// <summary>
        /// Handles the custom minimize button to hide the application to the system tray.
        /// </summary>
        private void OnMinimizeToTrayClick(object sender, RoutedEventArgs e)
        {
            Hide();
            if (DataContext is MainViewModel vm)
            {
                vm.IsWindowVisible = false;
            }
        }

        /// <summary>
        /// Opens the settings context menu for protocol registration management.
        /// </summary>
        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenu") };
            var itemStyle = (Style)FindResource("DarkMenuItem");

            var registerItem = new MenuItem { Header = "Re-register protocol handler", Style = itemStyle };
            registerItem.Click += (s, args) =>
            {
                ProtocolHandler.Register();
                System.Windows.MessageBox.Show(this, "Protocol handler registered.", "YT Downloader Pro", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            };

            var unregisterItem = new MenuItem { Header = "Unregister protocol handler", Style = itemStyle };
            unregisterItem.Click += (s, args) =>
            {
                ProtocolHandler.Unregister();
                System.Windows.MessageBox.Show(this, "Protocol handler unregistered.", "YT Downloader Pro", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            };

            var exitItem = new MenuItem { Header = "Exit", Style = itemStyle };
            exitItem.Click += (s, args) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.ExitCommand.Execute(null);
                }
            };

            menu.Items.Add(registerItem);
            menu.Items.Add(unregisterItem);
            menu.Items.Add(new Separator { Style = (Style)FindResource("DarkMenuSeparator") });
            menu.Items.Add(exitItem);

            if (sender is Button btn)
            {
                // Right-align the menu under the gear so it never runs off the window's right edge.
                menu.PlacementTarget = btn;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
                menu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => new[]
                {
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(
                        new System.Windows.Point(targetSize.Width - popupSize.Width, targetSize.Height + 4),
                        System.Windows.Controls.Primitives.PopupPrimaryAxis.None)
                };
                menu.IsOpen = true;
            }
        }
    }
}