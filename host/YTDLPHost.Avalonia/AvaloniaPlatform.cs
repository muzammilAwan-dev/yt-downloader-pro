using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>The one place that decides which implementations the Avalonia app uses (the counterpart of WpfPlatform), so the shared view model never sees a toolkit.</summary>
public static class AvaloniaPlatform
{
    private const string IconAsset = "avares://YTDLPHost.Avalonia/Assets/icon.png";

    /// <param name="mainWindow">The main window if it exists yet (it is created on first use).</param>
    /// <param name="shutdown">Ends the app.</param>
    public static PlatformServices Create(Application app, Func<Window?> mainWindow, Action shutdown)
    {
        var os = AppPathsFactory.CurrentOs();
        return new PlatformServices(
            new AvaloniaUiDispatcher(),
            new AvaloniaDialogService(mainWindow),
            new AvaloniaAppLifetime(mainWindow, shutdown),
            new AvaloniaClipboardService(mainWindow),
            new AvaloniaTrayService(app),
            new SystemNotifier(os, iconPath: EnsureIconFile()),
            os == HostOs.Windows ? new ShellExecuteActions() : new UnixShellActions(os));
    }

    /// <summary>The logo as a real file, because the OS notification systems take a path, not an app resource. Null when it cannot be written.</summary>
    private static string? EnsureIconFile()
    {
        try
        {
            string path = Path.Combine(AppPaths.Current.DataDir, "notification-icon.png");
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(AppPaths.Current.DataDir);
                using var source = AssetLoader.Open(new Uri(IconAsset));
                using var target = File.Create(path);
                source.CopyTo(target);
            }
            return path;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[NOTIFY] Could not write the notification icon: {ex.Message}");
            return null;
        }
    }
}
