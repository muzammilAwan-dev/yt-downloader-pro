using Avalonia;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            // "--register-native-host" and "--unregister-native-host" do their job and exit; no window, no single-instance check.
            var command = HostCommandLine.Parse(args);
            if (command != HostCommand.None) return PlatformNativeHost.RunCommand(command);

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[CRITICAL] The Avalonia app stopped: {ex}");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
