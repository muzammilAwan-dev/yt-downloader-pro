using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using YTDLPHost.Models;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Services
{
    /// <summary>
    /// What the window shows around the download list: the filter tabs and their counts, the cards that match the tab, the footer
    /// text and total speed, "Clear finished", and the Limits button text. It follows the download list and every card by itself
    /// (this is the logic main's WPF window keeps in its code-behind), so any UI only has to bind to it.
    /// All changes are applied on the UI thread, whichever thread changed a download.
    /// </summary>
    public sealed class QueueViewModel : ObservableObject
    {
        private readonly ObservableCollection<DownloadItemViewModel> _source;
        private readonly IUiDispatcher _ui;
        private readonly Func<string?> _statusText;
        private readonly Func<AppSettings> _settings;
        private readonly HashSet<DownloadItemViewModel> _watched = new();
        private int _refreshQueued;

        private QueueFilter _filter = QueueFilter.All;
        private QueueCounts _counts = QueueSummary.Count(Array.Empty<DownloadStatus>());
        private string _footerText = "Ready";
        private string? _footerSpeedText;

        public QueueViewModel(ObservableCollection<DownloadItemViewModel> source, IUiDispatcher ui, Func<string?> statusText, Func<AppSettings> settings)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _statusText = statusText ?? throw new ArgumentNullException(nameof(statusText));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            _source.CollectionChanged += (_, _) => Refresh();
            foreach (var item in _source) Watch(item);
            Refresh();
        }

        /// <summary>The cards that match the chosen tab, in the order of the download list.</summary>
        public ObservableCollection<DownloadItemViewModel> Visible { get; } = new();

        public QueueFilter Filter
        {
            get => _filter;
            set { if (SetProperty(ref _filter, value)) Refresh(); }
        }

        public QueueCounts Counts => _counts;
        public int AllCount => _counts.All;
        public int ActiveCount => _counts.Active;
        public int DoneCount => _counts.Done;
        public int FailedCount => _counts.Failed;

        /// <summary>Tabs appear once the list is long enough to need them.</summary>
        public bool ShowFilters => QueueSummary.ShowFilters(_counts.All);
        public bool CanClearFinished => QueueSummary.CanClearFinished(_counts);
        public bool HasAnyCards => _counts.All > 0;

        /// <summary>Shown in the middle of the list when the chosen tab has no cards.</summary>
        public bool IsFilterEmpty => _counts.All > 0 && Visible.Count == 0;
        public string FilterEmptyText => QueueSummary.EmptyFilterMessage(_filter);

        /// <summary>Accent while something is queued or running, muted when idle.</summary>
        public bool IsBusy => _counts.Active > 0;

        public string FooterText => _footerText;
        public string? FooterSpeedText => _footerSpeedText;
        public bool HasFooterSpeed => _footerSpeedText != null;

        public string LimitsLabel => QueueSummary.LimitsLabel(_settings().SpeedLimit);
        public string LimitsTooltip => QueueSummary.LimitsTooltip(_settings().MaxConcurrentDownloads, _settings().SpeedLimit);

        /// <summary>Recomputes everything. Safe to call from any thread; the work happens once on the UI thread however many calls arrive.</summary>
        public void Refresh()
        {
            if (System.Threading.Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
            _ui.Post(() =>
            {
                System.Threading.Interlocked.Exchange(ref _refreshQueued, 0);
                RefreshNow();
            });
        }

        private void RefreshNow()
        {
            foreach (var item in _source) Watch(item);
            _watched.RemoveWhere(item =>
            {
                if (_source.Contains(item)) return false;
                item.PropertyChanged -= OnItemChanged;
                return true;
            });

            _counts = QueueSummary.Count(_source.Select(d => d.Task.Status));

            // Tabs vanish when the list gets short again; do not stay on a tab nobody can reach.
            if (_filter != QueueFilter.All && !QueueSummary.ShowFilters(_counts.All)) { _filter = QueueFilter.All; OnPropertyChanged(nameof(Filter)); }

            SyncVisible();
            _footerText = QueueSummary.FooterText(_statusText(), _counts);
            _footerSpeedText = QueueSummary.TotalSpeedText(_source);

            foreach (var name in new[]
            {
                nameof(Counts), nameof(AllCount), nameof(ActiveCount), nameof(DoneCount), nameof(FailedCount), nameof(ShowFilters), nameof(CanClearFinished),
                nameof(HasAnyCards), nameof(IsFilterEmpty), nameof(FilterEmptyText), nameof(IsBusy), nameof(FooterText), nameof(FooterSpeedText),
                nameof(HasFooterSpeed), nameof(LimitsLabel), nameof(LimitsTooltip)
            }) OnPropertyChanged(name);
        }

        /// <summary>Only the total speed changes while bytes flow, so progress ticks do not rebuild the lists.</summary>
        private void RefreshSpeed()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _speedQueued, 1, 0) == 1) return;
            _ui.Post(() =>
            {
                System.Threading.Interlocked.Exchange(ref _speedQueued, 0);
                var speed = QueueSummary.TotalSpeedText(_source);
                if (speed == _footerSpeedText) return;
                _footerSpeedText = speed;
                OnPropertyChanged(nameof(FooterSpeedText));
                OnPropertyChanged(nameof(HasFooterSpeed));
            });
        }
        private int _speedQueued;

        private void SyncVisible()
        {
            var wanted = _source.Where(d => QueueSummary.Matches(_filter, d.Task.Status)).ToList();
            var wantedSet = new HashSet<DownloadItemViewModel>(wanted);

            for (int i = Visible.Count - 1; i >= 0; i--)
                if (!wantedSet.Contains(Visible[i])) Visible.RemoveAt(i);

            for (int i = 0; i < wanted.Count; i++)
            {
                if (i < Visible.Count && ReferenceEquals(Visible[i], wanted[i])) continue;
                int at = Visible.IndexOf(wanted[i]);
                if (at > i) Visible.Move(at, i);
                else Visible.Insert(i, wanted[i]);
            }
        }

        private void Watch(DownloadItemViewModel item)
        {
            if (_watched.Add(item)) item.PropertyChanged += OnItemChanged;
        }

        private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            // StatusLabel is raised last when a status changes: counts, tabs and the footer follow it. StatusLine changes on every progress
            // flush: only the speed total needs that.
            if (e.PropertyName == nameof(DownloadItemViewModel.StatusLabel)) Refresh();
            else if (e.PropertyName == nameof(DownloadItemViewModel.StatusLine)) RefreshSpeed();
        }
    }
}
