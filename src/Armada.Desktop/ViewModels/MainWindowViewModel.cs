namespace Armada.Desktop.ViewModels
{
    using System;
    using System.Reactive.Linq;
    using ReactiveUI;
    using Armada.Desktop.Services;
    using Avalonia.Threading;

    /// <summary>
    /// Main window view model managing navigation and global state.
    /// </summary>
    public class MainWindowViewModel : ViewModelBase
    {
        #region Private-Members

        private ArmadaConnectionService _Connection;
        private ViewModelBase _CurrentPage;
        private string _CurrentPageName = "Dashboard";
        private bool _IsConnected;
        private int _UnreadNotifications;

        #endregion

        #region Public-Members

        /// <summary>
        /// The currently displayed page view model.
        /// </summary>
        public ViewModelBase CurrentPage
        {
            get => _CurrentPage;
            set => this.RaiseAndSetIfChanged(ref _CurrentPage, value);
        }

        /// <summary>
        /// Name of the current page for sidebar highlighting.
        /// </summary>
        public string CurrentPageName
        {
            get => _CurrentPageName;
            set => this.RaiseAndSetIfChanged(ref _CurrentPageName, value);
        }

        /// <summary>
        /// Whether the Admiral connection is active.
        /// </summary>
        public bool IsConnected
        {
            get => _IsConnected;
            set => this.RaiseAndSetIfChanged(ref _IsConnected, value);
        }

        /// <summary>
        /// Unread notification count for badge display.
        /// </summary>
        public int UnreadNotifications
        {
            get => _UnreadNotifications;
            set => this.RaiseAndSetIfChanged(ref _UnreadNotifications, value);
        }

        /// <summary>
        /// Whether there are unread notifications.
        /// </summary>
        public bool HasUnreadNotifications => _UnreadNotifications > 0;

        /// <summary>
        /// Connection service for child view models.
        /// </summary>
        public ArmadaConnectionService Connection => _Connection;

        /// <summary>
        /// Dashboard page view model.
        /// </summary>
        public DashboardViewModel DashboardVm { get; }

        /// <summary>
        /// Fleet management view model.
        /// </summary>
        public FleetViewModel FleetVm { get; }

        /// <summary>
        /// Voyage list view model.
        /// </summary>
        public VoyageListViewModel VoyageListVm { get; }

        /// <summary>
        /// Mission list view model.
        /// </summary>
        public MissionListViewModel MissionListVm { get; }

        /// <summary>
        /// Dispatch view model.
        /// </summary>
        public DispatchViewModel DispatchVm { get; }

        /// <summary>
        /// Settings view model.
        /// </summary>
        public SettingsViewModel SettingsVm { get; }

        /// <summary>
        /// Notification history view model.
        /// </summary>
        public NotificationHistoryViewModel NotificationHistoryVm { get; }

        /// <summary>
        /// Doctor / system health view model.
        /// </summary>
        public DoctorViewModel DoctorVm { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="connection">Connection service.</param>
        /// <param name="notificationService">Notification service.</param>
        public MainWindowViewModel(ArmadaConnectionService connection, DesktopNotificationService notificationService)
        {
            _Connection = connection ?? throw new ArgumentNullException(nameof(connection));

            DashboardVm = new DashboardViewModel(connection);
            FleetVm = new FleetViewModel(connection);
            VoyageListVm = new VoyageListViewModel(connection);
            MissionListVm = new MissionListViewModel(connection);
            DispatchVm = new DispatchViewModel(connection);
            SettingsVm = new SettingsViewModel(connection);
            NotificationHistoryVm = new NotificationHistoryViewModel(notificationService);
            DoctorVm = new DoctorViewModel(connection);

            _CurrentPage = DashboardVm;

            IsConnected = connection.IsConnected;
            connection.ConnectionChanged += (s, connected) =>
            {
                Dispatcher.UIThread.Post(() => IsConnected = connected);
            };

            notificationService.UnreadCountChanged += (s, count) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    UnreadNotifications = count;
                    this.RaisePropertyChanged(nameof(HasUnreadNotifications));
                });
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Navigate to a page by name.
        /// </summary>
        /// <param name="pageName">Page name.</param>
        public void NavigateTo(string pageName)
        {
            CurrentPageName = pageName;
            CurrentPage = pageName switch
            {
                "Dashboard" => DashboardVm,
                "Fleet" => FleetVm,
                "Voyages" => VoyageListVm,
                "Missions" => MissionListVm,
                "Dispatch" => DispatchVm,
                "Settings" => SettingsVm,
                "Notifications" => NotificationHistoryVm,
                "Doctor" => DoctorVm,
                _ => DashboardVm
            };

            if (pageName == "Notifications")
            {
                NotificationHistoryVm.MarkAllRead();
            }
        }

        /// <summary>
        /// Navigate to the Missions page and select a specific mission.
        /// </summary>
        /// <param name="missionId">Mission ID to select.</param>
        public void NavigateToMission(string missionId)
        {
            NavigateTo("Missions");
            MissionListVm.SelectMission(missionId);
        }

        /// <summary>
        /// Navigate to the Voyages page and select a specific voyage.
        /// </summary>
        /// <param name="voyageId">Voyage ID to select.</param>
        public void NavigateToVoyage(string voyageId)
        {
            NavigateTo("Voyages");
            VoyageListVm.SelectVoyage(voyageId);
        }

        #endregion
    }
}
