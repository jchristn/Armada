namespace Armada.Desktop.ViewModels
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Threading.Tasks;
    using ReactiveUI;
    using Avalonia.Threading;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using Armada.Desktop.Services;

    /// <summary>
    /// Fleet management view model covering fleets, vessels, and captains.
    /// </summary>
    public class FleetViewModel : ViewModelBase
    {
        #region Private-Members

        private ArmadaConnectionService _Connection;
        private string _SelectedTab = "Fleets";
        private bool _IsLoading;

        private string _NewVesselName = "";
        private string _NewVesselRepoUrl = "";
        private string _NewVesselBranch = "main";
        private string? _NewVesselFleetId = null;
        private bool _ShowAddVessel;

        private string _NewFleetName = "";
        private string _NewFleetDescription = "";
        private bool _ShowAddFleet;

        private string _NewCaptainName = "";
        private AgentRuntimeEnum _NewCaptainRuntime = AgentRuntimeEnum.ClaudeCode;
        private int _NewCaptainMaxParallelism = 1;
        private bool _ShowAddCaptain;
        private string _ErrorMessage = "";

        #endregion

        #region Public-Members

        /// <summary>Selected tab.</summary>
        public string SelectedTab
        {
            get => _SelectedTab;
            set => this.RaiseAndSetIfChanged(ref _SelectedTab, value);
        }

        /// <summary>Fleets.</summary>
        public ObservableCollection<FleetDisplayItem> Fleets { get; } = new ObservableCollection<FleetDisplayItem>();

        /// <summary>Vessels.</summary>
        public ObservableCollection<Vessel> Vessels { get; } = new ObservableCollection<Vessel>();

        /// <summary>Captains.</summary>
        public ObservableCollection<Captain> Captains { get; } = new ObservableCollection<Captain>();

        /// <summary>Whether an async operation is in progress.</summary>
        public bool IsLoading
        {
            get => _IsLoading;
            set => this.RaiseAndSetIfChanged(ref _IsLoading, value);
        }

        /// <summary>New vessel name.</summary>
        public string NewVesselName
        {
            get => _NewVesselName;
            set => this.RaiseAndSetIfChanged(ref _NewVesselName, value);
        }

        /// <summary>New vessel repo URL.</summary>
        public string NewVesselRepoUrl
        {
            get => _NewVesselRepoUrl;
            set => this.RaiseAndSetIfChanged(ref _NewVesselRepoUrl, value);
        }

        /// <summary>New vessel default branch.</summary>
        public string NewVesselBranch
        {
            get => _NewVesselBranch;
            set => this.RaiseAndSetIfChanged(ref _NewVesselBranch, value);
        }

        /// <summary>New vessel fleet ID.</summary>
        public string? NewVesselFleetId
        {
            get => _NewVesselFleetId;
            set => this.RaiseAndSetIfChanged(ref _NewVesselFleetId, value);
        }

        /// <summary>Whether to show the add vessel form.</summary>
        public bool ShowAddVessel
        {
            get => _ShowAddVessel;
            set => this.RaiseAndSetIfChanged(ref _ShowAddVessel, value);
        }

        /// <summary>New fleet name.</summary>
        public string NewFleetName
        {
            get => _NewFleetName;
            set => this.RaiseAndSetIfChanged(ref _NewFleetName, value);
        }

        /// <summary>New fleet description.</summary>
        public string NewFleetDescription
        {
            get => _NewFleetDescription;
            set => this.RaiseAndSetIfChanged(ref _NewFleetDescription, value);
        }

        /// <summary>Whether to show the add fleet form.</summary>
        public bool ShowAddFleet
        {
            get => _ShowAddFleet;
            set => this.RaiseAndSetIfChanged(ref _ShowAddFleet, value);
        }

        /// <summary>New captain name.</summary>
        public string NewCaptainName
        {
            get => _NewCaptainName;
            set => this.RaiseAndSetIfChanged(ref _NewCaptainName, value);
        }

        /// <summary>New captain runtime.</summary>
        public AgentRuntimeEnum NewCaptainRuntime
        {
            get => _NewCaptainRuntime;
            set => this.RaiseAndSetIfChanged(ref _NewCaptainRuntime, value);
        }

        /// <summary>New captain max parallelism.</summary>
        public int NewCaptainMaxParallelism
        {
            get => _NewCaptainMaxParallelism;
            set => this.RaiseAndSetIfChanged(ref _NewCaptainMaxParallelism, value < 1 ? 1 : value);
        }

        /// <summary>Whether to show the add captain form.</summary>
        public bool ShowAddCaptain
        {
            get => _ShowAddCaptain;
            set => this.RaiseAndSetIfChanged(ref _ShowAddCaptain, value);
        }

        /// <summary>Error message for display.</summary>
        public string ErrorMessage
        {
            get => _ErrorMessage;
            set => this.RaiseAndSetIfChanged(ref _ErrorMessage, value);
        }

        /// <summary>Available runtime options.</summary>
        public List<AgentRuntimeEnum> RuntimeOptions { get; } = new List<AgentRuntimeEnum>
        {
            AgentRuntimeEnum.ClaudeCode,
            AgentRuntimeEnum.Codex,
            AgentRuntimeEnum.Custom
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetViewModel(ArmadaConnectionService connection)
        {
            _Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _Connection.DataRefreshed += OnDataRefreshed;

            // Load any data that's already available
            if (_Connection.Vessels.Count > 0 || _Connection.Captains.Count > 0 || _Connection.Fleets.Count > 0)
            {
                OnDataRefreshed(this, EventArgs.Empty);
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get application settings.
        /// </summary>
        public ArmadaSettings GetSettings() => _Connection.GetSettings();

        /// <summary>
        /// Stop a captain by ID.
        /// </summary>
        public async Task StopCaptainAsync(string captainId)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                await _Connection.GetApiClient().StopCaptainAsync(captainId).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Stop all captains.
        /// </summary>
        public async Task StopAllCaptainsAsync()
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                foreach (Captain captain in _Connection.Captains.Where(c => c.State == CaptainStateEnum.Working || c.State == CaptainStateEnum.Stalled))
                {
                    await _Connection.GetApiClient().StopCaptainAsync(captain.Id).ConfigureAwait(false);
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
        /// Add a new captain using the form fields.
        /// </summary>
        public async Task AddCaptainAsync()
        {
            if (string.IsNullOrWhiteSpace(NewCaptainName)) return;

            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                Captain captain = new Captain(NewCaptainName.Trim(), NewCaptainRuntime);
                captain.MaxParallelism = NewCaptainMaxParallelism;
                await _Connection.GetApiClient().CreateCaptainAsync(captain).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);

                Dispatcher.UIThread.Post(() =>
                {
                    NewCaptainName = "";
                    NewCaptainRuntime = AgentRuntimeEnum.ClaudeCode;
                    NewCaptainMaxParallelism = 1;
                    ShowAddCaptain = false;
                    ErrorMessage = "";
                });
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                if (msg.Contains("UNIQUE")) msg = "A captain with that name already exists.";
                Dispatcher.UIThread.Post(() => ErrorMessage = msg);
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Delete a vessel.
        /// </summary>
        public async Task DeleteVesselAsync(string vesselId)
        {
            try
            {
                await _Connection.GetApiClient().DeleteVesselAsync(vesselId).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch { }
        }

        /// <summary>
        /// Add a new vessel.
        /// </summary>
        public async Task AddVesselAsync()
        {
            if (string.IsNullOrWhiteSpace(NewVesselName) || string.IsNullOrWhiteSpace(NewVesselRepoUrl)) return;

            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                Vessel vessel = new Vessel(NewVesselName.Trim(), NewVesselRepoUrl.Trim());
                vessel.DefaultBranch = string.IsNullOrWhiteSpace(NewVesselBranch) ? "main" : NewVesselBranch.Trim();
                if (!string.IsNullOrEmpty(NewVesselFleetId)) vessel.FleetId = NewVesselFleetId;

                await _Connection.GetApiClient().CreateVesselAsync(vessel).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);

                Dispatcher.UIThread.Post(() =>
                {
                    NewVesselName = "";
                    NewVesselRepoUrl = "";
                    NewVesselBranch = "main";
                    NewVesselFleetId = null;
                    ShowAddVessel = false;
                    ErrorMessage = "";
                });
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                if (msg.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)) msg = "A vessel with that name already exists.";
                else if (msg.Contains("500")) msg = "Server error creating vessel.";
                Dispatcher.UIThread.Post(() => ErrorMessage = msg);
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Update a captain's properties.
        /// </summary>
        public async Task UpdateCaptainAsync(Captain captain)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                await _Connection.GetApiClient().UpdateCaptainAsync(captain.Id, captain).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                if (msg.Contains("UNIQUE")) msg = "A captain with that name already exists.";
                Dispatcher.UIThread.Post(() => ErrorMessage = msg);
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Remove a captain permanently.
        /// </summary>
        public async Task RemoveCaptainAsync(string captainId)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                await _Connection.GetApiClient().DeleteCaptainAsync(captainId).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Add a new fleet.
        /// </summary>
        public async Task AddFleetAsync()
        {
            if (string.IsNullOrWhiteSpace(NewFleetName)) return;

            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                Fleet fleet = new Fleet(NewFleetName.Trim());
                if (!string.IsNullOrWhiteSpace(NewFleetDescription)) fleet.Description = NewFleetDescription.Trim();

                await _Connection.GetApiClient().CreateFleetAsync(fleet).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);

                Dispatcher.UIThread.Post(() =>
                {
                    NewFleetName = "";
                    NewFleetDescription = "";
                    ShowAddFleet = false;
                    ErrorMessage = "";
                });
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                if (msg.Contains("UNIQUE")) msg = "A fleet with that name already exists.";
                Dispatcher.UIThread.Post(() => ErrorMessage = msg);
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Remove a fleet.
        /// </summary>
        public async Task RemoveFleetAsync(string fleetId)
        {
            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                await _Connection.GetApiClient().DeleteFleetAsync(fleetId).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch { }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        /// <summary>
        /// Update a fleet's name and description.
        /// </summary>
        public async Task UpdateFleetAsync(FleetDisplayItem item)
        {
            if (string.IsNullOrWhiteSpace(item.EditName)) return;

            Dispatcher.UIThread.Post(() => IsLoading = true);
            try
            {
                item.Fleet.Name = item.EditName.Trim();
                item.Fleet.Description = string.IsNullOrWhiteSpace(item.EditDescription) ? null : item.EditDescription.Trim();

                await _Connection.GetApiClient().UpdateFleetAsync(item.Fleet.Id, item.Fleet).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);

                Dispatcher.UIThread.Post(() => item.IsEditing = false);
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                if (msg.Contains("UNIQUE")) msg = "A fleet with that name already exists.";
                Dispatcher.UIThread.Post(() => ErrorMessage = msg);
            }
            finally
            {
                Dispatcher.UIThread.Post(() => IsLoading = false);
            }
        }

        #endregion

        #region Private-Methods

        private void OnDataRefreshed(object? sender, EventArgs e)
        {
            ArmadaConnectionService.DebugLogStatic($"[FleetVM] OnDataRefreshed: conn.Fleets={_Connection.Fleets.Count} conn.Vessels={_Connection.Vessels.Count} conn.Captains={_Connection.Captains.Count}");
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Fleets.Clear();
                    foreach (Fleet f in _Connection.Fleets) Fleets.Add(new FleetDisplayItem
                    {
                        Fleet = f,
                        EditName = f.Name,
                        EditDescription = f.Description ?? ""
                    });

                    Vessels.Clear();
                    foreach (Vessel v in _Connection.Vessels) Vessels.Add(v);

                    Captains.Clear();
                    foreach (Captain c in _Connection.Captains) Captains.Add(c);

                    ArmadaConnectionService.DebugLogStatic($"[FleetVM] UI updated: Fleets={Fleets.Count} Vessels={Vessels.Count} Captains={Captains.Count}");
                }
                catch (Exception ex)
                {
                    ArmadaConnectionService.DebugLogStatic($"[FleetVM] UI update FAILED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                }
            });
        }

        #endregion
    }

    /// <summary>
    /// Display model for fleet list items with inline editing support.
    /// </summary>
    public class FleetDisplayItem : ReactiveObject
    {
        private bool _IsEditing;
        private string _EditName = "";
        private string _EditDescription = "";

        /// <summary>The fleet.</summary>
        public Fleet Fleet { get; set; } = new Fleet();

        /// <summary>Whether this item is in edit mode.</summary>
        public bool IsEditing
        {
            get => _IsEditing;
            set => this.RaiseAndSetIfChanged(ref _IsEditing, value);
        }

        /// <summary>Editable name.</summary>
        public string EditName
        {
            get => _EditName;
            set => this.RaiseAndSetIfChanged(ref _EditName, value);
        }

        /// <summary>Editable description.</summary>
        public string EditDescription
        {
            get => _EditDescription;
            set => this.RaiseAndSetIfChanged(ref _EditDescription, value);
        }
    }
}
