namespace Armada.Desktop.Services
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Manages in-app toast notifications and tracks state changes for alerting.
    /// </summary>
    public class DesktopNotificationService
    {
        #region Private-Members

        private Dictionary<string, MissionStatusEnum> _SeenMissionStates = new Dictionary<string, MissionStatusEnum>();
        private Dictionary<string, VoyageStatusEnum> _SeenVoyageStates = new Dictionary<string, VoyageStatusEnum>();
        private HashSet<string> _SeenStalledIds = new HashSet<string>();
        private bool _FirstPoll = true;
        private bool _Enabled = true;

        #endregion

        #region Public-Members

        /// <summary>
        /// Active toast notifications (displayed in UI).
        /// </summary>
        public ObservableCollection<ToastNotification> ActiveToasts { get; } = new ObservableCollection<ToastNotification>();

        /// <summary>
        /// Notification history (all notifications, newest first).
        /// </summary>
        public ObservableCollection<ToastNotification> History { get; } = new ObservableCollection<ToastNotification>();

        /// <summary>
        /// Whether notifications are enabled.
        /// </summary>
        public bool Enabled
        {
            get => _Enabled;
            set => _Enabled = value;
        }

        /// <summary>
        /// Unread notification count.
        /// </summary>
        public int UnreadCount { get; private set; }

        /// <summary>
        /// Fired when a new notification is added.
        /// </summary>
        public event EventHandler<ToastNotification>? NotificationAdded;

        /// <summary>
        /// Fired when unread count changes.
        /// </summary>
        public event EventHandler<int>? UnreadCountChanged;

        /// <summary>
        /// Navigation callback for mission toasts. Receives mission ID.
        /// </summary>
        public Action<string>? NavigateToMission { get; set; }

        /// <summary>
        /// Navigation callback for voyage toasts. Receives voyage ID.
        /// </summary>
        public Action<string>? NavigateToVoyage { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Process a data refresh and generate notifications for state changes.
        /// </summary>
        /// <param name="missions">Current missions list.</param>
        /// <param name="captains">Current captains list.</param>
        /// <param name="voyages">Current voyages list.</param>
        public void ProcessUpdate(List<Mission> missions, List<Captain> captains, List<Voyage> voyages)
        {
            if (_FirstPoll)
            {
                // Seed tracking on first poll -- don't alert for existing state
                foreach (Mission m in missions)
                    _SeenMissionStates[m.Id] = m.Status;
                foreach (Voyage v in voyages)
                    _SeenVoyageStates[v.Id] = v.Status;
                foreach (Captain c in captains.Where(c => c.State == CaptainStateEnum.Stalled))
                    _SeenStalledIds.Add(c.Id);
                _FirstPoll = false;
                return;
            }

            if (!_Enabled) return;

            // Detect mission state transitions
            foreach (Mission m in missions)
            {
                bool isNew = !_SeenMissionStates.TryGetValue(m.Id, out MissionStatusEnum previousStatus);
                if (isNew || previousStatus != m.Status)
                {
                    _SeenMissionStates[m.Id] = m.Status;

                    string capturedMissionId = m.Id;
                    AddNotification(new ToastNotification
                    {
                        Severity = GetMissionSeverity(m.Status),
                        Title = "Mission " + m.Status.ToString(),
                        Message = Truncate(m.Title, 80),
                        MissionId = m.Id,
                        OnClick = () => NavigateToMission?.Invoke(capturedMissionId)
                    });
                }
            }

            // Detect voyage state transitions
            foreach (Voyage v in voyages)
            {
                bool isNew = !_SeenVoyageStates.TryGetValue(v.Id, out VoyageStatusEnum previousStatus);
                if (isNew || previousStatus != v.Status)
                {
                    _SeenVoyageStates[v.Id] = v.Status;

                    string capturedVoyageId = v.Id;
                    AddNotification(new ToastNotification
                    {
                        Severity = GetVoyageSeverity(v.Status),
                        Title = "Voyage " + v.Status.ToString(),
                        Message = Truncate(v.Title, 80),
                        VoyageId = v.Id,
                        OnClick = () => NavigateToVoyage?.Invoke(capturedVoyageId)
                    });
                }
            }

            // Detect newly stalled captains
            foreach (Captain c in captains.Where(c => c.State == CaptainStateEnum.Stalled))
            {
                if (_SeenStalledIds.Add(c.Id))
                {
                    AddNotification(new ToastNotification
                    {
                        Severity = ToastSeverity.Warning,
                        Title = "Captain Stalled",
                        Message = c.Name,
                        CaptainId = c.Id
                    });
                }
            }

            // Clear stalled tracking for captains that recovered
            List<string> recoveredCaptains = _SeenStalledIds
                .Where(id => !captains.Any(c => c.Id == id && c.State == CaptainStateEnum.Stalled))
                .ToList();
            foreach (string id in recoveredCaptains)
                _SeenStalledIds.Remove(id);
        }

        /// <summary>
        /// Show an informational notification.
        /// </summary>
        /// <param name="title">Notification title.</param>
        /// <param name="message">Notification message.</param>
        public void ShowInfo(string title, string message)
        {
            AddNotification(new ToastNotification
            {
                Severity = ToastSeverity.Info,
                Title = title,
                Message = message
            });
        }

        /// <summary>
        /// Dismiss a toast notification.
        /// </summary>
        public void Dismiss(ToastNotification toast)
        {
            ActiveToasts.Remove(toast);
        }

        /// <summary>
        /// Mark all as read.
        /// </summary>
        public void MarkAllRead()
        {
            UnreadCount = 0;
            UnreadCountChanged?.Invoke(this, 0);
        }

        /// <summary>
        /// Clear all history.
        /// </summary>
        public void ClearHistory()
        {
            History.Clear();
            MarkAllRead();
        }

        #endregion

        #region Private-Methods

        private void AddNotification(ToastNotification toast)
        {
            ActiveToasts.Add(toast);
            History.Insert(0, toast);

            // Limit active toasts to 5
            while (ActiveToasts.Count > 5)
                ActiveToasts.RemoveAt(0);

            // Limit history to 100
            while (History.Count > 100)
                History.RemoveAt(History.Count - 1);

            UnreadCount++;
            UnreadCountChanged?.Invoke(this, UnreadCount);
            NotificationAdded?.Invoke(this, toast);

            // Auto-dismiss after 5 seconds
            _ = AutoDismissAsync(toast);
        }

        private async System.Threading.Tasks.Task AutoDismissAsync(ToastNotification toast)
        {
            await System.Threading.Tasks.Task.Delay(5000).ConfigureAwait(false);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ActiveToasts.Remove(toast));
        }

        private string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Length <= maxLength) return value;
            return value.Substring(0, maxLength - 3) + "...";
        }

        private ToastSeverity GetMissionSeverity(MissionStatusEnum status)
        {
            return status switch
            {
                MissionStatusEnum.Complete => ToastSeverity.Success,
                MissionStatusEnum.Failed => ToastSeverity.Error,
                MissionStatusEnum.Cancelled => ToastSeverity.Warning,
                _ => ToastSeverity.Info
            };
        }

        private ToastSeverity GetVoyageSeverity(VoyageStatusEnum status)
        {
            return status switch
            {
                VoyageStatusEnum.Complete => ToastSeverity.Success,
                VoyageStatusEnum.Cancelled => ToastSeverity.Warning,
                _ => ToastSeverity.Info
            };
        }

        #endregion
    }

    /// <summary>
    /// A toast notification item.
    /// </summary>
    public class ToastNotification
    {
        /// <summary>Severity level.</summary>
        public ToastSeverity Severity { get; set; } = ToastSeverity.Info;

        /// <summary>Notification title.</summary>
        public string Title { get; set; } = "";

        /// <summary>Notification message.</summary>
        public string Message { get; set; } = "";

        /// <summary>Timestamp.</summary>
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Formatted timestamp.</summary>
        public string TimestampText => TimestampUtc.ToLocalTime().ToString("HH:mm:ss");

        /// <summary>Associated mission ID.</summary>
        public string? MissionId { get; set; }

        /// <summary>Associated captain ID.</summary>
        public string? CaptainId { get; set; }

        /// <summary>Associated voyage ID.</summary>
        public string? VoyageId { get; set; }

        /// <summary>Navigation callback invoked when the toast body is clicked.</summary>
        public Action? OnClick { get; set; }
    }

    /// <summary>
    /// Toast notification severity.
    /// </summary>
    public enum ToastSeverity
    {
        /// <summary>Informational.</summary>
        Info,

        /// <summary>Success (mission complete, etc.).</summary>
        Success,

        /// <summary>Warning (captain stalled, etc.).</summary>
        Warning,

        /// <summary>Error (mission failed, etc.).</summary>
        Error
    }
}
