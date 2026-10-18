using System.Collections.ObjectModel;
using YTDLPHost.Models;
using YTDLPHost.ViewModels;

namespace YTDLPHost.Services
{
    /// <summary>What <see cref="EngineSetup"/> needs from the engine installer (implemented by <see cref="EngineProvisioner"/>).</summary>
    public interface IEngineProvisioner
    {
        bool AllPresent();
        Task InstallMissingAsync(Action<EngineStage>? onStage = null);
        void StartBackgroundUpdate();
    }

    /// <summary>
    /// First-start flow, moved from MainViewModel (Phase 1.5e): if yt-dlp, ffmpeg and deno are missing, show an
    /// "Initial System Setup" card while they download, then let the queue start. If the install fails, the card
    /// shows the error and <see cref="HasFailed"/> stays true so links are refused until the app is restarted.
    /// Every UI change goes through the dispatcher, so this can run from any thread.
    /// </summary>
    public sealed class EngineSetup
    {
        /// <summary>Title of the setup card. The view model hides pause/cancel/open for an item with this title.</summary>
        public const string SetupCardTitle = "Initial System Setup";

        private readonly IEngineProvisioner _engine;
        private readonly DownloadQueue _queue;
        private readonly ObservableCollection<DownloadItemViewModel> _items;
        private readonly IUiDispatcher _ui;
        private readonly IClipboardService? _clipboard;
        private readonly Action<string> _setStatus;
        private readonly TimeSpan _cardLinger;
        private volatile bool _hasFailed;

        /// <param name="setStatus">Updates the status bar. Always called on the UI thread.</param>
        /// <param name="cardLinger">How long the "Setup Complete!" card stays before it disappears (default 2 s).</param>
        public EngineSetup(
            IEngineProvisioner engine,
            DownloadQueue queue,
            ObservableCollection<DownloadItemViewModel> items,
            IUiDispatcher ui,
            IClipboardService? clipboard,
            Action<string> setStatus,
            TimeSpan? cardLinger = null)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _items = items ?? throw new ArgumentNullException(nameof(items));
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _clipboard = clipboard;
            _setStatus = setStatus ?? throw new ArgumentNullException(nameof(setStatus));
            _cardLinger = cardLinger ?? TimeSpan.FromSeconds(2);
        }

        /// <summary>True when the first-start install failed. Downloads cannot run until the app is restarted.</summary>
        public bool HasFailed => _hasFailed;

        public async Task RunAsync()
        {
            DownloadItemViewModel? card = null;

            try
            {
                if (_engine.AllPresent())
                {
                    AppLogger.Log("[DEPENDENCIES] Core dependencies located securely.");
                    StartQueue();
                    return;
                }

                AppLogger.Log("[DEPENDENCIES] Dependencies missing. Creating UX Setup Card.");

                var setupTask = new DownloadTask
                {
                    Id = Guid.NewGuid(),
                    Title = SetupCardTitle,
                    Status = DownloadStatus.Downloading,
                    CurrentPhase = "Initializing download...",
                    IsIndeterminate = true
                };
                var setupCard = new DownloadItemViewModel(setupTask, _clipboard);
                card = setupCard;

                _ui.Invoke(() =>
                {
                    _items.Insert(0, setupCard);
                    _setStatus("Downloading required engine updates... Please wait.");
                });

                await _engine.InstallMissingAsync(stage => _ui.Invoke(() =>
                {
                    setupCard.Task.CurrentPhase = StageText(stage);
                    setupCard.Refresh();
                })).ConfigureAwait(false);

                _ui.Invoke(() =>
                {
                    setupCard.Task.CurrentPhase = "Setup Complete!";
                    setupCard.Task.Status = DownloadStatus.Completed;
                    setupCard.Task.Progress = 100.0;
                    setupCard.Task.IsIndeterminate = false;
                    setupCard.Refresh();
                    _setStatus("Ready");
                });

                await Task.Delay(_cardLinger).ConfigureAwait(false);
                _ui.Invoke(() => _items.Remove(setupCard));

                StartQueue();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[DEPENDENCIES ERROR] Failed to provision dependencies: {ex.Message}");
                _hasFailed = true;

                _ui.Invoke(() =>
                {
                    _setStatus("Network Error. Please check your internet connection.");
                    if (card != null)
                    {
                        card.Task.Status = DownloadStatus.Error;
                        card.Task.CurrentPhase = "Setup Failed! Please restart the app.";
                        card.Task.IsIndeterminate = false;
                        card.Task.ErrorMessage = "Check your internet connection and try again.";
                        card.Refresh();
                    }
                });
            }
        }

        private void StartQueue()
        {
            _queue.MarkEngineReady();
            _engine.StartBackgroundUpdate();
            _queue.Trigger();
        }

        private static string StageText(EngineStage stage) => stage switch
        {
            EngineStage.DownloadingYtDlp => "Downloading yt-dlp engine...",
            EngineStage.DownloadingFfmpeg => "Downloading FFmpeg media codecs...",
            EngineStage.ExtractingFfmpeg => "Extracting codecs...",
            EngineStage.DownloadingDeno => "Downloading JS engine...",
            EngineStage.ExtractingDeno => "Extracting JS engine...",
            _ => "Setting up..."
        };
    }
}
