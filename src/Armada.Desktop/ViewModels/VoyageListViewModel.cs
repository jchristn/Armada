namespace Armada.Desktop.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading.Tasks;
    using ReactiveUI;
    using Avalonia.Threading;
    using Armada.Core.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Desktop.Services;

    /// <summary>
    /// Voyage list and detail view model.
    /// </summary>
    public class VoyageListViewModel : ViewModelBase
    {
        #region Private-Members

        private ArmadaConnectionService _Connection;
        private Voyage? _SelectedVoyage;
        private string _StatusFilter = "All";
        private string _VesselFilter = "All";
        private bool _IsLoading;
        private int _PageNumber = 1;
        private int _TotalPages;
        private long _TotalRecords;
        private int _PageSize = 25;
        private bool _ShowCreateVoyage;
        private string _NewVoyageTitle = "";
        private string _NewVoyageDescription = "";

        #endregion

        #region Public-Members

        /// <summary>Voyages list.</summary>
        public ObservableCollection<VoyageDisplayItem> Voyages { get; } = new ObservableCollection<VoyageDisplayItem>();

        /// <summary>Missions for the selected voyage.</summary>
        public ObservableCollection<Mission> SelectedVoyageMissions { get; } = new ObservableCollection<Mission>();

        /// <summary>Selected voyage.</summary>
        public Voyage? SelectedVoyage
        {
            get => _SelectedVoyage;
            set
            {
                this.RaiseAndSetIfChanged(ref _SelectedVoyage, value);
                UpdateSelectedVoyageMissions();
                this.RaisePropertyChanged(nameof(CanRetryFailed));
                this.RaisePropertyChanged(nameof(CanCancelVoyage));
                this.RaisePropertyChanged(nameof(CanDeleteVoyage));
            }
        }

        /// <summary>Whether the Retry Failed button should be visible.</summary>
        public bool CanRetryFailed =>
            _SelectedVoyage != null &&
            _Connection.Missions.Any(m => m.VoyageId == _SelectedVoyage.Id &&
                (m.Status == MissionStatusEnum.Failed || m.Status == MissionStatusEnum.Cancelled));

        /// <summary>Whether the Cancel Voyage button should be visible.</summary>
        public bool CanCancelVoyage =>
            _SelectedVoyage != null &&
            (_SelectedVoyage.Status == VoyageStatusEnum.Open || _SelectedVoyage.Status == VoyageStatusEnum.InProgress);

        /// <summary>Whether the Delete Voyage button should be visible.</summary>
        public bool CanDeleteVoyage =>
            _SelectedVoyage != null &&
            _SelectedVoyage.Status != VoyageStatusEnum.Open &&
            _SelectedVoyage.Status != VoyageStatusEnum.InProgress;

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
        public List<string> StatusFilters { get; } = new List<string> { "All", "Open", "InProgress", "Complete", "Cancelled" };

        /// <summary>Available vessel filters (populated dynamically).</summary>
        public ObservableCollection<string> VesselFilters { get; } = new ObservableCollection<string> { "All" };

        /// <summary>Whether an async operation is in progress.</summary>
        public bool IsLoading
        {
            get => _IsLoading;
            set => this.RaiseAndSetIfChanged(ref _IsLoading, value);
        }

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

        /// <summary>Whether to show the create voyage form.</summary>
        public bool ShowCreateVoyage
        {
            get => _ShowCreateVoyage;
            set => this.RaiseAndSetIfChanged(ref _ShowCreateVoyage, value);
        }

        /// <summary>New voyage title.</summary>
        public string NewVoyageTitle
        {
            get => _NewVoyageTitle;
            set => this.RaiseAndSetIfChanged(ref _NewVoyageTitle, value);
        }

        /// <summary>New voyage description.</summary>
        public string NewVoyageDescription
        {
            get => _NewVoyageDescription;
            set => this.RaiseAndSetIfChanged(ref _NewVoyageDescription, value);
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public VoyageListViewModel(ArmadaConnectionService connection)
        {
            _Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _Connection.DataRefreshed += OnDataRefreshed;

            if (_Connection.Voyages.Count > 0)
            {
                OnDataRefreshed(this, EventArgs.Empty);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Cancel a voyage.
        /// </summary>
        public async Task CancelVoyageAsync(string voyageId)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                await _Connection.GetApiClient().CancelVoyageAsync(voyageId).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Retry failed missions in a voyage.
        /// </summary>
        public async Task RetryFailedAsync(string voyageId)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                List<Mission> failed = _Connection.Missions
                    .Where(m => m.VoyageId == voyageId && (m.Status == MissionStatusEnum.Failed || m.Status == MissionStatusEnum.Cancelled))
                    .ToList();

                foreach (Mission m in failed)
                {
                    Mission retry = new Mission(m.Title, m.Description)
                    {
                        VesselId = m.VesselId,
                        VoyageId = m.VoyageId,
                        Priority = m.Priority
                    };
                    await _Connection.GetApiClient().CreateMissionAsync(retry).ConfigureAwait(false);
                }

                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Permanently delete a voyage and all its associated missions.
        /// </summary>
        public async Task DeleteVoyageAsync(string voyageId)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                await _Connection.GetApiClient().PurgeVoyageAsync(voyageId).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => SelectedVoyage = null);
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Select a voyage by ID.
        /// </summary>
        public void SelectVoyage(string voyageId)
        {
            SelectedVoyage = _Connection.Voyages.FirstOrDefault(v => v.Id == voyageId);
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
        /// Create a new voyage.
        /// </summary>
        public async Task CreateVoyageAsync()
        {
            if (string.IsNullOrWhiteSpace(NewVoyageTitle)) return;

            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                Voyage voyage = new Voyage(NewVoyageTitle.Trim());
                if (!string.IsNullOrWhiteSpace(NewVoyageDescription)) voyage.Description = NewVoyageDescription.Trim();

                await _Connection.GetApiClient().CreateVoyageAsync(voyage).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);

                Dispatcher.UIThread.Post(() =>
                {
                    NewVoyageTitle = "";
                    NewVoyageDescription = "";
                    ShowCreateVoyage = false;
                });
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
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
                this.RaisePropertyChanged(nameof(CanRetryFailed));
                this.RaisePropertyChanged(nameof(CanCancelVoyage));
                this.RaisePropertyChanged(nameof(CanDeleteVoyage));
            });
        }

        private void RefreshLookups()
        {
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

                ArmadaApiClient client = _Connection.GetApiClient();
                EnumerationResult<Voyage>? result = await client.EnumerateVoyagesAsync(query).ConfigureAwait(false);

                if (result != null)
                {
                    // Determine vessel ID for client-side filtering
                    string? filterVesselId = null;
                    if (VesselFilter != "All")
                    {
                        Vessel? filterVessel = _Connection.Vessels.FirstOrDefault(v => v.Name == VesselFilter);
                        if (filterVessel != null)
                            filterVesselId = filterVessel.Id;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        TotalPages = result.TotalPages;
                        TotalRecords = result.TotalRecords;
                        PageNumber = result.PageNumber;

                        // Filter voyages client-side by vessel if selected
                        List<Voyage> filtered = result.Objects.ToList();
                        if (filterVesselId != null)
                        {
                            filtered = filtered.Where(v =>
                                _Connection.Missions.Any(m => m.VoyageId == v.Id && m.VesselId == filterVesselId)).ToList();
                        }

                        Voyages.Clear();
                        foreach (Voyage v in filtered)
                        {
                            int total = _Connection.Missions.Count(m => m.VoyageId == v.Id);
                            int complete = _Connection.Missions.Count(m => m.VoyageId == v.Id && m.Status == MissionStatusEnum.Complete);
                            int failedCount = _Connection.Missions.Count(m => m.VoyageId == v.Id && m.Status == MissionStatusEnum.Failed);

                            Voyages.Add(new VoyageDisplayItem
                            {
                                Voyage = v,
                                TotalMissions = total,
                                CompletedMissions = complete,
                                FailedMissions = failedCount,
                                ProgressText = total > 0 ? $"{complete}/{total}" : "0/0"
                            });
                        }

                        UpdateSelectedVoyageMissions();

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

        private void UpdateSelectedVoyageMissions()
        {
            SelectedVoyageMissions.Clear();
            if (_SelectedVoyage != null)
            {
                foreach (Mission m in _Connection.Missions.Where(m => m.VoyageId == _SelectedVoyage.Id).OrderBy(m => m.CreatedUtc))
                {
                    SelectedVoyageMissions.Add(m);
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Display model for voyage list items.
    /// </summary>
    public class VoyageDisplayItem
    {
        /// <summary>The voyage.</summary>
        public Voyage Voyage { get; set; } = new Voyage();

        /// <summary>Total missions.</summary>
        public int TotalMissions { get; set; }

        /// <summary>Completed missions.</summary>
        public int CompletedMissions { get; set; }

        /// <summary>Failed missions.</summary>
        public int FailedMissions { get; set; }

        /// <summary>Progress text.</summary>
        public string ProgressText { get; set; } = "";

        /// <summary>Progress percentage.</summary>
        public double Progress => TotalMissions > 0 ? (double)CompletedMissions / TotalMissions * 100 : 0;
    }
}
