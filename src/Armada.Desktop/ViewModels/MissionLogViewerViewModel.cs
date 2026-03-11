namespace Armada.Desktop.ViewModels
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using ReactiveUI;
    using Avalonia.Threading;
    using Armada.Core.Settings;

    /// <summary>
    /// View model for the mission log viewer window.
    /// </summary>
    public class MissionLogViewerViewModel : ViewModelBase, IDisposable
    {
        #region Private-Members

        private string _MissionId;
        private string _MissionTitle;
        private string _LogContent = "";
        private bool _IsFollowing;
        private int _LineCount = 200;
        private CancellationTokenSource? _FollowCts;
        private string _LogFilePath;
        private bool _Disposed;

        #endregion

        #region Public-Members

        /// <summary>Mission title for the window header.</summary>
        public string MissionTitle
        {
            get => _MissionTitle;
            set => this.RaiseAndSetIfChanged(ref _MissionTitle, value);
        }

        /// <summary>Log content.</summary>
        public string LogContent
        {
            get => _LogContent;
            set => this.RaiseAndSetIfChanged(ref _LogContent, value);
        }

        /// <summary>Whether auto-follow is active.</summary>
        public bool IsFollowing
        {
            get => _IsFollowing;
            set
            {
                this.RaiseAndSetIfChanged(ref _IsFollowing, value);
                if (value) StartFollowing();
                else StopFollowing();
            }
        }

        /// <summary>Number of lines to show.</summary>
        public int LineCount
        {
            get => _LineCount;
            set
            {
                this.RaiseAndSetIfChanged(ref _LineCount, value);
                _ = LoadLogAsync();
            }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="missionTitle">Mission display title.</param>
        /// <param name="settings">Application settings.</param>
        public MissionLogViewerViewModel(string missionId, string missionTitle, ArmadaSettings settings)
        {
            _MissionId = missionId ?? throw new ArgumentNullException(nameof(missionId));
            _MissionTitle = missionTitle ?? missionId;
            _LogFilePath = Path.Combine(settings.LogDirectory, "missions", missionId + ".log");

            _ = LoadLogAsync();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Refresh the log content.
        /// </summary>
        public async Task LoadLogAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(_LogFilePath))
                    {
                        Dispatcher.UIThread.Post(() => LogContent = "No log file found at: " + _LogFilePath);
                        return;
                    }

                    using (FileStream fs = new FileStream(_LogFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader reader = new StreamReader(fs))
                    {
                        string content = reader.ReadToEnd();
                        string[] lines = content.Split('\n');
                        int takeCount = Math.Min(_LineCount, lines.Length);
                        string tail = string.Join("\n", lines.AsSpan().Slice(lines.Length - takeCount).ToArray());
                        Dispatcher.UIThread.Post(() => LogContent = tail);
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.UIThread.Post(() => LogContent = "Error reading log: " + ex.Message);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Dispose resources.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            StopFollowing();
        }

        #endregion

        #region Private-Methods

        private void StartFollowing()
        {
            StopFollowing();
            _FollowCts = new CancellationTokenSource();
            CancellationToken token = _FollowCts.Token;
            _ = FollowAsync(token);
        }

        private void StopFollowing()
        {
            _FollowCts?.Cancel();
            _FollowCts?.Dispose();
            _FollowCts = null;
        }

        private async Task FollowAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await LoadLogAsync().ConfigureAwait(false);
                try
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        #endregion
    }
}
