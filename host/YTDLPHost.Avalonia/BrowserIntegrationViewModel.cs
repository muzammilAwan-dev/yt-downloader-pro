using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YTDLPHost.Native;

namespace YTDLPHost.UI;

/// <summary>
/// Step 4.4: the "Browser integration" panel. Shows, for each browser, whether it starts this install's shim, and Repair re-links all of them
/// (the same call the app makes at every start). The two functions are passed in so the panel can be rendered with sample data.
/// Written without the source generators: the Avalonia project does not get the analyzers of the toolkit package from Core.
/// </summary>
public sealed class BrowserIntegrationViewModel : ObservableObject
{
    private readonly Func<NativeHostReport> _check;
    private readonly Func<IReadOnlyList<string>> _repair;
    private string _summary = "";
    private string _repairLog = "";
    private bool _canRepair;

    public ObservableCollection<BrowserLinkStatus> Browsers { get; } = new();

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }

    /// <summary>What the last Repair did, one line per browser; empty before the first Repair.</summary>
    public string RepairLog { get => _repairLog; private set { if (SetProperty(ref _repairLog, value)) OnPropertyChanged(nameof(HasRepairLog)); } }

    public bool HasRepairLog => _repairLog.Length > 0;

    public bool CanRepair { get => _canRepair; private set { if (SetProperty(ref _canRepair, value)) RepairCommand.NotifyCanExecuteChanged(); } }

    public RelayCommand RepairCommand { get; }

    public BrowserIntegrationViewModel(Func<NativeHostReport> check, Func<IReadOnlyList<string>> repair)
    {
        _check = check;
        _repair = repair;
        RepairCommand = new RelayCommand(Repair, () => CanRepair);
        Refresh();
    }

    public void Refresh()
    {
        var report = _check();
        Browsers.Clear();
        foreach (var status in report.Browsers) Browsers.Add(status);
        Summary = report.Summary;
        CanRepair = report.CanRepair;
    }

    private void Repair()
    {
        RepairLog = string.Join(Environment.NewLine, _repair());
        Refresh();
    }
}
