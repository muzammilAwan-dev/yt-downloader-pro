using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using YTDLPHost;
using YTDLPHost.Services;

// Isolated material experiment. No production startup, persistence or real queue.
internal static class MaterialProbe
{
    [StructLayout(LayoutKind.Sequential)] private struct Margins { public int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    private static readonly List<object> Results = new();
    private static ResourceDictionary Resources(ThemeMode mode) => (ResourceDictionary)typeof(Program).GetMethod("LoadResources", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { mode })!;
    private static PreviewQueue Queue() => (PreviewQueue)typeof(Program).GetMethod("CreateQueue", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
    private static void Chrome(MainWindow window) => typeof(Program).GetMethod("SetFixtureChrome", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { window });

    [STAThread]
    public static void Main(string[] args)
    {
        var output = args.Single(); Directory.CreateDirectory(output);
        var app = new PreviewApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources = Resources(ThemeMode.Dark);
        MainWindow NewWindow()
        {
            var work = SystemParameters.WorkArea;
            return new MainWindow
            {
                DataContext = Queue(), Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = work.Left + 8, Top = work.Top + 8,
                Width = Math.Min(980, work.Width - 16), Height = Math.Min(700, work.Height - 16)
            };
        }
        var window = NewWindow();
        Chrome(window);
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                await System.Threading.Tasks.Task.Delay(500);
                Results.Add(new { Check = "Selected preview activation", Active = NativeWindowCapture.TryActivate(window) });
                await System.Threading.Tasks.Task.Delay(200);
                if (!Capture(window, output, "01-solid-dark.png")) { app.Shutdown(); return; }
                Apply(window, true);
                await System.Threading.Tasks.Task.Delay(500);
                if (!Capture(window, output, "02-mica-dark.png")) { app.Shutdown(); return; }
                var style = GetWindowLongPtr(new WindowInteropHelper(window).Handle, -16).ToInt64();
                window.WindowState = WindowState.Maximized;
                await System.Threading.Tasks.Task.Delay(200);
                window.WindowState = WindowState.Normal;
                await System.Threading.Tasks.Task.Delay(200);
                Results.Add(new { Check = "Programmatic maximize/restore", State = window.WindowState.ToString(), NativeStyleUnchanged = style == GetWindowLongPtr(new WindowInteropHelper(window).Handle, -16).ToInt64(), NativeCaption = (style & 0x00C00000) != 0, NativeResizeFrame = (style & 0x00040000) != 0 });
                window.Width = window.MinWidth; window.Height = window.MinHeight;
                await System.Threading.Tasks.Task.Delay(300);
                Capture(window, output, "03-mica-minimum-dark.png");
                Apply(window, false);
                await System.Threading.Tasks.Task.Delay(300);
                Capture(window, output, "04-solid-restored-dark.png");
                var old = window;
                app.Resources = Resources(ThemeMode.Light);
                window = NewWindow();
                Chrome(window); window.Show(); old.Close();
                await System.Threading.Tasks.Task.Delay(400);
                Results.Add(new { Check = "Selected preview activation", Active = NativeWindowCapture.TryActivate(window) });
                Apply(window, true);
                await System.Threading.Tasks.Task.Delay(400);
                Capture(window, output, "05-mica-light.png");
                Apply(window, false);
                await System.Threading.Tasks.Task.Delay(300);
                Capture(window, output, "06-solid-restored-light.png");
                app.Shutdown();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "probe-error.txt"), ex.ToString()); app.Shutdown(1); }
            finally { File.WriteAllText(Path.Combine(output, "probe-results.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true })); }
        };
        app.Run(window);
    }

    private static void Apply(MainWindow window, bool enable)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var target = HwndSource.FromHwnd(hwnd).CompositionTarget;
        int kind = enable ? 2 : 1, caption = enable ? -1 : ColorRef(ThemeService.Hex("BackgroundBrush")), text = enable ? -1 : ColorRef(ThemeService.Hex("TextPrimaryBrush"));
        int dark = ThemeService.IsDark ? 1 : 0;
        var margins = new Margins { Left = enable ? -1 : 0, Right = enable ? -1 : 0, Top = enable ? -1 : 0, Bottom = enable ? -1 : 0 };
        int darkResult = DwmSetWindowAttribute(hwnd, 20, ref dark, 4);
        int frameResult = DwmExtendFrameIntoClientArea(hwnd, ref margins);
        int backdropResult = DwmSetWindowAttribute(hwnd, 38, ref kind, 4);
        int captionResult = DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
        int textResult = DwmSetWindowAttribute(hwnd, 36, ref text, 4);
        target.BackgroundColor = enable ? Colors.Transparent : (Color)ColorConverter.ConvertFromString(ThemeService.Hex("BackgroundBrush"));
        window.Background = enable ? Brushes.Transparent : (Brush)Application.Current.Resources["BackgroundBrush"];
        int readResult = DwmGetWindowAttribute(hwnd, 38, out var actualKind, 4);
        Results.Add(new { Check = enable ? "Prototype enable" : "Prototype restore solid", Theme = ThemeService.Mode.ToString(), OS = Environment.OSVersion.Version.ToString(), Runtime = RuntimeInformation.FrameworkDescription, WindowsTransparency = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1), HighContrast = SystemParameters.HighContrast, DarkHRESULT = darkResult, FrameHRESULT = frameResult, BackdropHRESULT = backdropResult, CaptionHRESULT = captionResult, TextHRESULT = textResult, ReadHRESULT = readResult, ActualBackdropType = actualKind, window.AllowsTransparency, WindowStyle = window.WindowStyle.ToString(), CompositionBackground = target.BackgroundColor.ToString() });
    }
    private static int ColorRef(string hex) { var c = (Color)ColorConverter.ConvertFromString(hex); return c.R | c.G << 8 | c.B << 16; }
    private static bool Capture(MainWindow window, string output, string name)
    {
        bool captured = NativeWindowCapture.TryCapture(window, Path.Combine(output, name), out var detail);
        Results.Add(new { Check = "Screen capture", File = name, Captured = captured, Detail = detail });
        return captured;
    }
}
