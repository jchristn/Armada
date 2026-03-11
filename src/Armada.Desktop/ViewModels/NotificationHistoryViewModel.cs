namespace Armada.Desktop.ViewModels
{
    using System;
    using System.Collections.ObjectModel;
    using ReactiveUI;
    using Armada.Desktop.Services;

    /// <summary>
    /// Notification history view model.
    /// </summary>
    public class NotificationHistoryViewModel : ViewModelBase
    {
        #region Private-Members

        private DesktopNotificationService _NotificationService;
        private int _UnreadCount;

        #endregion

        #region Public-Members

        /// <summary>Notification history.</summary>
        public ObservableCollection<ToastNotification> History => _NotificationService.History;

        /// <summary>Unread notification count.</summary>
        public int UnreadCount
        {
            get => _UnreadCount;
            set => this.RaiseAndSetIfChanged(ref _UnreadCount, value);
        }

        /// <summary>Whether there are notifications.</summary>
        public bool HasNotifications => History.Count > 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="notificationService">Notification service.</param>
        public NotificationHistoryViewModel(DesktopNotificationService notificationService)
        {
            _NotificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

            UnreadCount = notificationService.UnreadCount;
            notificationService.UnreadCountChanged += (s, count) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    UnreadCount = count;
                    this.RaisePropertyChanged(nameof(HasNotifications));
                });
            };

            notificationService.NotificationAdded += (s, toast) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    this.RaisePropertyChanged(nameof(HasNotifications));
                });
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Mark all notifications as read.
        /// </summary>
        public void MarkAllRead()
        {
            _NotificationService.MarkAllRead();
        }

        /// <summary>
        /// Clear all notification history.
        /// </summary>
        public void ClearHistory()
        {
            _NotificationService.ClearHistory();
            this.RaisePropertyChanged(nameof(HasNotifications));
        }

        #endregion
    }
}
