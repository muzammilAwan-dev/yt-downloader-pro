using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using YTDLPHost.Models;
using YTDLPHost.Services;
using YTDLPHost.ViewModels;

namespace YTDLPHost.UI;

/// <summary>
/// The main window (step 4.3): the AXAML is bound to the shared <see cref="MainViewModel"/>; this file only holds what a binding cannot do
/// (folder picker, the settings menu, close-to-tray).
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Set by the app just before it shuts down; until then closing the window only hides it.</summary>
    public bool AllowClose { get; set; }

    /// <summary>For the designer only; the app uses the other constructor.</summary>
    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public MainWindow(MainViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Closing += OnClosing;

        // The view model decides what a browser download does from "is the window on screen" (NativeAnnouncePolicy), so keep it true to life.
        // A minimised window is as invisible to the user as a hidden one (the case of a desktop without a tray, step 4.5).
        PropertyChanged += (_, e) =>
        {
            if ((e.Property == IsVisibleProperty || e.Property == WindowStateProperty) && DataContext is MainViewModel vm)
                vm.IsWindowVisible = IsVisible && WindowState != WindowState.Minimized;
        };

        // The toggle item's text shows the current state, so it is refreshed each time the menu opens.
        if (this.FindControl<Button>("SettingsButton")?.Flyout is MenuFlyout menu)
        {
            menu.Opening += (_, _) => RefreshSettingsMenu(menu);
        }
    }

    private void RefreshSettingsMenu(MenuFlyout menu)
    {
        if (DataContext is not MainViewModel vm) return;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (Equals(item.Tag, "browser-downloads"))
            {
                item.Header = vm.ShowWindowOnNativeDownload
                    ? "Browser downloads: show this window (on)"
                    : "Browser downloads: notify only (off)";
                ToolTip.SetTip(item, "Choose what happens when a download starts from the browser while this window is hidden: a short notification (default) or bringing this window to the front. Click to switch.");
            }
            else if (Equals(item.Tag, "tray-info"))
            {
                item.IsVisible = !TraySupport.CanHideToTray;
            }
            else if (Equals(item.Tag, "update-engine"))
            {
                ToolTip.SetTip(item, EngineUpdater.TroubleshootingHint);
            }
            else if (item.Tag is string tag && tag.StartsWith("theme-", StringComparison.Ordinal))
            {
                item.IsChecked = tag == "theme-" + vm.Theme;
            }
        }
    }

    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is MenuItem { Tag: string tag } && tag.StartsWith("theme-", StringComparison.Ordinal))
            vm.SetTheme(ThemePalette.Parse(tag.Substring("theme-".Length)));
    }

    private void OnFilterTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is Control { Tag: string tag } && Enum.TryParse<QueueFilter>(tag, out var filter))
            vm.Queue.Filter = filter;
    }

    private void OnToastActionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.Toast.RunAction();
    }

    // Right-click menu of a card: the same actions as its buttons (a context menu is not part of the list, so it cannot bind to the window's commands).
    private void RunCardCommand(object? sender, Func<MainViewModel, System.Windows.Input.ICommand> command)
    {
        if (DataContext is MainViewModel vm && sender is Control { DataContext: DownloadItemViewModel item } && command(vm).CanExecute(item))
            command(vm).Execute(item);
    }

    private void OnCardPlayClick(object? sender, RoutedEventArgs e) => RunCardCommand(sender, vm => vm.PlayFileCommand);
    private void OnCardFolderClick(object? sender, RoutedEventArgs e) => RunCardCommand(sender, vm => vm.OpenFolderCommand);
    private void OnCardResumeClick(object? sender, RoutedEventArgs e) => RunCardCommand(sender, vm => vm.ResumeDownloadCommand);
    private void OnCardPauseClick(object? sender, RoutedEventArgs e) => RunCardCommand(sender, vm => vm.PauseDownloadCommand);
    private void OnCardCancelClick(object? sender, RoutedEventArgs e) => RunCardCommand(sender, vm => vm.CancelDownloadCommand);
    private void OnCardRemoveClick(object? sender, RoutedEventArgs e) => RunCardCommand(sender, vm => vm.RemoveDownloadCommand);
    private void OnCardLogClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: DownloadItemViewModel item }) item.ToggleLogCommand.Execute(null);
    }

    private void OnUpdateEngineClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.UpdateEngineCommand.Execute(null);
    }

    private void OnToggleShowWindowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.SetShowWindowOnNativeDownload(!vm.ShowWindowOnNativeDownload);
    }

    private async void OnBrowserIntegrationClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var panel = new BrowserIntegrationWindow(new BrowserIntegrationViewModel(PlatformNativeHost.Check, PlatformNativeHost.EnsureRegistered));
            await panel.ShowDialog(this);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[UI] The Browser integration panel failed: {ex.Message}");
        }
    }

    private void OnExitClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.ExitCommand.Execute(null);
    }

    private async void OnBrowseDownloadFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        try
        {
            var options = new FolderPickerOpenOptions
            {
                Title = "Choose where downloads from the browser are saved",
                AllowMultiple = false
            };

            string current = vm.Settings.DownloadFolder;
            if (!string.IsNullOrWhiteSpace(current) && Directory.Exists(current))
            {
                options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(current);
            }

            var folders = await StorageProvider.OpenFolderPickerAsync(options);
            string? path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrEmpty(path)) vm.SetDownloadFolderText(path);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"[UI] The folder picker failed: {ex.Message}");
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (AllowClose || e.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown) return;
        e.Cancel = true;   // the app keeps running: to the tray, or, where the desktop has none, to the task bar
        if (TraySupport.CanHideToTray) Hide();
        else WindowState = WindowState.Minimized;
    }
}
