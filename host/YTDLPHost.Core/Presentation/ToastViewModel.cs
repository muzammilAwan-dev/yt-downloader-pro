using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YTDLPHost.Services
{
    /// <summary>
    /// The short message that floats over the list (replaces message boxes, which cannot follow the app's theme): one line, an optional
    /// action such as "Undo", gone after 3.5 s (7 s when there is an action to reach).
    /// </summary>
    public sealed class ToastViewModel : ObservableObject
    {
        public static readonly TimeSpan PlainDuration = TimeSpan.FromSeconds(3.5);
        public static readonly TimeSpan ActionDuration = TimeSpan.FromSeconds(7);

        private readonly IUiDispatcher _ui;
        private readonly TimeSpan _plain, _withAction;
        private CancellationTokenSource? _timer;
        private Action? _action;
        private string _message = "";
        private string? _actionLabel;
        private bool _isVisible;

        public ToastViewModel(IUiDispatcher ui, TimeSpan? plainDuration = null, TimeSpan? actionDuration = null)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
            _plain = plainDuration ?? PlainDuration;
            _withAction = actionDuration ?? ActionDuration;
        }

        public string Message { get => _message; private set => SetProperty(ref _message, value); }
        public string? ActionLabel { get => _actionLabel; private set { if (SetProperty(ref _actionLabel, value)) OnPropertyChanged(nameof(HasAction)); } }
        public bool HasAction => _actionLabel != null;
        public bool IsVisible { get => _isVisible; private set => SetProperty(ref _isVisible, value); }

        /// <summary>Shows a message; a newer toast replaces the one on screen. Callable from any thread.</summary>
        public void Show(string message, string? actionLabel = null, Action? action = null)
        {
            var timer = new CancellationTokenSource();
            var duration = actionLabel == null ? _plain : _withAction;
            _ui.Post(() =>
            {
                var old = Interlocked.Exchange(ref _timer, timer);
                old?.Cancel();
                _action = actionLabel == null ? null : action;
                Message = message;
                ActionLabel = actionLabel;
                IsVisible = true;
            });

            _ = Task.Delay(duration, timer.Token).ContinueWith(t =>
            {
                if (t.IsCanceled) return;
                _ui.Post(() => { if (ReferenceEquals(_timer, timer)) Dismiss(); });
            }, TaskScheduler.Default);
        }

        /// <summary>The action button was pressed: the toast goes away, then the action runs.</summary>
        public void RunAction()
        {
            var action = _action;
            _action = null;
            Dismiss();
            action?.Invoke();
        }

        public void Dismiss()
        {
            var old = Interlocked.Exchange(ref _timer, null);
            old?.Cancel();
            _action = null;
            IsVisible = false;
        }
    }
}
