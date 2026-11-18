using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace YTDLPHost.UI;

public partial class BrowserIntegrationWindow : Window
{
    /// <summary>For the designer only.</summary>
    public BrowserIntegrationWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public BrowserIntegrationWindow(BrowserIntegrationViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
