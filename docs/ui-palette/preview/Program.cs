using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.Input;
using YTDLPHost;
using YTDLPHost.Models;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

// Render the actual compiled WPF window, resources and item view models.
// The root fixture has no engine, persistence, protocol or external actions.
// This verifies presentation only, NOT MainViewModel/startup/download behavior.
public sealed class PreviewApplication : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e) { }
    protected override void OnExit(ExitEventArgs e) { }
}

public sealed class PreviewQueue
{
    public ObservableCollection<DownloadItemViewModel> Downloads { get; } = new();
    public bool HasDownloads => Downloads.Count > 0;
    public int ActiveDownloadCount => Downloads.Count(d => d.IsActive);
    public AppSettings Settings { get; } = new();
    public IRelayCommand<DownloadItemViewModel> OpenFolderCommand { get; } = new RelayCommand<DownloadItemViewModel>(_ => { });
    public IRelayCommand<DownloadItemViewModel> PlayFileCommand { get; } = new RelayCommand<DownloadItemViewModel>(_ => { });
    public IRelayCommand<DownloadItemViewModel> ResumeDownloadCommand { get; } = new RelayCommand<DownloadItemViewModel>(d => { if (d != null) d.Task.Status = DownloadStatus.Downloading; });
    public IRelayCommand<DownloadItemViewModel> PauseDownloadCommand { get; } = new RelayCommand<DownloadItemViewModel>(d => { if (d != null) d.Task.Status = DownloadStatus.Paused; });
    public IRelayCommand<DownloadItemViewModel> CancelDownloadCommand { get; } = new RelayCommand<DownloadItemViewModel>(_ => { });
    public IRelayCommand<DownloadItemViewModel> RemoveDownloadCommand { get; } = new RelayCommand<DownloadItemViewModel>(_ => { });
    public IRelayCommand SaveSettingsCommand { get; } = new RelayCommand(() => { });
}

public static class Program
{
    private static ContextMenu? OpenedMenu;
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { public ushort Vk; public ushort Scan; public uint Flags; public uint Time; public IntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct NativeInput { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public KeyboardInput Keyboard; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] inputs, int size);
    [STAThread]
    public static void Main(string[] args)
    {
        var output = args.Length == 1 ? args[0] : AppContext.BaseDirectory;
        Directory.CreateDirectory(output);
        File.Delete(Path.Combine(output, "preview-error.txt"));
        File.WriteAllText(Path.Combine(output, "preview-context.txt"),
            "Actual compiled MainWindow; App.xaml resource dictionary parsed from source with assembly-qualified converter names and colors initialized from the actual ThemeService palette; synthetic root queue; actual DownloadTask/DownloadItemViewModel.\n" +
            "No normal startup, MainViewModel, protocol, history, tray, engine or file commands executed. Synthetic pause/resume change fixture status only; other root commands are no-ops.\n" +
            $"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}\n" +
            $"Runtime host: {Environment.ProcessPath}\nWPF assembly: {typeof(Window).Assembly.Location}\n" +
            "Client-area WPF renders at 96 DPI with Window.Background composited; filter/footer/counts set by fixture; native title bar not captured; not end-to-end testing.\n" +
            "Separate light window/resources created for static-palette fidelity. This parsed-resource fixture measures static palettes only; runtime switching is verified separately with source resources compiled into a safe application.\n" +
            $"Windows ClientAreaAnimation={SystemParameters.ClientAreaAnimation}. Physical scaling/contrast themes/Snap/Narrator not tested.\n");
        File.AppendAllText(Path.Combine(output, "preview-context.txt"), "This solid palette fixture skips native SendInput/foreground activation tests. No physical keys sent.\n");
        try
        {
            var app = new PreviewApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            EventManager.RegisterClassHandler(typeof(MainWindow), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) =>
                {
                    if (sender is not MainWindow main) return;
                    var material = typeof(MainWindow).GetField("_windowMaterial", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(main)!;
                    material.GetType().GetMethod("RestoreSolid", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                        .Invoke(material, new object[] { "Matched synthetic solid-state preview" });
                    ((IDisposable)material).Dispose(); // Keep Windows messages from re-enabling Mica during bitmap-only verification.
                }));
            File.AppendAllText(Path.Combine(output, "preview-context.txt"), "After preview explicitly restores the production service's solid fallback for matched client-state captures; this bitmap cannot capture DWM material.\n");
            EventManager.RegisterClassHandler(typeof(ContextMenu), ContextMenu.OpenedEvent,
                new RoutedEventHandler((sender, _) => OpenedMenu = sender as ContextMenu));
            app.Resources = LoadResources(ThemeMode.Dark);
            LogBrushes(output, "Parsed dark resources");
            var queue = new PreviewQueue();
            var window = new MainWindow { DataContext = queue };
            ((TextBlock)window.FindName("FooterText")).Text = "Ready";
            var step = 0;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
            timer.Tick += async (_, _) =>
            {
                try
                {
                    switch (step++)
                    {
                        case 0:
                            Capture(window, output, "01-empty-dark.png");
                            queue = CreateQueue();
                            window.DataContext = queue;
                            SetFixtureChrome(window);
                            break;
                        case 1:
                            Capture(window, output, "02-queue-dark.png");
                            Inspect(window, output, "dark-queue");
                            timer.Stop();
                            await TestNativeControlsAsync(window, output);
                            timer.Start();
                            LogBrushes(output, "Before light switch");
                            ThemeService.SetMode(ThemeMode.Light);
                            LogBrushes(output, "After light switch");
                            File.AppendAllText(Path.Combine(output, "brush-diagnostics.txt"), $"Window.Background={window.Background}\n");
                            var previous = window;
                            app.Resources = LoadResources(ThemeMode.Light);
                            window = new MainWindow { DataContext = queue };
                            SetFixtureChrome(window);
                            window.Show();
                            previous.Close();
                            LogBrushes(output, "Fresh static light resources");
                            break;
                        case 2:
                            Capture(window, output, "03-queue-light.png");
                            Inspect(window, output, "light-queue");
                            ((ToggleButton)window.FindName("LimitsToggle")).IsChecked = true;
                            break;
                        case 3:
                            Capture(window, output, "04-limits-light.png");
                            window.Width = window.MinWidth;
                            window.Height = window.MinHeight;
                            break;
                        case 4:
                            Capture(window, output, "05-minimum-limits-light.png");
                            Inspect(window, output, "light-minimum-limits");
                            app.Resources = LoadResources(ThemeMode.Dark);
                            var old = window;
                            queue = CreateAdditionalStates();
                            window = new MainWindow { DataContext = queue };
                            window.Show();
                            old.Close();
                            ((TextBlock)window.FindName("FooterText")).Text = "2 downloading · 1 waiting";
                            break;
                        case 5:
                            Capture(window, output, "06-queued-processing-live-cancelled-dark.png");
                            Inspect(window, output, "additional-states-dark");
                            queue = new PreviewQueue();
                            var error = new DownloadItemViewModel(new DownloadTask
                            {
                                Title = "Independent short film — behind the scenes", Status = DownloadStatus.Error,
                                ErrorMessage = "ERROR: HTTP Error 403: Forbidden\nSynthetic diagnostic detail — no URL or credentials."
                            });
                            error.Task.AppendLog("[preview] Synthetic diagnostic log — no network or file actions.");
                            error.Task.AppendLog("[download] 42.6% of 128.40 MiB at 3.30MiB/s ETA 00:22");
                            error.Task.AppendLog("ERROR: HTTP Error 403: Forbidden");
                            error.IsErrorDetailsVisible = true;
                            error.IsLogVisible = true;
                            queue.Downloads.Add(error);
                            window.DataContext = queue;
                            ((TextBlock)window.FindName("FooterText")).Text = "All downloads finished";
                            break;
                        case 6:
                            Capture(window, output, "07-expanded-error-and-log-dark.png");
                            Inspect(window, output, "error-details-log-dark");
                            var settingsButton = Walk(window).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Settings and tools");
                            settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            break;
                        case 7:
                            var menus = OpenedMenu == null ? new List<ContextMenu>() : new List<ContextMenu> { OpenedMenu };
                            File.AppendAllText(Path.Combine(output, "preview-context.txt"), $"Open settings menus found={menus.Count}. Only opened; no choices invoked.\n");
                            foreach (var menu in menus)
                            {
                                DumpPeers(menu, output, "settings-menu");
                                foreach (var item in menu.Items.OfType<MenuItem>())
                                {
                                    var peer = UIElementAutomationPeer.CreatePeerForElement(item);
                                    File.AppendAllText(Path.Combine(output, "settings-menu-names.txt"),
                                        $"visible={string.Join(" | ", Walk(item).OfType<TextBlock>().Select(t => t.Text))}; peer-name={peer?.GetName()}; type={peer?.GetAutomationControlType()}\n");
                                }
                                menu.IsOpen = false;
                            }
                            queue = new PreviewQueue();
                            for (var i = 0; i < 200; i++) queue.Downloads.Add(new DownloadItemViewModel(new DownloadTask { Title = $"Synthetic queued item {i + 1}", Status = DownloadStatus.Queued }));
                            window.DataContext = queue;
                            break;
                        case 8:
                            window.UpdateLayout();
                            var list = Walk(window).OfType<ListBox>().Single();
                            var realized = Enumerable.Range(0, list.Items.Count).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null);
                            File.WriteAllText(Path.Combine(output, "large-queue-containers.txt"),
                                $"Synthetic items={list.Items.Count}; generated ListBoxItem containers={realized}; CanContentScroll={ScrollViewer.GetCanContentScroll(list)}. No performance timing or engine throughput test.\n");
                            timer.Stop();
                            File.AppendAllText(Path.Combine(output, "preview-context.txt"), "Seven captures completed. Programmatic focus traversal and WPF automation-peer inspection, NOT keyboard-only/Narrator tests.\n");
                            app.Shutdown();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(output, "preview-error.txt"), ex.ToString());
                    timer.Stop();
                    app.Shutdown(1);
                }
            };
            window.ContentRendered += (_, _) => timer.Start();
            app.Run(window);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "preview-error.txt"), ex.ToString());
            Environment.ExitCode = 1;
        }
    }

    private static PreviewQueue CreateQueue()
    {
        var queue = new PreviewQueue();
        queue.Downloads.Add(new DownloadItemViewModel(new DownloadTask
        {
            Title = "A walk through the mountains at sunrise", Resolution = "1080p",
            Status = DownloadStatus.Downloading, CurrentPhase = "Downloading video...",
            Progress = 42.6, FileSize = "128.40 MiB", Speed = "3.30MiB/s", Eta = "00:22"
        }));
        queue.Downloads.Add(new DownloadItemViewModel(new DownloadTask
        {
            Title = "Acoustic session — live at the studio", Resolution = "MP3",
            Status = DownloadStatus.Paused, Progress = 61.3
        }));
        queue.Downloads.Add(new DownloadItemViewModel(new DownloadTask
        {
            Title = "Independent short film — behind the scenes", Resolution = "720p",
            Status = DownloadStatus.Completed, CompletedAt = new DateTime(2026, 11, 15, 10, 42, 0)
        }));
        queue.Downloads.Add(new DownloadItemViewModel(new DownloadTask
        {
            Title = "A very long documentary title about restoring historical buildings and preserving local craft traditions for the next generation",
            Resolution = "1080p", Status = DownloadStatus.Error,
            ErrorMessage = "ERROR: HTTP Error 403: Forbidden"
        }));
        return queue;
    }

    private static PreviewQueue CreateAdditionalStates()
    {
        var queue = new PreviewQueue();
        queue.Downloads.Add(new DownloadItemViewModel(new DownloadTask { Status = DownloadStatus.Queued }));
        queue.Downloads.Add(new DownloadItemViewModel(new DownloadTask
        {
            Title = "A walk through the mountains at sunrise", Status = DownloadStatus.Downloading,
            CurrentPhase = "Converting Audio...", Progress = 100, IsIndeterminate = true,
            Speed = "3.30MiB/s", Eta = "00:00", FileSize = "128.40 MiB"
        }));
        queue.Downloads.Add(new DownloadItemViewModel(new DownloadTask
        {
            Title = "Live acoustic session — duration unknown", Status = DownloadStatus.Downloading,
            CurrentPhase = "Downloading live stream...", IsIndeterminate = true,
            Speed = "1.20MiB/s", Eta = "00:42", FileSize = "12.40 MiB"
        }));
        queue.Downloads.Add(new DownloadItemViewModel(new DownloadTask { Status = DownloadStatus.Cancelled }));
        return queue;
    }

    private static void SetFixtureChrome(MainWindow window)
    {
        ((FrameworkElement)window.FindName("FilterTrack")).Visibility = Visibility.Visible;
        ((RadioButton)window.FindName("AllTab")).Tag = 4;
        ((RadioButton)window.FindName("ActiveTab")).Tag = 2;
        ((RadioButton)window.FindName("DoneTab")).Tag = 1;
        ((RadioButton)window.FindName("FailedTab")).Tag = 1;
        ((TextBlock)window.FindName("FooterText")).Text = "1 downloading · 1 paused · 1 completed · 1 failed";
        ((TextBlock)window.FindName("FooterSpeedText")).Text = "3.30 MiB/s";
        ((TextBlock)window.FindName("FooterSpeedText")).Visibility = Visibility.Visible;
        ((Button)window.FindName("ClearFinishedButton")).Visibility = Visibility.Visible;
    }

    private static ResourceDictionary LoadResources(ThemeMode mode)
    {
        // WPF's generated App.InitializeComponent requires App's own assembly.
        // Use source resources rather than executing real startup. Initialize
        // actual palette colors before parsing, since parsed styles freeze
        // shared brushes and cannot establish runtime switch fidelity.
        ThemeService.SetMode(mode);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(@"C:\Users\Malku\Downloads\Compressed\yt-downloader-pro\host\YTDLPHost\App.xaml");
        var dictionary = new XElement(source.Root!.Element(wpf + "Application.Resources")!.Element(wpf + "ResourceDictionary")!);
        foreach (var attribute in source.Root.Attributes().Where(a => a.IsNamespaceDeclaration))
            dictionary.SetAttributeValue(attribute.Name, attribute.Value.Contains("clr-namespace:YTDLPHost.Converters")
                ? attribute.Value + ";assembly=YTDLPHost" : attribute.Value);
        foreach (var element in dictionary.Descendants().Where(e => e.Name.NamespaceName == "clr-namespace:YTDLPHost.Converters").ToList())
            element.Name = XName.Get(element.Name.LocalName, "clr-namespace:YTDLPHost.Converters;assembly=YTDLPHost");
        foreach (var brush in dictionary.Elements(wpf + "SolidColorBrush"))
            brush.SetAttributeValue("Color", ThemeService.Hex((string)brush.Attribute(xaml + "Key")!));
        foreach (var color in dictionary.Elements(wpf + "Color"))
        {
            var key = (string?)color.Attribute(xaml + "Key");
            if (key == "AccentColor") color.Value = ThemeService.Hex("AccentBrush");
            if (key == "AccentClearColor") color.Value = "#00" + ThemeService.Hex("AccentBrush").TrimStart('#');
        }
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private static void Capture(MainWindow window, string output, string file)
    {
        Walk(window).OfType<ListBox>().SelectMany(list => Walk(list).OfType<ScrollViewer>()).FirstOrDefault()?.ScrollToTop();
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),
            (int)Math.Ceiling(root.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            var bounds = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
            drawing.DrawRectangle(window.Background, null, bounds);
        }
        bitmap.Render(visual);
        // Render directly so transparent margins retain their layout coordinates;
        // a default VisualBrush maps content bounds and can distort spacing.
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, file));
        encoder.Save(stream);
        File.AppendAllText(Path.Combine(output, "preview-context.txt"),
            $"{file}: {bitmap.PixelWidth}x{bitmap.PixelHeight} pixels; Window={window.ActualWidth}x{window.ActualHeight} DIPs; background={window.Background}; host DPI={VisualTreeHelper.GetDpi(root).PixelsPerDip * 96}.\n");
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Walk(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static void Inspect(MainWindow window, string output, string label)
    {
        window.UpdateLayout();
        var measurements = new List<object>();
        foreach (var text in Walk(window).OfType<TextBlock>().Where(t => t.IsVisible && !string.IsNullOrEmpty(t.Text)))
        {
            var ancestors = new List<DependencyObject>();
            for (DependencyObject? node = text; node != null; node = VisualTreeHelper.GetParent(node)) ancestors.Add(node);
            var background = Colors.Transparent;
            foreach (var node in ancestors.AsEnumerable().Reverse())
            {
                // A Control.Background is not evidence that it is painted:
                // e.g. RadioButton's custom template draws a transparent Border
                // and ignores its default Background. Measure drawing elements.
                Brush? brush = node switch { Border b => b.Background, Panel p => p.Background, Window w => w.Background, _ => null };
                if (brush is SolidColorBrush solid) background = Composite(solid.Color, background, 1);
            }
            if (text.Foreground is not SolidColorBrush foreground) continue;
            var opacity = ancestors.OfType<UIElement>().Aggregate(1.0, (total, node) => total * node.Opacity);
            var renderedForeground = Composite(foreground.Color, background, opacity);
            var pos = text.TranslatePoint(new Point(0, 0), (UIElement)window.Content);
            measurements.Add(new
            {
                text.Text, FontSize = text.FontSize, Font = text.FontFamily.ToString(), Foreground = foreground.Color.ToString(),
                Background = background.ToString(), Opacity = opacity, Contrast = Contrast(renderedForeground, background),
                X = pos.X, Y = pos.Y, Width = text.ActualWidth, Height = text.ActualHeight,
                Scope = "effective solid drawing-element brushes, alpha composited; no hover/focus/pixel glyph sampling"
            });
        }
        File.WriteAllText(Path.Combine(output, label + "-text-measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        DumpPeers(window, output, label);
        var toggle = (ToggleButton)window.FindName("LimitsToggle");
        Keyboard.Focus(toggle);
        var focus = new List<object>();
        for (var i = 0; i < 64; i++)
        {
            if (Keyboard.FocusedElement is not UIElement current) break;
            var peer = UIElementAutomationPeer.CreatePeerForElement(current);
            var task = (current as FrameworkElement)?.DataContext as DownloadItemViewModel;
            focus.Add(new { Index = i, Type = current.GetType().Name, Name = peer?.GetName(), Task = task?.DisplayTitle, Moved = current.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)) });
            if (ReferenceEquals(Keyboard.FocusedElement, toggle)) break;
        }
        File.WriteAllText(Path.Combine(output, label + "-focus-traversal.json"), JsonSerializer.Serialize(focus, new JsonSerializerOptions { WriteIndented = true }));
        focus.Clear();
        Keyboard.Focus(toggle);
        for (var i = 0; i < 64; i++)
        {
            if (Keyboard.FocusedElement is not UIElement current) break;
            var peer = UIElementAutomationPeer.CreatePeerForElement(current);
            focus.Add(new { Index = i, Type = current.GetType().Name, Name = peer?.GetName(), Task = ((current as FrameworkElement)?.DataContext as DownloadItemViewModel)?.DisplayTitle, Moved = current.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)) });
            if (ReferenceEquals(Keyboard.FocusedElement, toggle)) break;
        }
        File.WriteAllText(Path.Combine(output, label + "-reverse-focus-traversal.json"), JsonSerializer.Serialize(focus, new JsonSerializerOptions { WriteIndented = true }));
        Keyboard.ClearFocus();
    }

    private static void DumpPeers(UIElement root, string output, string label)
    {
        var rows = new List<object>();
        foreach (var element in Walk(root).OfType<UIElement>().Where(e => e.IsVisible))
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(element);
            if (peer == null) continue;
            rows.Add(new { Type = element.GetType().Name, Name = peer.GetName(), ControlType = peer.GetAutomationControlType().ToString(),
                KeyboardFocusable = peer.IsKeyboardFocusable(), RangeValue = peer.GetPattern(PatternInterface.RangeValue) != null });
        }
        File.WriteAllText(Path.Combine(output, label + "-automation-peers.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async System.Threading.Tasks.Task TestTabsAsync(MainWindow window, string output, string label)
    {
        var rows = new List<object>();
        var handle = new WindowInteropHelper(window).Handle;
        var first = (ToggleButton)window.FindName("LimitsToggle");
        window.Activate();
        SetForegroundWindow(handle);
        await System.Threading.Tasks.Task.Delay(100);
        Keyboard.Focus(first);
        for (var i = 0; i < 64; i++)
        {
            if (GetForegroundWindow() != handle)
            {
                rows.Add(new { Blocked = "Preview is not foreground; no input sent." });
                break;
            }
            if (Keyboard.FocusedElement is UIElement current)
            {
                var peer = UIElementAutomationPeer.CreatePeerForElement(current);
                rows.Add(new { Index = i, Type = current.GetType().Name, Name = peer?.GetName(), Task = ((current as FrameworkElement)?.DataContext as DownloadItemViewModel)?.DisplayTitle });
            }
            var inputs = new[]
            {
                new NativeInput { Type = 1, Keyboard = new KeyboardInput { Vk = 9 } },
                new NativeInput { Type = 1, Keyboard = new KeyboardInput { Vk = 9, Flags = 2 } }
            };
            if (SendInput(2, inputs, Marshal.SizeOf<NativeInput>()) != 2) throw new InvalidOperationException("SendInput did not send both Tab events.");
            await System.Threading.Tasks.Task.Delay(100);
            if (ReferenceEquals(Keyboard.FocusedElement, first)) break;
        }
        File.WriteAllText(Path.Combine(output, label + "-native-tab-input.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        Keyboard.ClearFocus();
    }

    private static async System.Threading.Tasks.Task TestNativeControlsAsync(MainWindow window, string output)
    {
        var rows = new List<object>();
        var buttons = Walk(window).OfType<Button>().Where(b => b.IsVisible).ToList();
        var pause = buttons.First(b => AutomationProperties.GetName(b) == "Pause");
        var task = (DownloadItemViewModel)pause.DataContext;
        pause.Focus();
        pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, pause));
        pause.Command.Execute(pause.CommandParameter);
        await System.Threading.Tasks.Task.Delay(200);
        rows.Add(new { Check = "Synthetic Pause replaces focused action", WindowActive = window.IsActive, Status = task.Task.Status.ToString(), Focus = (Keyboard.FocusedElement is UIElement f ? UIElementAutomationPeer.CreatePeerForElement(f)?.GetName() : null) });
        task.Task.Status = DownloadStatus.Downloading;
        window.UpdateLayout();
        var settings = buttons.First(b => AutomationProperties.GetName(b) == "Settings and tools");
        settings.Focus();
        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, settings));
        await System.Threading.Tasks.Task.Delay(100);
        if (OpenedMenu != null) OpenedMenu.IsOpen = false;
        await System.Threading.Tasks.Task.Delay(100);
        rows.Add(new { Check = "Programmatic settings dismissal", FocusRestored = ReferenceEquals(Keyboard.FocusedElement, settings) });

        var details = buttons.First(b => AutomationProperties.GetName(b) == "Show details");
        var error = (DownloadItemViewModel)details.DataContext;
        details.Focus();
        details.Command.Execute(details.CommandParameter);
        window.UpdateLayout();
        rows.Add(new { Check = "Details expand/collapse", Expanded = error.IsErrorDetailsVisible, FocusRetained = ReferenceEquals(Keyboard.FocusedElement, details) });
        details.Command.Execute(details.CommandParameter);

        var bar = Walk(window).OfType<ProgressBar>().First();
        task.Task.CurrentPhase = "Converting Audio...";
        await System.Threading.Tasks.Task.Delay(200);
        bar.Tag = false; // Fixture-only injection, not a change to Windows settings.
        window.UpdateLayout();
        var sweep = (FrameworkElement)bar.Template.FindName("Sweep", bar);
        rows.Add(new { Check = "Reduced-motion template branch (injected Tag=false)", Sweep = sweep.Visibility.ToString(), Indeterminate = bar.IsIndeterminate });
        bar.ClearValue(FrameworkElement.TagProperty);
        task.Task.CurrentPhase = "Downloading video...";
        await System.Threading.Tasks.Task.Delay(200);
        File.WriteAllText(Path.Combine(output, "native-control-checks.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        ((ToggleButton)window.FindName("LimitsToggle")).IsChecked = true;
        window.UpdateLayout();
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { @"C:\Users\Malku\Downloads\Compressed\yt-downloader-pro\tests\YTDLPHost.UiChecks\bin\Debug\net8.0-windows10.0.17763.0\YTDLPHost.UiChecks.dll", "--uia", new WindowInteropHelper(window).Handle.ToInt64().ToString(), Environment.ProcessId.ToString(), Path.Combine(output, "external-uia-window.json") }) start.ArgumentList.Add(arg);
        using (var process = System.Diagnostics.Process.Start(start)!)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            File.WriteAllText(Path.Combine(output, "external-uia-result.txt"), $"Exit={process.ExitCode}\n{await stdout}\n{await stderr}");
        }
        ((ToggleButton)window.FindName("LimitsToggle")).IsChecked = false;
        Keyboard.ClearFocus();
    }

    private static Color Composite(Color foreground, Color background, double opacity)
    {
        var alpha = foreground.A / 255.0 * opacity;
        return Color.FromArgb(255, (byte)Math.Round(foreground.R * alpha + background.R * (1 - alpha)),
            (byte)Math.Round(foreground.G * alpha + background.G * (1 - alpha)),
            (byte)Math.Round(foreground.B * alpha + background.B * (1 - alpha)));
    }

    private static double Contrast(Color foreground, Color background)
    {
        static double Channel(byte value) { var c = value / 255.0; return c <= .04045 ? c / 12.92 : Math.Pow((c + .055) / 1.055, 2.4); }
        static double Luminance(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        var a = Luminance(foreground); var b = Luminance(background);
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    private static void LogBrushes(string output, string label)
    {
        File.AppendAllText(Path.Combine(output, "brush-diagnostics.txt"), label + "\n");
        foreach (var key in new[] { "BackgroundBrush", "SurfaceBrush", "TextPrimaryBrush", "AccentBrush" })
        {
            var brush = (SolidColorBrush)System.Windows.Application.Current.Resources[key];
            File.AppendAllText(Path.Combine(output, "brush-diagnostics.txt"), $"{key}={brush.Color}, frozen={brush.IsFrozen}\n");
        }
    }
}
