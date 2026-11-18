using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using YTDLPHost;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

// Actual compiled application resources, but never production startup or exit.
public sealed partial class SafeCompiledApplication : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e) { }
    protected override void OnExit(ExitEventArgs e) { }
}

internal static class ProductVerification
{
    private static readonly List<object> Results = new();
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object Material(MainWindow window) => typeof(MainWindow).GetField("_windowMaterial", Private)!.GetValue(window)!;
    private static bool Enabled(MainWindow window) => (bool)Material(window).GetType().GetProperty("IsEnabled", Private)!.GetValue(Material(window))!;
    private static PreviewQueue Queue() => (PreviewQueue)typeof(Program).GetMethod("CreateQueue", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
    private static void Chrome(MainWindow window) => typeof(Program).GetMethod("SetFixtureChrome", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { window });
    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in Walk(VisualTreeHelper.GetChild(root, i))) yield return item;
    }

    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort Vk, Scan; public uint Flags, Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Key; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--palette-states") { PaletteStates.Run(args[1]); return; }
        var output = args.Single(); Directory.CreateDirectory(output);
        var app = new SafeCompiledApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        ThemeService.SetMode(ThemeMode.Dark);
        var work = SystemParameters.WorkArea;
        var queue = Queue();
        var window = new MainWindow
        {
            DataContext = queue, Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = work.Left + 8, Top = work.Top + 8, Width = Math.Min(980, work.Width - 16), Height = Math.Min(700, work.Height - 16)
        };
        Chrome(window);
        window.ContentRendered += async (_, _) =>
        {
            try
            {
                await Task.Delay(400);
                NativeWindowCapture.TryActivate(window);
                await Task.Delay(300);
                Record(window, "Production Mica dark"); Capture(window, output, "01-mica-dark.png");
                var caption = Walk(window).OfType<TextBlock>().Single(t => t.Text == "YT Downloader Pro");
                var darkBrush = (SolidColorBrush)caption.Foreground;
                ThemeService.SetMode(ThemeMode.Light);
                await Task.Delay(400);
                var lightBrush = (SolidColorBrush)caption.Foreground;
                Results.Add(new { Check = "Source resources compiled in safe application, runtime theme switch", SameBrushInstance = ReferenceEquals(darkBrush, lightBrush), Resource = ((SolidColorBrush)app.Resources["TextPrimaryBrush"]).Color.ToString(), VisibleHeaderBrush = lightBrush.Color.ToString(), BrushFrozen = lightBrush.IsFrozen, MatchesPalette = lightBrush.Color == (Color)ColorConverter.ConvertFromString(ThemeService.Hex("TextPrimaryBrush")) });
                Record(window, "Production Mica light"); Capture(window, output, "02-mica-light.png");
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                ThemeService.SetMode(ThemeMode.Light);
                await Task.Delay(300);
                Record(window, "Software-rendering solid fallback light"); Capture(window, output, "03-fallback-light.png");
                ThemeService.SetMode(ThemeMode.Dark);
                await Task.Delay(300);
                Record(window, "Software-rendering solid fallback dark"); Capture(window, output, "04-fallback-dark.png");
                RenderOptions.ProcessRenderMode = RenderMode.Default;
                ThemeService.SetMode(ThemeMode.System);
                await Task.Delay(300);
                Record(window, "Runtime system theme"); Capture(window, output, "05-system-theme.png");
                ThemeService.SetMode(ThemeMode.Light);
                await Task.Delay(300);
                await KeyboardChecks(window, output);
                var style = GetWindowLongPtr(new WindowInteropHelper(window).Handle, -16).ToInt64();
                window.WindowState = WindowState.Maximized; await Task.Delay(200);
                window.WindowState = WindowState.Normal; await Task.Delay(200);
                Results.Add(new { Check = "Production native maximize/restore", Restored = window.WindowState == WindowState.Normal, NativeStyleUnchanged = style == GetWindowLongPtr(new WindowInteropHelper(window).Handle, -16).ToInt64(), Caption = (style & 0x00C00000) != 0, ResizeFrame = (style & 0x00040000) != 0, window.AllowsTransparency, window.WindowStyle });
                window.Width = window.MinWidth; window.Height = window.MinHeight;
                await Task.Delay(300);
                Record(window, "Production minimum light"); Capture(window, output, "06-minimum-light.png");
                window.Close();
                // Fresh light HWND also checks material/solid at identical data, size and native activation.
                var fresh = new MainWindow
                {
                    DataContext = queue, Topmost = true, WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = work.Left + 8, Top = work.Top + 8, Width = Math.Min(980, work.Width - 16), Height = Math.Min(700, work.Height - 16)
                };
                Chrome(fresh); fresh.Show();
                await Task.Delay(400);
                NativeWindowCapture.TryActivate(fresh);
                await Task.Delay(500);
                Record(fresh, "Fresh production light HWND Mica"); Capture(fresh, output, "08-fresh-light-mica.png");
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                ThemeService.SetMode(ThemeMode.Light);
                await Task.Delay(400);
                Record(fresh, "Fresh production light HWND solid"); Capture(fresh, output, "09-fresh-light-solid.png");
                fresh.Close();
                ThemeService.SetMode(ThemeMode.Dark); // Closed/disposed service must not react.
                Results.Add(new { Check = "Closed window theme change", CompletedWithoutException = true });
                app.Shutdown();
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "verification-error.txt"), ex.ToString()); app.Shutdown(1); }
            finally { File.WriteAllText(Path.Combine(output, "verification-results.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true })); }
        };
        app.Run(window);
    }

    private static void Record(MainWindow window, string label)
    {
        var material = Material(window);
        int hr = DwmGetWindowAttribute(new WindowInteropHelper(window).Handle, 38, out var kind, 4);
        Results.Add(new { Check = label, IsMaterialEnabled = Enabled(window), Fallback = material.GetType().GetProperty("FallbackReason", Private)!.GetValue(material), Theme = ThemeService.Mode.ToString(), ThemeService.IsDark, Background = window.Background.ToString(), CompositionBackground = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle).CompositionTarget.BackgroundColor.ToString(), BackdropReadHRESULT = hr, BackdropType = kind, window.Width, window.Height, DPI = VisualTreeHelper.GetDpi(window).PixelsPerDip * 96, Runtime = RuntimeInformation.FrameworkDescription });
    }
    private static void Capture(MainWindow window, string output, string file)
    {
        bool captured = NativeWindowCapture.TryCapture(window, Path.Combine(output, file), out var detail);
        Results.Add(new { Check = "Selected-window composited capture", File = file, Captured = captured, Detail = detail });
    }
    private static async Task<bool> Key(MainWindow window, ushort key, bool shift = false)
    {
        if (GetForegroundWindow() != new WindowInteropHelper(window).Handle)
        { Results.Add(new { Check = "Keyboard guard", Blocked = "Selected preview not foreground; no key sent." }); return false; }
        var inputs = new List<Input>();
        if (shift) inputs.Add(new Input { Type = 1, Key = new KeyboardInput { Vk = 16 } });
        inputs.Add(new Input { Type = 1, Key = new KeyboardInput { Vk = key } });
        inputs.Add(new Input { Type = 1, Key = new KeyboardInput { Vk = key, Flags = 2 } });
        if (shift) inputs.Add(new Input { Type = 1, Key = new KeyboardInput { Vk = 16, Flags = 2 } });
        if (SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()) != inputs.Count) throw new InvalidOperationException("Incomplete selected-window keyboard input.");
        await Task.Delay(100); return true;
    }
    private static string FocusName() => Keyboard.FocusedElement is DependencyObject element ? AutomationProperties.GetName(element) : "none";
    private static async Task KeyboardChecks(MainWindow window, string output)
    {
        if (!NativeWindowCapture.TryActivate(window)) { Results.Add(new { Check = "Keyboard", Blocked = true }); return; }
        var toggle = (ToggleButton)window.FindName("LimitsToggle");
        Keyboard.Focus(toggle);
        var forward = new List<object>();
        for (int i = 0; i < 48; i++)
        {
            if (!await Key(window, 9)) break;
            forward.Add(new { Step = i, Focus = FocusName() });
            if (ReferenceEquals(Keyboard.FocusedElement, toggle)) break;
        }
        Results.Add(new { Check = "Actual Tab input (programmatic initial focus)", Traversal = forward, ReturnedToStart = ReferenceEquals(Keyboard.FocusedElement, toggle) });
        var reverse = new List<object>();
        for (int i = 0; i < 48; i++)
        {
            if (!await Key(window, 9, true)) break;
            reverse.Add(new { Step = i, Focus = FocusName() });
            if (ReferenceEquals(Keyboard.FocusedElement, toggle)) break;
        }
        Results.Add(new { Check = "Actual Shift+Tab input", Traversal = reverse, ReturnedToStart = ReferenceEquals(Keyboard.FocusedElement, toggle) });
        Keyboard.Focus(toggle); await Key(window, 32);
        Results.Add(new { Check = "Space opens limits", Open = toggle.IsChecked == true });
        // Header actions precede the inline panel in normal document order; tab until its first field.
        for (int i = 0; i < 12 && Keyboard.FocusedElement is not TextBox; i++)
            if (!await Key(window, 9)) break;
        Capture(window, output, "07-limits-keyboard-focus-light.png");
        Results.Add(new { Check = "Tab reaches limits input", Focus = FocusName(), Type = Keyboard.FocusedElement?.GetType().Name });
        Keyboard.Focus(toggle); await Key(window, 32);
        var pause = Walk(window).OfType<Button>().First(b => AutomationProperties.GetName(b) == "Pause" && b.IsVisible);
        var item = (DownloadItemViewModel)pause.DataContext;
        Keyboard.Focus(pause); await Key(window, 32);
        Results.Add(new { Check = "Space pauses synthetic task", Status = item.Task.Status.ToString(), Focus = FocusName() });
        await Key(window, 32);
        Results.Add(new { Check = "Space resumes synthetic task", Status = item.Task.Status.ToString(), Focus = FocusName() });
        var settings = Walk(window).OfType<Button>().First(b => AutomationProperties.GetName(b) == "Settings and tools");
        Keyboard.Focus(settings); await Key(window, 32);
        await Key(window, 27);
        Results.Add(new { Check = "Settings Space / Escape", FocusRestored = ReferenceEquals(Keyboard.FocusedElement, settings) });
        // Never invoke copy, remove, real settings/tool/engine actions or native tray hiding.
    }
}
