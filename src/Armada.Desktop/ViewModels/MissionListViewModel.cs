namespace Armada.Desktop.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using ReactiveUI;
    using Avalonia.Threading;
    using Armada.Core;
    using Armada.Core.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Desktop.Services;

    /// <summary>
    /// Mission list and detail view model.
    /// </summary>
    public class MissionListViewModel : ViewModelBase
    {
        #region Private-Members

        private ArmadaConnectionService _Connection;
        private Mission? _SelectedMission;
        private string _StatusFilter = "All";
        private string _VesselFilter = "All";
        private bool _IsLoading;
        private bool _ShowCreateMission;
        private string _NewMissionTitle = "";
        private string _NewMissionDescription = "";
        private string? _NewMissionVesselId;
        private int _NewMissionPriority = 100;
        private string? _NewMissionVoyageId;
        private string _NewMissionNewVoyageTitle = "";
        private string _CaptainLog = "";
        private MissionDisplayItem? _SelectedDisplayItem;
        private int _PageNumber = 1;
        private int _TotalPages;
        private long _TotalRecords;
        private int _PageSize = 25;

        #endregion

        #region Public-Members

        /// <summary>Missions list.</summary>
        public ObservableCollection<MissionDisplayItem> Missions { get; } = new ObservableCollection<MissionDisplayItem>();

        /// <summary>Selected mission.</summary>
        public Mission? SelectedMission
        {
            get => _SelectedMission;
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedMission, value);
                if (value != null) _ = LoadCaptainLogAsync(value);
                else CaptainLog = "";
            }
        }

        /// <summary>Selected display item (for DataGrid binding).</summary>
        public MissionDisplayItem? SelectedDisplayItem
        {
            get => _SelectedDisplayItem;
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedDisplayItem, value);
                SelectedMission = value?.Mission;
            }
        }

        /// <summary>Status filter.</summary>
        public string StatusFilter
        {
            get => _StatusFilter;
            set
            {
                this.RaiseAndSetIfChanged(ref _StatusFilter, value);
                RefreshList();
            }
        }

        /// <summary>Vessel filter.</summary>
        public string VesselFilter
        {
            get => _VesselFilter;
            set
            {
                this.RaiseAndSetIfChanged(ref _VesselFilter, value);
                RefreshList();
            }
        }

        /// <summary>Available status filters.</summary>
        public List<string> StatusFilters { get; } = new List<string> { "All", "Pending", "Assigned", "InProgress", "Testing", "Review", "Complete", "Failed", "Cancelled" };

        /// <summary>Available vessel filters (populated dynamically).</summary>
        public ObservableCollection<string> VesselFilters { get; } = new ObservableCollection<string> { "All" };

        /// <summary>Whether an async operation is in progress.</summary>
        public bool IsLoading
        {
            get => _IsLoading;
            set => this.RaiseAndSetIfChanged(ref _IsLoading, value);
        }

        /// <summary>Whether to show the create mission form.</summary>
        public bool ShowCreateMission
        {
            get => _ShowCreateMission;
            set => this.RaiseAndSetIfChanged(ref _ShowCreateMission, value);
        }

        /// <summary>New mission title.</summary>
        public string NewMissionTitle
        {
            get => _NewMissionTitle;
            set => this.RaiseAndSetIfChanged(ref _NewMissionTitle, value);
        }

        /// <summary>New mission description.</summary>
        public string NewMissionDescription
        {
            get => _NewMissionDescription;
            set => this.RaiseAndSetIfChanged(ref _NewMissionDescription, value);
        }

        /// <summary>New mission vessel ID.</summary>
        public string? NewMissionVesselId
        {
            get => _NewMissionVesselId;
            set => this.RaiseAndSetIfChanged(ref _NewMissionVesselId, value);
        }

        /// <summary>New mission priority.</summary>
        public int NewMissionPriority
        {
            get => _NewMissionPriority;
            set => this.RaiseAndSetIfChanged(ref _NewMissionPriority, value);
        }

        /// <summary>Captain log output for the selected mission.</summary>
        public string CaptainLog
        {
            get => _CaptainLog;
            set => this.RaiseAndSetIfChanged(ref _CaptainLog, value);
        }

        /// <summary>New mission voyage ID (attach to existing voyage).</summary>
        public string? NewMissionVoyageId
        {
            get => _NewMissionVoyageId;
            set => this.RaiseAndSetIfChanged(ref _NewMissionVoyageId, value);
        }

        /// <summary>New voyage title (create new voyage for this mission).</summary>
        public string NewMissionNewVoyageTitle
        {
            get => _NewMissionNewVoyageTitle;
            set => this.RaiseAndSetIfChanged(ref _NewMissionNewVoyageTitle, value);
        }

        /// <summary>Available vessels for the create form.</summary>
        public ObservableCollection<Vessel> AvailableVessels { get; } = new ObservableCollection<Vessel>();

        /// <summary>Available voyages for the create form.</summary>
        public ObservableCollection<Voyage> AvailableVoyages { get; } = new ObservableCollection<Voyage>();

        /// <summary>Current page number.</summary>
        public int PageNumber
        {
            get => _PageNumber;
            set => this.RaiseAndSetIfChanged(ref _PageNumber, value);
        }

        /// <summary>Total pages.</summary>
        public int TotalPages
        {
            get => _TotalPages;
            set => this.RaiseAndSetIfChanged(ref _TotalPages, value);
        }

        /// <summary>Total records.</summary>
        public long TotalRecords
        {
            get => _TotalRecords;
            set => this.RaiseAndSetIfChanged(ref _TotalRecords, value);
        }

        /// <summary>Page size.</summary>
        public int PageSize
        {
            get => _PageSize;
            set
            {
                this.RaiseAndSetIfChanged(ref _PageSize, value);
                PageNumber = 1;
                _ = LoadPageAsync();
            }
        }

        /// <summary>Available page size options.</summary>
        public List<int> PageSizeOptions { get; } = new List<int> { 10, 25, 50, 100, 250 };

        /// <summary>Pagination display text.</summary>
        public string PaginationText => $"Page {PageNumber} of {TotalPages}";

        /// <summary>Whether previous page is available.</summary>
        public bool CanGoPrevious => PageNumber > 1;

        /// <summary>Whether next page is available.</summary>
        public bool CanGoNext => PageNumber < TotalPages;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public MissionListViewModel(ArmadaConnectionService connection)
        {
            _Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _Connection.DataRefreshed += OnDataRefreshed;

            if (_Connection.Missions.Count > 0)
            {
                OnDataRefreshed(this, EventArgs.Empty);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get the connection service.
        /// </summary>
        public ArmadaConnectionService GetConnection() => _Connection;

        /// <summary>
        /// Cancel a mission.
        /// </summary>
        public async Task CancelMissionAsync(string missionId)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                await _Connection.GetApiClient().DeleteMissionAsync(missionId).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Permanently delete a mission from the database.
        /// </summary>
        public async Task DeleteMissionAsync(string missionId)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                await _Connection.GetApiClient().PurgeMissionAsync(missionId).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => SelectedMission = null);
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Restart a failed or cancelled mission with optional instruction edits.
        /// </summary>
        public async Task RestartMissionAsync(string missionId, string? title = null, string? description = null)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                await _Connection.GetApiClient().RestartMissionAsync(missionId, title, description).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Create a new standalone mission.
        /// </summary>
        public async Task CreateMissionAsync()
        {
            if (string.IsNullOrWhiteSpace(NewMissionTitle)) return;

            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                Mission mission = new Mission(NewMissionTitle.Trim());
                if (!string.IsNullOrWhiteSpace(NewMissionDescription)) mission.Description = NewMissionDescription.Trim();
                if (!string.IsNullOrEmpty(NewMissionVesselId)) mission.VesselId = NewMissionVesselId;
                mission.Priority = NewMissionPriority;

                // Handle voyage: create new or attach to existing
                if (!string.IsNullOrWhiteSpace(NewMissionNewVoyageTitle))
                {
                    Voyage newVoyage = new Voyage(NewMissionNewVoyageTitle.Trim());
                    Voyage? created = await _Connection.GetApiClient().CreateVoyageAsync(newVoyage).ConfigureAwait(false);
                    if (created != null) mission.VoyageId = created.Id;
                }
                else if (!string.IsNullOrEmpty(NewMissionVoyageId))
                {
                    mission.VoyageId = NewMissionVoyageId;
                }

                await _Connection.GetApiClient().CreateMissionAsync(mission).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);

                Dispatcher.UIThread.Post(() =>
                {
                    NewMissionTitle = "";
                    NewMissionDescription = "";
                    NewMissionVesselId = null;
                    NewMissionVoyageId = null;
                    NewMissionNewVoyageTitle = "";
                    NewMissionPriority = 100;
                    ShowCreateMission = false;
                });
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Load captain log for the selected mission.
        /// </summary>
        public async Task LoadCaptainLogAsync(Mission mission)
        {
            if (string.IsNullOrEmpty(mission.CaptainId))
            {
                Dispatcher.UIThread.Post(() => CaptainLog = "");
                return;
            }

            await Task.Run(() =>
            {
                try
                {
                    string logDir = Path.Combine(_Connection.GetSettings().LogDirectory, "captains");
                    string logFile = Path.Combine(logDir, mission.CaptainId + ".log");

                    if (!File.Exists(logFile))
                    {
                        Dispatcher.UIThread.Post(() => CaptainLog = "No log file found.");
                        return;
                    }

                    using (FileStream fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (StreamReader reader = new StreamReader(fs))
                    {
                        string content = reader.ReadToEnd();
                        string[] lines = content.Split('\n');
                        int takeCount = Math.Min(50, lines.Length);
                        string tail = string.Join("\n", lines.Skip(lines.Length - takeCount));
                        Dispatcher.UIThread.Post(() => CaptainLog = tail);
                    }
                }
                catch
                {
                    Dispatcher.UIThread.Post(() => CaptainLog = "Unable to read log file.");
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Go to next page.
        /// </summary>
        public void NextPage()
        {
            if (PageNumber < TotalPages) { PageNumber++; _ = LoadPageAsync(); }
        }

        /// <summary>
        /// Go to previous page.
        /// </summary>
        public void PreviousPage()
        {
            if (PageNumber > 1) { PageNumber--; _ = LoadPageAsync(); }
        }

        /// <summary>
        /// Go to specific page.
        /// </summary>
        public void GoToPage(int page)
        {
            if (page >= 1 && page <= TotalPages) { PageNumber = page; _ = LoadPageAsync(); }
        }

        /// <summary>
        /// Refresh current page.
        /// </summary>
        public async Task RefreshPageAsync()
        {
            await LoadPageAsync().ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private void OnDataRefreshed(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                RefreshLookups();
                _ = LoadPageAsync();
            });
        }

        private void RefreshLookups()
        {
            // Update available vessels for create form
            AvailableVessels.Clear();
            foreach (Vessel v in _Connection.Vessels) AvailableVessels.Add(v);

            // Update available voyages for create form (only open/in-progress)
            AvailableVoyages.Clear();
            foreach (Voyage v in _Connection.Voyages.Where(v => v.Status == VoyageStatusEnum.Open || v.Status == VoyageStatusEnum.InProgress))
                AvailableVoyages.Add(v);

            // Update vessel filters
            List<string> currentVessels = new List<string> { "All" };
            foreach (Vessel v in _Connection.Vessels) currentVessels.Add(v.Name);
            if (!currentVessels.SequenceEqual(VesselFilters))
            {
                VesselFilters.Clear();
                foreach (string name in currentVessels) VesselFilters.Add(name);
            }
        }

        private void RefreshList()
        {
            RefreshLookups();
            PageNumber = 1;
            _ = LoadPageAsync();
        }

        private async Task LoadPageAsync()
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                EnumerationQuery query = new EnumerationQuery();
                query.PageNumber = PageNumber;
                query.PageSize = PageSize;

                if (StatusFilter != "All")
                    query.Status = StatusFilter;

                if (VesselFilter != "All")
                {
                    Vessel? filterVessel = _Connection.Vessels.FirstOrDefault(v => v.Name == VesselFilter);
                    if (filterVessel != null)
                        query.VesselId = filterVessel.Id;
                }

                ArmadaApiClient client = _Connection.GetApiClient();
                EnumerationResult<Mission>? result = await client.EnumerateMissionsAsync(query).ConfigureAwait(false);

                if (result != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        TotalPages = result.TotalPages;
                        TotalRecords = result.TotalRecords;
                        PageNumber = result.PageNumber;

                        // Build lookups from connection service cache
                        Dictionary<string, Captain> captainLookup = _Connection.Captains.ToDictionary(c => c.Id);
                        Dictionary<string, Vessel> vesselLookup = _Connection.Vessels.ToDictionary(v => v.Id);
                        Dictionary<string, Voyage> voyageLookup = _Connection.Voyages.ToDictionary(v => v.Id);

                        Missions.Clear();
                        foreach (Mission m in result.Objects)
                        {
                            string captainName = "";
                            if (!string.IsNullOrEmpty(m.CaptainId) && captainLookup.TryGetValue(m.CaptainId, out Captain? c))
                                captainName = c.Name;

                            string vesselName = "";
                            if (!string.IsNullOrEmpty(m.VesselId) && vesselLookup.TryGetValue(m.VesselId, out Vessel? v))
                                vesselName = v.Name;

                            string voyageTitle = "";
                            if (!string.IsNullOrEmpty(m.VoyageId) && voyageLookup.TryGetValue(m.VoyageId, out Voyage? voy))
                                voyageTitle = voy.Title;

                            Missions.Add(new MissionDisplayItem
                            {
                                Mission = m,
                                CaptainName = captainName,
                                VesselName = vesselName,
                                VoyageTitle = voyageTitle
                            });
                        }

                        this.RaisePropertyChanged(nameof(PaginationText));
                        this.RaisePropertyChanged(nameof(CanGoPrevious));
                        this.RaisePropertyChanged(nameof(CanGoNext));
                    });
                }
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        #endregion
    }

    /// <summary>
    /// Display model for mission list items.
    /// </summary>
    public class MissionDisplayItem
    {
        /// <summary>The mission.</summary>
        public Mission Mission { get; set; } = new Mission();

        /// <summary>Captain name.</summary>
        public string CaptainName { get; set; } = "";

        /// <summary>Vessel name.</summary>
        public string VesselName { get; set; } = "";

        /// <summary>Voyage title.</summary>
        public string VoyageTitle { get; set; } = "";
    }
}
