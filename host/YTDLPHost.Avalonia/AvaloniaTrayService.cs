using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>
/// The tray icon on Windows, macOS and Linux through Avalonia's own <see cref="TrayIcon"/>. On Linux it needs a desktop that shows
/// StatusNotifier icons (KDE, Xfce with a plugin, GNOME only with an AppIndicator extension): the fallback is step 4.5.
/// </summary>
public sealed class AvaloniaTrayService : ITrayService
{
    private readonly Application _app;
    private TrayIcon? _icon;

    public AvaloniaTrayService(Application app) => _app = app;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        using var stream = AssetLoader.Open(new Uri("avares://YTDLPHost.Avalonia/Assets/icon.png"));

        var show = new NativeMenuItem("Show");
        show.Click += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new NativeMenu();
        menu.Add(show);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);

        _icon = new TrayIcon { Icon = new WindowIcon(stream), ToolTipText = "YT Downloader Pro", Menu = menu };
        _icon.Clicked += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        TrayIcon.SetIcons(_app, new TrayIcons { _icon });
    }

    public void UpdateTooltip(string text)
    {
        if (_icon != null) _icon.ToolTipText = text;
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }
}
