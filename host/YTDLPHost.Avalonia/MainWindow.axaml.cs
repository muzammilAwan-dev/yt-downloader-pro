using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

public partial class MainWindow : Window
{
    /// <summary>Set by the app just before it shuts down; until then closing the window only hides it.</summary>
    public bool AllowClose { get; set; }

    /// <summary>For the designer only; the app uses the other constructor.</summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindow(PlatformServices services, Action exit) : this()
    {
        var paths = AppPaths.Current;   // proof that Core's per-OS folders work here
        this.FindControl<TextBlock>("SystemInfo")!.Text =
            $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), {RuntimeInformation.FrameworkDescription}\n" +
            $"Data folder: {paths.DataDir}\nLog folder: {paths.LogsDir}";

        var status = this.FindControl<TextBlock>("Status")!;
        status.Text = "Ready.";

        // Every button goes through the same PlatformServices the shared view model will use (step 4.3).
        this.FindControl<Button>("NotifyButton")!.Click += (_, _) =>
        {
            services.Notifier.NotifyDownloadComplete("skeleton-test.mp4");
            status.Text = "Notification sent. If you see none, check that notify-send (Linux) is installed; the log has details.";
        };
        this.FindControl<Button>("HideButton")!.Click += (_, _) => services.Lifetime.MinimizeMainWindow();
        this.FindControl<Button>("ExitButton")!.Click += (_, _) => exit();

        this.FindControl<Button>("CopyButton")!.Click += (_, _) =>
        {
            services.Clipboard.SetText("YT Downloader Pro clipboard test");
            status.Text = "Text copied: paste it somewhere to check (it reads \"YT Downloader Pro clipboard test\").";
        };
        this.FindControl<Button>("DialogButton")!.Click += (_, _) =>
        {
            services.Dialogs.Show("Dialog test", "This message comes from the dialog service.\nIt should be centred on this window.");
            status.Text = "Dialog requested.";
        };
        this.FindControl<Button>("FolderButton")!.Click += (_, _) =>
        {
            services.Shell.OpenFolder(paths.DataDir);
            status.Text = "Opening the data folder in the system file manager.";
        };
        this.FindControl<Button>("ThreadButton")!.Click += (_, _) =>
        {
            status.Text = "Waiting for a background thread...";
            Task.Run(() => services.Ui.Post(() => status.Text = "Thread test passed: this text was set from a background thread through the UI dispatcher."));
        };

        Closing += OnClosing;
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (AllowClose || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown) return;
        e.Cancel = true;   // close-to-tray: the app keeps running
        Hide();
    }
}
