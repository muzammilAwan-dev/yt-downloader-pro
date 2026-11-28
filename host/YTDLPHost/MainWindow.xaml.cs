using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using YTDLPHost.Models;
using YTDLPHost.ViewModels;
using YTDLPHost.Services;

// EXPLICIT ALIASES: Resolves the ambiguous references between WPF and WinForms
using Button = System.Windows.Controls.Button;
using ListBox = System.Windows.Controls.ListBox;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Separator = System.Windows.Controls.Separator;
using MessageBox = System.Windows.MessageBox;

namespace YTDLPHost
{
    /// <summary>
    /// Interaction logic for the main application window.
    /// </summary>
    public partial class MainWindow : Window
    {
        private WindowMaterialService? _windowMaterial;

        private enum QueueFilter { All, Active, Done, Failed }

        private QueueFilter _filter = QueueFilter.All;
        private MainViewModel? _vm;
        private ICollectionView? _view;
        private readonly HashSet<DownloadItemViewModel> _hooked = new();
        private bool _refreshQueued;

        private readonly DispatcherTimer _toastTimer = new();

        // Total speed in the footer, refreshed once a second
        private readonly DispatcherTimer _speedTimer = new() { Interval = TimeSpan.FromSeconds(1) };
        private Action? _toastAction;

        public MainWindow()
        {
            InitializeComponent();
            FitToWorkArea();
            Loaded += OnLoaded;
            _toastTimer.Tick += (s, e) => HideToast();

            _speedTimer.Tick += OnSpeedTick;
        }

        /// <summary>
        /// The window is designed for 980 x 700, but never bigger than the screen allows: on a laptop (for example 1366 x 768 at 125 %) that size
        /// would run off the bottom. WindowFit (Core, tested) shrinks it to fit the work area (the screen minus the taskbar); the window then opens centred.
        /// </summary>
        private void FitToWorkArea()
        {
            var area = SystemParameters.WorkArea; // device-independent units, taskbar excluded
            var fit = WindowFit.Compute(Width, Height, MinWidth, MinHeight, area.Width, area.Height, 1.0);
            MinWidth = fit.MinWidth;
            MinHeight = fit.MinHeight;
            Width = fit.Width;
            Height = fit.Height;
        }

        /// <summary>
        /// Retains the native frame and applies optional Mica with an opaque, themed fallback.
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _windowMaterial = new WindowMaterialService(this);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_vm != null || DataContext is not MainViewModel vm) return;

            _vm = vm;
            vm.RequestScrollToItem += OnRequestScrollToItem;
            vm.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(MainViewModel.StatusText))
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateFooter));
            };

            // Filter tabs work on the default view of the same collection the list is bound to, so no ViewModel change is needed.
            _view = CollectionViewSource.GetDefaultView(vm.Downloads);
            _view.Filter = FilterDownload;

            // Live filtering re-checks a card only when its status changes. Calling Refresh() instead rebuilds every card in the
            // list, which made the whole queue flicker and reset the scroll position each time a download changed state.
            if (_view is ICollectionViewLiveShaping live && live.CanChangeLiveFiltering)
            {
                live.LiveFilteringProperties.Add(nameof(DownloadItemViewModel.StatusLabel));
                live.IsLiveFiltering = true;
            }
            vm.Downloads.CollectionChanged += OnDownloadsChanged;
            foreach (var item in vm.Downloads.ToList()) Hook(item);

            RefreshQueueView();
            UpdateLimitsSummary();
            _speedTimer.Start();
        }

        private void OnRequestScrollToItem(object? sender, DownloadItemViewModel item)
        {
            // The ListBox will automatically show the item due to selection behavior
        }

        // ---------------------------------------------------------------- Queue filter + counts

        private bool FilterDownload(object obj)
        {
            if (obj is not DownloadItemViewModel d) return true;
            var status = d.Task.Status;
            return _filter switch
            {
                QueueFilter.Active => status == DownloadStatus.Queued || status == DownloadStatus.Downloading || status == DownloadStatus.Paused,
                QueueFilter.Done => status == DownloadStatus.Completed,
                QueueFilter.Failed => status == DownloadStatus.Error,
                _ => true
            };
        }

        private void OnFilterChecked(object sender, RoutedEventArgs e)
        {
            if (ReferenceEquals(sender, ActiveTab)) _filter = QueueFilter.Active;
            else if (ReferenceEquals(sender, DoneTab)) _filter = QueueFilter.Done;
            else if (ReferenceEquals(sender, FailedTab)) _filter = QueueFilter.Failed;
            else _filter = QueueFilter.All;

            if (_view == null) return; // fires once while the XAML loads, before the window is ready
            _view.Refresh();
            RefreshQueueView();
        }

        private void OnDownloadsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset && _vm != null)
            {
                foreach (var item in _vm.Downloads.ToList()) Hook(item);
            }
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<DownloadItemViewModel>()) Unhook(item);
            }
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<DownloadItemViewModel>()) Hook(item);
            }
            ScheduleRefresh();
        }

        private void Hook(DownloadItemViewModel item)
        {
            if (_hooked.Add(item)) item.PropertyChanged += OnItemPropertyChanged;
        }

        private void Unhook(DownloadItemViewModel item)
        {
            if (_hooked.Remove(item)) item.PropertyChanged -= OnItemPropertyChanged;
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // StatusLabel is raised once per status change (see DownloadItemViewModel.RefreshStateProperties).
            if (e.PropertyName == nameof(DownloadItemViewModel.StatusLabel)) ScheduleRefresh();
        }

        // Status changes can arrive from background threads and in bursts, so coalesce them into one UI-thread refresh.
        private void ScheduleRefresh()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                _refreshQueued = false;
                RefreshQueueView();
            }));
        }

        private void RefreshQueueView()
        {
            if (_vm == null) return;

            int all = _vm.Downloads.Count, active = 0, done = 0, failed = 0, cancelled = 0;
            _downloadingCount = 0; _waitingCount = 0; _pausedCount = 0;
            foreach (var d in _vm.Downloads)
            {
                switch (d.Task.Status)
                {
                    case DownloadStatus.Queued:
                        active++; _waitingCount++;
                        break;
                    case DownloadStatus.Downloading:
                        active++; _downloadingCount++;
                        break;
                    case DownloadStatus.Paused:
                        active++; _pausedCount++;
                        break;
                    case DownloadStatus.Completed:
                        done++;
                        break;
                    case DownloadStatus.Error:
                        failed++;
                        break;
                    case DownloadStatus.Cancelled:
                        cancelled++;
                        break;
                }
            }

            // Filters only earn their place once there is a longer list to sort through; with a few cards they are just noise.
            bool showFilters = all >= 4;
            FilterTrack.Visibility = showFilters ? Visibility.Visible : Visibility.Collapsed;
            if (!showFilters && _filter != QueueFilter.All) AllTab.IsChecked = true;

            ClearFinishedButton.Visibility = done + cancelled > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateFooter();

            AllTab.Tag = all.ToString();
            ActiveTab.Tag = active.ToString();
            DoneTab.Tag = done.ToString();
            FailedTab.Tag = failed.ToString();

            bool filteredEmpty = all > 0 && _view != null && _view.IsEmpty;
            FilterEmptyText.Visibility = filteredEmpty ? Visibility.Visible : Visibility.Collapsed;
            if (filteredEmpty)
            {
                FilterEmptyText.Text = _filter switch
                {
                    QueueFilter.Active => "Nothing is downloading right now.",
                    QueueFilter.Done => "No completed downloads yet.",
                    QueueFilter.Failed => "No failed downloads. Nice.",
                    _ => ""
                };
            }
        }

        // ---------------------------------------------------------------- Total speed (footer)

        private void OnSpeedTick(object? sender, EventArgs e)
        {
            if (_vm == null) return;

            double total = 0;
            foreach (var d in _vm.Downloads)
            {
                // Merging / converting moves no bytes, so it adds no speed.
                if (d.Task.Status == DownloadStatus.Downloading && !d.IsProcessing) total += RateMath.ParseRate(d.Task.Speed);
            }

            if (total > 0)
            {
                var (value, unit) = RateMath.Format(total);
                FooterSpeedText.Text = $"\u2193 {value} {unit}";
                FooterSpeedText.Visibility = Visibility.Visible;
            }
            else
            {
                FooterSpeedText.Visibility = Visibility.Collapsed;
            }
        }

        // ---------------------------------------------------------------- Footer + clearing

        private int _downloadingCount, _waitingCount, _pausedCount;

        /// <summary>
        /// The footer says what the queue is doing right now ("1 downloading · 2 waiting") instead of the last event
        /// ("Added: Fetching video..."). Real messages from the app (engine updates, network errors) still show as they are.
        /// </summary>
        private void UpdateFooter()
        {
            if (_vm == null) return;

            string message = _vm.StatusText ?? "";
            bool generic = message.Length == 0 || message == "Ready" || message == "All downloads complete" ||
                           message.StartsWith("Added:", StringComparison.Ordinal) || message.StartsWith("Downloading:", StringComparison.Ordinal);

            if (generic)
            {
                var parts = new List<string>();
                if (_downloadingCount > 0) parts.Add($"{_downloadingCount} downloading");
                if (_waitingCount > 0) parts.Add($"{_waitingCount} waiting");
                if (_pausedCount > 0) parts.Add($"{_pausedCount} paused");

                message = parts.Count > 0
                    ? string.Join(" \u00B7 ", parts)
                    : (_vm.Downloads.Count == 0 ? "Ready" : "All downloads finished");
            }

            FooterText.Text = message;
        }

        private void OnClearFinishedClick(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var finished = _vm.Downloads
                .Where(d => d.Task.Status == DownloadStatus.Completed || d.Task.Status == DownloadStatus.Cancelled)
                .ToList();
            foreach (var item in finished) _vm.RemoveDownloadCommand.Execute(item);
        }

        // ---------------------------------------------------------------- Limits panel

        private void UpdateLimitsSummary()
        {
            if (_vm == null) return;

            string speed = (_vm.Settings.SpeedLimit ?? "0").Trim();
            bool capped = speed.Length > 0 && speed != "0";

            // A speed cap is the one setting that quietly changes behaviour, so it is the one worth showing on the button.
            LimitsSummaryText.Text = capped ? $"Limit {speed.ToUpperInvariant()}/s" : "Limits";
            LimitsToggle.ToolTip = $"{_vm.Settings.MaxConcurrentDownloads} downloads at once \u00B7 {(capped ? speed.ToUpperInvariant() + "/s speed limit" : "no speed limit")}";
        }

        private void OnSaveLimitsClick(object sender, RoutedEventArgs e)
        {
            // The Save command runs right after this handler (and may clamp or reject values), so read the settings afterwards.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(UpdateLimitsSummary));
        }

        // ---------------------------------------------------------------- Toast (replaces light-themed message boxes)

        private void ShowToast(string message, string? actionLabel = null, Action? action = null)
        {
            _toastTimer.Stop();
            ToastText.Text = message;
            _toastAction = action;
            ToastActionButton.Content = actionLabel == null ? null : new TextBlock { Text = actionLabel };
            ToastActionButton.Visibility = actionLabel == null ? Visibility.Collapsed : Visibility.Visible;

            ToastHost.Visibility = Visibility.Visible;
            if (SystemParameters.ClientAreaAnimation)
                ToastHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            else
            {
                ToastHost.BeginAnimation(OpacityProperty, null);
                ToastHost.Opacity = 1;
            }

            _toastTimer.Interval = TimeSpan.FromSeconds(actionLabel == null ? 3.5 : 7);
            _toastTimer.Start();
        }

        private void HideToast()
        {
            _toastTimer.Stop();
            if (!SystemParameters.ClientAreaAnimation)
            {
                ToastHost.BeginAnimation(OpacityProperty, null);
                ToastHost.Opacity = 0;
                ToastHost.Visibility = Visibility.Collapsed;
                return;
            }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            fade.Completed += (s, e) =>
            {
                if (ToastHost.Opacity == 0) ToastHost.Visibility = Visibility.Collapsed;
            };
            ToastHost.BeginAnimation(OpacityProperty, fade);
        }

        private void OnToastActionClick(object sender, RoutedEventArgs e)
        {
            var action = _toastAction;
            _toastAction = null;
            HideToast();
            action?.Invoke();
        }

        // ---------------------------------------------------------------- Cards

        // Loaded is a bubbling event, so a child (the log box, the progress bar...) loading used to restart the whole card's fade.
        // Now only the card's own Loaded counts, each card animates once, and Windows' "show animations" setting is respected.
        private void OnCardLoaded(object sender, RoutedEventArgs e)
        {
            if (!ReferenceEquals(e.OriginalSource, sender) || sender is not FrameworkElement card) return;
            if (card.Tag is true) return;
            card.Tag = true;
            card.AddHandler(Button.ClickEvent, new RoutedEventHandler(OnCardActionClicked));

            if (!SystemParameters.ClientAreaAnimation) return;

            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }

        // Tab traverses every row. Bring only the newly focused control into view,
        // including logs whose live caret updates intentionally suppress BringIntoView.
        private void OnQueueKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not ListBox list || e.NewFocus is not FrameworkElement target) return;
            list.UpdateLayout();
            var viewer = FindVisualChildren<ScrollViewer>(list).FirstOrDefault();
            if (viewer == null || !viewer.IsAncestorOf(target)) return;
            var bounds = target.TransformToAncestor(viewer).TransformBounds(new Rect(target.RenderSize));
            const double inset = 6;
            if (bounds.Top < inset)
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + bounds.Top - inset);
            else if (bounds.Bottom > viewer.ViewportHeight - inset)
                viewer.ScrollToVerticalOffset(viewer.VerticalOffset + Math.Min(bounds.Top - inset, bounds.Bottom - viewer.ViewportHeight + inset));
        }

        private void OnCardActionClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement card || e.OriginalSource is not Button button ||
                button.Command == null || !ReferenceEquals(Keyboard.FocusedElement, button)) return;

            // Pause/resume/retry can replace the focused action. Keep focus in the
            // same task, but never steal it from a user who has already moved on.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (!IsActive || (button.IsVisible && button.IsEnabled)) return;
                var focused = Keyboard.FocusedElement as DependencyObject;
                if (focused != null && !ReferenceEquals(focused, this) && !ReferenceEquals(focused, button) && !card.IsAncestorOf(focused)) return;
                var next = FindVisualChildren<Button>(card).FirstOrDefault(b => b.IsVisible && b.IsEnabled && b.Focusable);
                if (next != null) next.Focus();
                else LimitsToggle.Focus();
            }));
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match) yield return match;
                foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
            }
        }

        private void RestoreFocusAfterMenu(ContextMenu menu, FrameworkElement anchor)
        {
            var previous = Keyboard.FocusedElement as UIElement;
            menu.Closed += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!IsActive) return;
                var focused = Keyboard.FocusedElement as DependencyObject;
                if (focused != null && !ReferenceEquals(focused, this) && !menu.IsAncestorOf(focused)) return;
                if (anchor.IsVisible && anchor.IsEnabled && anchor.Focusable) anchor.Focus();
                else if (previous != null && previous.IsVisible && previous.IsEnabled) previous.Focus();
                else LimitsToggle.Focus();
            }));
        }

        protected override void OnClosed(EventArgs e)
        {
            _speedTimer.Stop();
            _toastTimer.Stop();
            _windowMaterial?.Dispose();
            base.OnClosed(e);
        }

        // ---------------------------------------------------------------- Card menu

        private void OnCardRightClick(object sender, MouseButtonEventArgs e)
        {
            // Right-click inside the log keeps the normal copy / select-all menu.
            if (IsInsideTextBox(e.OriginalSource as DependencyObject)) return;

            if (sender is FrameworkElement card && card.DataContext is DownloadItemViewModel item)
            {
                ShowCardMenu(item, card, atMouse: true);
                e.Handled = true;
            }
        }

        private static bool IsInsideTextBox(DependencyObject? node)
        {
            while (node != null)
            {
                if (node is System.Windows.Controls.TextBox) return true;
                node = node is System.Windows.Media.Visual
                    ? System.Windows.Media.VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }

        private static void RunCommand(System.Windows.Input.ICommand? command, object? parameter)
        {
            if (command != null && command.CanExecute(parameter)) command.Execute(parameter);
        }

        /// <summary>
        /// One small menu holds every secondary action, so a card shows a single main button instead of four icons.
        /// Items depend on the download's state; the destructive one is last and red.
        /// </summary>
        private void ShowCardMenu(DownloadItemViewModel item, FrameworkElement anchor, bool atMouse)
        {
            if (_vm == null) return;

            var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenu"), MinWidth = 210 };
            var status = item.Task.Status;

            if (status == DownloadStatus.Completed)
            {
                menu.Items.Add(MakeMenuItem("Icon.Play", "Play", null, (s, a) => RunCommand(_vm.PlayFileCommand, item)));
                menu.Items.Add(MakeMenuItem("Icon.FolderOpen", "Show in folder", null, (s, a) => RunCommand(_vm.OpenFolderCommand, item)));
            }

            menu.Items.Add(MakeMenuItem("Icon.Log", item.IsLogVisible ? "Hide log" : "Show log", null, (s, a) =>
            {
                RunCommand(item.ToggleLogCommand, null);
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => anchor.BringIntoView()));
            }));

            bool canCancel = item.CanBeCancelled;
            bool canRemove = item.CanBeRemoved;
            if (canCancel || canRemove)
            {
                menu.Items.Add(new Separator { Style = (Style)FindResource("DarkMenuSeparator") });
            }
            if (canCancel)
            {
                menu.Items.Add(MakeMenuItem("Icon.CancelCircle", "Cancel download", "Stops it and deletes the partial files.", (s, a) => RunCommand(_vm.CancelDownloadCommand, item), danger: true));
            }
            if (canRemove)
            {
                menu.Items.Add(MakeMenuItem("Icon.Cancel", "Remove from list", "The file, if any, stays on disk.", (s, a) => RunCommand(_vm.RemoveDownloadCommand, item)));
            }

            menu.PlacementTarget = anchor;
            if (atMouse)
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            }
            else
            {
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
                menu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => new[]
                {
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(
                        new System.Windows.Point(targetSize.Width - popupSize.Width, targetSize.Height + 4),
                        System.Windows.Controls.Primitives.PopupPrimaryAxis.None)
                };
            }
            RestoreFocusAfterMenu(menu, anchor);
            menu.IsOpen = true;
        }

        // ---------------------------------------------------------------- Log panel behaviour

        // Opening a log makes the card much taller, often past the bottom of the window. Bring the whole card into view.
        private void OnLogToggleClick(object sender, RoutedEventArgs e)
        {
            if (sender is not DependencyObject start) return;

            var cardStyle = (Style)FindResource("CardStyle");
            DependencyObject? current = System.Windows.Media.VisualTreeHelper.GetParent(start);
            while (current != null && !(current is Border border && ReferenceEquals(border.Style, cardStyle)))
            {
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }

            if (current is FrameworkElement card)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => card.BringIntoView()));
            }
        }

        // Replacing the log text moves the text box's caret, and a text box asks every scroll container above it to bring the
        // caret into view. That is what yanked the whole queue back to the top while a log was live. The log scrolls itself.
        private void OnLogRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e) => e.Handled = true;

        // The log text is replaced as a whole every ~250 ms. Replacing text resets a TextBox's scroll position, which is what made
        // the log jump back to the top while you were reading it. So: remember where the view was before the change, let the
        // layout catch up, then put it back. If you were at the bottom, keep following the newest line; if you scrolled up to read, stay put.
        private void OnLogTextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox tb) return;

            bool following = tb.ExtentHeight <= tb.ViewportHeight + 1 ||
                             tb.VerticalOffset + tb.ViewportHeight >= tb.ExtentHeight - 3;
            double offset = tb.VerticalOffset;

            tb.UpdateLayout();

            if (following) tb.ScrollToEnd();
            else tb.ScrollToVerticalOffset(offset);
        }

        // A TextBox swallows every mouse-wheel tick, even when it cannot scroll any further, which trapped the pointer
        // (and the whole queue) while it hovered over an open log. At the top/bottom edge, hand the wheel to the queue.
        private void OnLogPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not System.Windows.Controls.TextBox tb) return;

            bool fits = tb.ExtentHeight <= tb.ViewportHeight + 1;
            bool atTop = tb.VerticalOffset <= 0.5;
            bool atBottom = tb.VerticalOffset + tb.ViewportHeight >= tb.ExtentHeight - 1;

            if (fits || (e.Delta > 0 && atTop) || (e.Delta < 0 && atBottom))
            {
                var outer = FindParentScrollViewer(tb);
                if (outer != null)
                {
                    e.Handled = true;
                    outer.ScrollToVerticalOffset(outer.VerticalOffset - e.Delta * 0.4);
                }
            }
        }

        private static ScrollViewer? FindParentScrollViewer(DependencyObject child)
        {
            DependencyObject? current = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (current != null)
            {
                if (current is ScrollViewer sv) return sv;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // ---------------------------------------------------------------- Window actions

        /// <summary>
        /// Handles the custom minimize button to hide the application to the system tray.
        /// </summary>
        private void OnMinimizeToTrayClick(object sender, RoutedEventArgs e)
        {
            Hide();
            if (DataContext is MainViewModel vm)
            {
                vm.IsWindowVisible = false;
            }
        }

        private MenuItem MakeMenuItem(string? iconKey, string title, string? description, RoutedEventHandler onClick, bool danger = false)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
            if (!string.IsNullOrEmpty(description))
            {
                text.Children.Add(new TextBlock
                {
                    Text = description,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 280,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush")
                });
            }

            // Lucide icon geometry; it takes the menu item's Foreground, so danger items turn the icon red too.
            // A null key leaves an empty slot so labels stay aligned (used by the unchecked theme choices).
            var icon = new ContentControl
            {
                Style = (Style)FindResource("LucideIcon"),
                Content = iconKey == null ? null : FindResource(iconKey),
                Width = 18,
                Height = 18,
                Margin = new Thickness(2, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var header = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
            header.Children.Add(icon);
            header.Children.Add(text);

            var item = new MenuItem { Header = header, Style = (Style)FindResource("DarkMenuItem") };
            AutomationProperties.SetName(item, title);
            if (!string.IsNullOrEmpty(description)) AutomationProperties.SetHelpText(item, description);
            if (danger) item.Foreground = (System.Windows.Media.Brush)FindResource("ErrorBrush");
            item.Click += onClick;
            return item;
        }

        /// <summary>A theme choice with a check mark on the one in use.</summary>
        private MenuItem MakeThemeItem(string label, ThemeMode mode)
        {
            var item = MakeMenuItem(ThemeService.Mode == mode ? "Icon.Check" : null, label, null, (s, a) => ChangeTheme(mode));
            item.IsCheckable = true;
            item.IsChecked = ThemeService.Mode == mode;
            return item;
        }

        private void ChangeTheme(ThemeMode mode)
        {
            ThemeService.SetMode(mode);

            // Save only the theme: re-read the file so unapplied edits in the Limits boxes are not saved by accident,
            // and keep the running copy in step so a later "Save limits" does not write the old theme back.
            var onDisk = AppSettings.Load();
            onDisk.Theme = mode.ToString();
            onDisk.Save();
            if (_vm != null) _vm.Settings.Theme = mode.ToString();
        }

        /// <summary>
        /// Opens the settings menu: engine update, browser link repair, exit. Plain-language names; the risky one is last and red.
        /// </summary>
        /// <summary>Cross-platform branch (2.5b): where downloads started from the browser are saved.</summary>
        private void OnBrowseDownloadFolderClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose where downloads from the browser are saved" };
            string current = vm.Settings.DownloadFolder;
            if (!string.IsNullOrWhiteSpace(current) && System.IO.Directory.Exists(current)) dialog.InitialDirectory = current;

            if (dialog.ShowDialog(this) == true) vm.SetDownloadFolderText(dialog.FolderName);
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { Style = (Style)FindResource("DarkContextMenu") };

            var updateEngineItem = MakeMenuItem("Icon.Sync", "Update download engine",
                "Fixes most \u201Ccan\u2019t download\u201D errors. Runs automatically at launch and once a day.",
                (s, args) =>
                {
                    if (DataContext is MainViewModel vm) vm.UpdateEngineCommand.Execute(null);
                });

            var appearanceLabel = new MenuItem
            {
                Style = (Style)FindResource("DarkMenuItem"),
                Focusable = false,
                IsHitTestVisible = false,
                Padding = new Thickness(12, 6, 12, 2),
                Header = new TextBlock
                {
                    Text = "Appearance",
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush")
                }
            };
            var darkItem = MakeThemeItem("Dark", ThemeMode.Dark);
            var lightItem = MakeThemeItem("Light", ThemeMode.Light);
            var systemItem = MakeThemeItem("Match Windows", ThemeMode.System);

            var repairItem = MakeMenuItem("Icon.Link", "Repair browser link",
                "Reconnects the extension so its Download button can open this app.",
                (s, args) =>
                {
                    ProtocolHandler.Register();
                    ShowToast("Browser link repaired. The extension can open this app again.");
                });

            var disconnectItem = MakeMenuItem("Icon.Cancel", "Disconnect browser link",
                "The extension will stop opening this app until you repair the link.",
                (s, args) =>
                {
                    ProtocolHandler.Unregister();
                    ShowToast("Browser link disconnected.", "Undo", () =>
                    {
                        ProtocolHandler.Register();
                        ShowToast("Browser link restored.");
                    });
                }, danger: true);

            // Cross-platform branch (2.5b): what a download started from the browser does while this window is hidden.
            bool showOnBrowserDownload = (DataContext as MainViewModel)?.ShowWindowOnNativeDownload ?? false;
            var showWindowItem = MakeMenuItem(null,
                showOnBrowserDownload ? "Browser downloads: show this window (on)" : "Browser downloads: notify only (off)",
                "What happens when a download starts from the browser while this window is hidden: a short notification (default) or bringing this window to the front. Click to switch.",
                (s, args) =>
                {
                    if (DataContext is MainViewModel vm) vm.SetShowWindowOnNativeDownload(!vm.ShowWindowOnNativeDownload);
                });

            var exitItem = MakeMenuItem("Icon.Power", "Exit YT Downloader Pro", "Stops the app and any running downloads.",
                (s, args) =>
                {
                    if (DataContext is MainViewModel vm) vm.ExitCommand.Execute(null);
                });

            // Wrapped in a real MenuItem so the menu does not wrap it in a default (light, focusable) one.
            var credit = new MenuItem
            {
                Style = (Style)FindResource("DarkMenuItem"),
                Focusable = false,
                IsHitTestVisible = false,
                Header = new TextBlock
                {
                    Text = "Crafted with \u26A1 by muzammilAwan-dev",
                    FontSize = 11.5,
                    Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush")
                }
            };

            menu.Items.Add(updateEngineItem);
            menu.Items.Add(new Separator { Style = (Style)FindResource("DarkMenuSeparator") });
            menu.Items.Add(appearanceLabel);
            menu.Items.Add(darkItem);
            menu.Items.Add(lightItem);
            menu.Items.Add(systemItem);
            menu.Items.Add(new Separator { Style = (Style)FindResource("DarkMenuSeparator") });
            menu.Items.Add(repairItem);
            menu.Items.Add(disconnectItem);
            menu.Items.Add(showWindowItem);
            menu.Items.Add(new Separator { Style = (Style)FindResource("DarkMenuSeparator") });
            menu.Items.Add(exitItem);
            menu.Items.Add(new Separator { Style = (Style)FindResource("DarkMenuSeparator") });
            menu.Items.Add(credit);

            if (sender is Button btn)
            {
                // Right-align the menu under the gear so it never runs off the window's right edge.
                menu.PlacementTarget = btn;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Custom;
                menu.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => new[]
                {
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(
                        new System.Windows.Point(targetSize.Width - popupSize.Width, targetSize.Height + 4),
                        System.Windows.Controls.Primitives.PopupPrimaryAxis.None)
                };
                RestoreFocusAfterMenu(menu, btn);
                menu.IsOpen = true;
            }
        }
    }
}
