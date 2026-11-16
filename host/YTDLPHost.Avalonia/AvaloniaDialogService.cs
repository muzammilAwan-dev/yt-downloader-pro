using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using YTDLPHost.Services;

namespace YTDLPHost.UI;

/// <summary>
/// A message box. Avalonia has none of its own, so this builds a small window in code. Unlike WPF's MessageBox it does not block the caller
/// (nobody uses a result); it is modal to the main window when that is showing.
/// </summary>
public sealed class AvaloniaDialogService : IDialogService
{
    private readonly Func<Window?> _owner;

    public AvaloniaDialogService(Func<Window?> owner) => _owner = owner;

    public void Show(string title, string message, DialogKind kind = DialogKind.Info) =>
        Dispatcher.UIThread.Post(() =>
        {
            var ok = new Button { Content = "OK", MinWidth = 88, HorizontalAlignment = HorizontalAlignment.Right };
            var dialog = new Window
            {
                Title = kind == DialogKind.Info ? title : $"{title} ({kind})",
                Width = 460,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                Content = new StackPanel
                {
                    Margin = new Thickness(24),
                    Spacing = 20,
                    Children = { new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap }, ok }
                }
            };
            ok.Click += (_, _) => dialog.Close();

            var owner = _owner();
            if (owner is { IsVisible: true })
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                _ = dialog.ShowDialog(owner);
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                dialog.Show();
            }
        });
}
