using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

public partial class MainWindow : Window
{
    private readonly INotifier _notifier;

    /// <summary>Set by the app just before it shuts down; until then closing the window only hides it.</summary>
    public bool AllowClose { get; set; }

    public MainWindow() : this(new LoggingNotifier(), () => { })
    {
    }

    public MainWindow(INotifier notifier, Action exit)
    {
        _notifier = notifier;
        AvaloniaXamlLoader.Load(this);

        var paths = AppPaths.Current;   // proof that Core's per-OS folders work here
        this.FindControl<TextBlock>("SystemInfo")!.Text =
            $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), {RuntimeInformation.FrameworkDescription}\n" +
            $"Data folder: {paths.DataDir}\nLog folder: {paths.LogsDir}";

        var status = this.FindControl<TextBlock>("Status")!;
        status.Text = "Ready.";

        this.FindControl<Button>("NotifyButton")!.Click += (_, _) =>
        {
            _notifier.NotifyDownloadComplete("skeleton-test.mp4");
            status.Text = "Notification requested (for now it is only written to the log; real OS notifications come in step 4.2).";
        };
        this.FindControl<Button>("HideButton")!.Click += (_, _) => Hide();
        this.FindControl<Button>("ExitButton")!.Click += (_, _) => exit();

        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (AllowClose || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown) return;
        e.Cancel = true;   // close-to-tray: the app keeps running
        Hide();
    }
}
