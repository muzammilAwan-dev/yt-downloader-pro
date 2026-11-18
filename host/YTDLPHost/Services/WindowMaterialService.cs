using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace YTDLPHost.Services
{
    /// <summary>
    /// Optional documented Windows 11 Mica for the native main-window shell only.
    /// Content brushes stay opaque. No layered window, custom frame or download/startup work.
    /// </summary>
    internal sealed class WindowMaterialService : IDisposable
    {
        private const int BackdropAttribute = 38, NoBackdrop = 1, MainWindowBackdrop = 2;
        private const int ColorDefault = -1;
        private readonly Window _window;
        private readonly HwndSource _source;
        private bool _disposed, _refreshPending;
        private IntPtr _powerNotification;
        internal bool IsEnabled { get; private set; }
        internal string FallbackReason { get; private set; } = "Not initialized";

        [StructLayout(LayoutKind.Sequential)]
        private struct Margins { public int Left, Right, Top, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PowerStatus
        {
            public byte AcLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public uint BatteryLifeTime, BatteryFullLifeTime;
        }
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out PowerStatus status);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid setting, int flags);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnregisterPowerSettingNotification(IntPtr notification);

        internal WindowMaterialService(Window window)
        {
            _window = window;
            _source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)
                ?? throw new InvalidOperationException("Window materials require an initialized HWND.");
            _source.AddHook(OnWindowMessage);
            // GUID_POWER_SAVING_STATUS; notification only, never change the owner's power setting.
            var powerSaving = new Guid("E00958C0-C213-4ACE-AC77-FECCED2EEEA5");
            _powerNotification = RegisterPowerSettingNotification(_source.Handle, ref powerSaving, 0);
            ThemeService.Changed += Refresh;
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            Refresh();
        }

        // Pure policy boundary, independently checked without changing any Windows preferences.
        internal static bool CanUseMica(bool supported, bool nativeFrame, bool highContrast,
            bool transparency, bool energySaver, bool softwareRendering, bool composition) =>
            supported && nativeFrame && !highContrast && transparency && !energySaver && !softwareRendering && composition;

        private void Refresh()
        {
            if (_disposed || _source.IsDisposed) return;
            if (!_window.Dispatcher.CheckAccess()) { QueueRefresh(); return; }
            try
            {
                bool supported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621);
                bool nativeFrame = !_window.AllowsTransparency && _window.WindowStyle == WindowStyle.SingleBorderWindow;
                bool transparency = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) is int enabled && enabled != 0;
                // An unreadable power/composition policy is a conservative solid fallback.
                bool powerKnown = GetSystemPowerStatus(out var power) && _powerNotification != IntPtr.Zero;
                bool composition = DwmIsCompositionEnabled(out var composed) >= 0 && composed;
                if (!CanUseMica(supported, nativeFrame, SystemParameters.HighContrast, transparency,
                    !powerKnown || power.SystemStatusFlag != 0, RenderOptions.ProcessRenderMode == RenderMode.SoftwareOnly, composition))
                {
                    RestoreSolid("Windows support, accessibility, transparency, power or rendering policy");
                    return;
                }

                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                // Expose the WPF base only after every native operation succeeds.
                if (!SetAttribute(20, ThemeService.IsDark ? 1 : 0) ||
                    DwmExtendFrameIntoClientArea(_source.Handle, ref margins) < 0 ||
                    !SetAttribute(BackdropAttribute, MainWindowBackdrop) ||
                    !SetAttribute(35, ColorDefault) || !SetAttribute(36, ColorDefault) || !SetAttribute(34, ColorDefault))
                {
                    RestoreSolid("Native backdrop configuration failed");
                    return;
                }
                if (_source.CompositionTarget == null) { RestoreSolid("No WPF composition target"); return; }
                _source.CompositionTarget.BackgroundColor = Colors.Transparent;
                _window.SetCurrentValue(Window.BackgroundProperty, Brushes.Transparent);
                IsEnabled = true;
                FallbackReason = "";
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or
                System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException or ExternalException)
            {
                RestoreSolid("Native API or policy unavailable");
            }
        }

        private bool SetAttribute(int attribute, int value) =>
            DwmSetWindowAttribute(_source.Handle, attribute, ref value, sizeof(int)) >= 0;

        private void RestoreSolid(string reason)
        {
            // Paint first: a failed rollback must never leave transparent/black client content.
            var background = (Color)ColorConverter.ConvertFromString(ThemeService.Hex("BackgroundBrush"));
            if (_source.CompositionTarget != null) _source.CompositionTarget.BackgroundColor = background;
            _window.SetCurrentValue(Window.BackgroundProperty, _window.TryFindResource("BackgroundBrush") as Brush
                ?? new SolidColorBrush(background));
            IsEnabled = false;
            FallbackReason = reason;
            try
            {
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
                {
                    bool resetBackdrop = SetAttribute(BackdropAttribute, NoBackdrop);
                    var margins = new Margins();
                    bool resetFrame = DwmExtendFrameIntoClientArea(_source.Handle, ref margins) >= 0;
                    if (!resetBackdrop || !resetFrame) Debug.WriteLine("Backdrop/frame reset refused; opaque WPF fallback remains painted.");
                }
                bool darkSet = SetAttribute(20, ThemeService.IsDark ? 1 : 0);
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                {
                    bool captionSet = SetAttribute(35, ColorRef("BackgroundBrush"));
                    bool textSet = SetAttribute(36, ColorRef("TextPrimaryBrush"));
                    bool borderSet = SetAttribute(34, ColorRef("BorderBrush"));
                    if (!captionSet || !textSet || !borderSet) Debug.WriteLine("Explicit native chrome color refused; default native chrome retained.");
                }
                if (!darkSet) Debug.WriteLine("Native dark-mode attribute unsupported; default title bar retained.");
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or ExternalException)
            { Debug.WriteLine("Native chrome unavailable; solid WPF background retained."); }
            Debug.WriteLine($"Window material fallback: {reason}");
        }

        private static int ColorRef(string key)
        {
            var c = (Color)ColorConverter.ConvertFromString(ThemeService.Hex(key));
            return c.R | c.G << 8 | c.B << 16;
        }

        private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // Leave all native messages unhandled, including frame, sizing, activation and system menu.
            if (message is 0x001A or 0x031A or 0x031E or 0x0218) QueueRefresh();
            return IntPtr.Zero;
        }

        private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SystemParameters.HighContrast)) QueueRefresh();
        }
        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => QueueRefresh();
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => QueueRefresh();
        private void QueueRefresh()
        {
            if (_disposed || _window.Dispatcher.HasShutdownStarted) return;
            if (!_window.Dispatcher.CheckAccess())
            {
                _window.Dispatcher.BeginInvoke(new Action(QueueRefresh));
                return;
            }
            if (_refreshPending) return;
            _refreshPending = true;
            _window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshPending = false;
                Refresh();
            }));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ThemeService.Changed -= Refresh;
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            if (_powerNotification != IntPtr.Zero)
            {
                if (!UnregisterPowerSettingNotification(_powerNotification)) Debug.WriteLine("Power-notification cleanup refused; HWND destruction provides final cleanup.");
                _powerNotification = IntPtr.Zero;
            }
            if (!_source.IsDisposed) _source.RemoveHook(OnWindowMessage);
        }
    }
}
