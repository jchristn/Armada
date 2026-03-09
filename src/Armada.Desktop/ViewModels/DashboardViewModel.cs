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
    /// Dashboard view model replicating all functionality from armada watch.
    /// </summary>
    public class DashboardViewModel : ViewModelBase
    {
        #region Private-Members

        private ArmadaConnectionService _Connection;

        private int _TotalCaptains;
        private int _IdleCaptains;
        private int _WorkingCaptains;
        private int _StalledCaptains;
        private int _PendingMissions;
        private int _InProgressMissions;
        private int _CompleteMissions;
        private int _FailedMissions;
        private int _ActiveVoyages;
        private string _LastRefresh = "";

        private HashSet<string> _SeenCompletedIds = new HashSet<string>();
        private HashSet<string> _SeenFailedIds = new HashSet<string>();
        private bool _FirstPoll = true;

        #endregion

        #region Public-Members

        /// <summary>Total captains.</summary>
        public int TotalCaptains { get => _TotalCaptains; set => this.RaiseAndSetIfChanged(ref _TotalCaptains, value); }

        /// <summary>Idle captains.</summary>
        public int IdleCaptains { get => _IdleCaptains; set => this.RaiseAndSetIfChanged(ref _IdleCaptains, value); }

        /// <summary>Working captains.</summary>
        public int WorkingCaptains { get => _WorkingCaptains; set => this.RaiseAndSetIfChanged(ref _WorkingCaptains, value); }

        /// <summary>Stalled captains.</summary>
        public int StalledCaptains { get => _StalledCaptains; set => this.RaiseAndSetIfChanged(ref _StalledCaptains, value); }

        /// <summary>Pending missions.</summary>
        public int PendingMissions { get => _PendingMissions; set => this.RaiseAndSetIfChanged(ref _PendingMissions, value); }

        /// <summary>In-progress missions.</summary>
        public int InProgressMissions { get => _InProgressMissions; set => this.RaiseAndSetIfChanged(ref _InProgressMissions, value); }

        /// <summary>Complete missions.</summary>
        public int CompleteMissions { get => _CompleteMissions; set => this.RaiseAndSetIfChanged(ref _CompleteMissions, value); }

        /// <summary>Failed missions.</summary>
        public int FailedMissions { get => _FailedMissions; set => this.RaiseAndSetIfChanged(ref _FailedMissions, value); }

        /// <summary>Active voyages.</summary>
        public int ActiveVoyages { get => _ActiveVoyages; set => this.RaiseAndSetIfChanged(ref _ActiveVoyages, value); }

        /// <summary>Last refresh timestamp.</summary>
        public string LastRefresh { get => _LastRefresh; set => this.RaiseAndSetIfChanged(ref _LastRefresh, value); }

        /// <summary>Voyage progress items.</summary>
        public ObservableCollection<VoyageProgressItem> VoyageProgressItems { get; } = new ObservableCollection<VoyageProgressItem>();

        /// <summary>Action required items.</summary>
        public ObservableCollection<ActionItem> ActionItems { get; } = new ObservableCollection<ActionItem>();

        /// <summary>Recent signals.</summary>
        public ObservableCollection<SignalItem> RecentSignals { get; } = new ObservableCollection<SignalItem>();

        /// <summary>Active captains with details.</summary>
        public ObservableCollection<CaptainItem> ActiveCaptains { get; } = new ObservableCollection<CaptainItem>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DashboardViewModel(ArmadaConnectionService connection)
        {
            _Connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _Connection.DataRefreshed += OnDataRefreshed;

            if (_Connection.CurrentStatus != null)
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
        /// Permanently delete a voyage and all its missions.
        /// </summary>
        public async Task DeleteVoyageAsync(string voyageId)
        {
            try
            {
                await _Connection.GetApiClient().PurgeVoyageAsync(voyageId).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch { }
        }

        /// <summary>
        /// Permanently delete a mission.
        /// </summary>
        public async Task DeleteMissionAsync(string missionId)
        {
            try
            {
                await _Connection.GetApiClient().PurgeMissionAsync(missionId).ConfigureAwait(false);
                await _Connection.RefreshAsync().ConfigureAwait(false);
            }
            catch { }
        }

        #endregion

        #region Private-Methods

        private void OnDataRefreshed(object? sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() => UpdateFromData());
        }

        private void UpdateFromData()
        {
            ArmadaStatus? status = _Connection.CurrentStatus;
            ArmadaConnectionService.DebugLogStatic($"[DashboardVM] UpdateFromData: status={(status != null ? "OK" : "NULL")} vessels={_Connection.Vessels.Count} captains={_Connection.Captains.Count} fleets={_Connection.Fleets.Count}");
            if (status == null) return;

            // Captain summary
            TotalCaptains = status.TotalCaptains;
            IdleCaptains = status.IdleCaptains;
            WorkingCaptains = status.WorkingCaptains;
            StalledCaptains = status.StalledCaptains;

            // Mission summary
            PendingMissions = GetStatusCount(status, "Pending");
            InProgressMissions = GetStatusCount(status, "InProgress") + GetStatusCount(status, "Assigned");
            CompleteMissions = GetStatusCount(status, "Complete");
            FailedMissions = GetStatusCount(status, "Failed");
            ActiveVoyages = status.ActiveVoyages;

            LastRefresh = DateTime.UtcNow.ToString("HH:mm:ss") + " UTC";

            // Build lookup dictionaries
            Dictionary<string, Vessel> vesselLookup = _Connection.Vessels.ToDictionary(v => v.Id);
            Dictionary<string, Captain> captainLookup = _Connection.Captains.ToDictionary(c => c.Id);
            Dictionary<string, Mission> missionLookup = _Connection.Missions.ToDictionary(m => m.Id);
            Dictionary<string, Voyage> voyageLookup = _Connection.Voyages.ToDictionary(v => v.Id);

            // Voyage progress
            VoyageProgressItems.Clear();
            foreach (VoyageProgress vp in status.Voyages)
            {
                VoyageProgressItems.Add(new VoyageProgressItem
                {
                    VoyageId = vp.Voyage?.Id ?? "",
                    Title = vp.Voyage?.Title ?? "Unknown",
                    CompletedMissions = vp.CompletedMissions,
                    TotalMissions = vp.TotalMissions,
                    FailedMissions = vp.FailedMissions,
                    InProgressMissions = vp.InProgressMissions,
                    Progress = vp.TotalMissions > 0 ? (double)vp.CompletedMissions / vp.TotalMissions * 100 : 0
                });
            }

            // Active captains
            ActiveCaptains.Clear();
            foreach (Captain captain in _Connection.Captains)
            {
                if (captain.State == CaptainStateEnum.Idle && _Connection.Captains.Count > 5) continue;

                string missionTitle = "";
                string vesselName = "";
                if (!string.IsNullOrEmpty(captain.CurrentMissionId) && missionLookup.TryGetValue(captain.CurrentMissionId, out Mission? m))
                {
                    missionTitle = m.Title.Length > 60 ? m.Title.Substring(0, 57) + "..." : m.Title;
                    if (!string.IsNullOrEmpty(m.VesselId) && vesselLookup.TryGetValue(m.VesselId, out Vessel? v))
                        vesselName = v.Name;
                }

                ActiveCaptains.Add(new CaptainItem
                {
                    Name = captain.Name,
                    State = captain.State,
                    Runtime = captain.Runtime,
                    MissionTitle = missionTitle,
                    VesselName = vesselName,
                    HeartbeatAge = FormatAge(captain.LastHeartbeatUtc)
                });
            }

            // Action items
            ActionItems.Clear();
            DateTime now = DateTime.UtcNow;

            // Stalled captains
            foreach (Captain captain in _Connection.Captains.Where(c => c.State == CaptainStateEnum.Stalled))
            {
                string chain = BuildCaptainChain(captain, missionLookup, vesselLookup);
                ActionItems.Add(new ActionItem
                {
                    Severity = "Stalled",
                    Title = chain,
                    Detail = "No heartbeat for " + FormatAge(captain.LastHeartbeatUtc),
                    Suggestion = "armada captain stop " + captain.Name,
                    CaptainId = captain.Id
                });
            }

            // Failed missions (last 5)
            List<Mission> failed = _Connection.Missions
                .Where(m => m.Status == MissionStatusEnum.Failed)
                .OrderByDescending(m => m.CompletedUtc ?? m.LastUpdateUtc)
                .Take(5)
                .ToList();

            foreach (Mission mission in failed)
            {
                string chain = BuildMissionChain(mission, captainLookup, vesselLookup);
                string voyageInfo = "";
                if (!string.IsNullOrEmpty(mission.VoyageId) && voyageLookup.TryGetValue(mission.VoyageId, out Voyage? voy))
                    voyageInfo = voy.Title + " - ";

                ActionItems.Add(new ActionItem
                {
                    Severity = "Failed",
                    Title = chain,
                    Detail = voyageInfo + "Failed " + FormatTimeSince(mission.CompletedUtc ?? mission.LastUpdateUtc) + " ago",
                    Suggestion = "armada mission retry " + mission.Id,
                    MissionId = mission.Id
                });
            }

            // Recently completed (last 30 min, max 5)
            List<Mission> completed = _Connection.Missions
                .Where(m => m.Status == MissionStatusEnum.Complete && m.CompletedUtc.HasValue && (now - m.CompletedUtc.Value).TotalMinutes <= 30)
                .OrderByDescending(m => m.CompletedUtc)
                .Take(5)
                .ToList();

            foreach (Mission mission in completed)
            {
                string chain = BuildMissionChain(mission, captainLookup, vesselLookup);
                ActionItems.Add(new ActionItem
                {
                    Severity = "Done",
                    Title = chain,
                    Detail = "Completed " + FormatTimeSince(mission.CompletedUtc!.Value) + " ago"
                });
            }

            // Recent signals
            RecentSignals.Clear();
            foreach (Signal signal in status.RecentSignals.Take(8))
            {
                RecentSignals.Add(new SignalItem
                {
                    Timestamp = signal.CreatedUtc.ToString("HH:mm:ss"),
                    Type = signal.Type.ToString(),
                    From = signal.FromCaptainId ?? "Admiral"
                });
            }

            // Track completions/failures for notifications
            if (!_FirstPoll)
            {
                int newCompleted = 0;
                int newFailed = 0;
                foreach (Mission m in _Connection.Missions.Where(m => m.Status == MissionStatusEnum.Complete))
                    if (_SeenCompletedIds.Add(m.Id)) newCompleted++;
                foreach (Mission m in _Connection.Missions.Where(m => m.Status == MissionStatusEnum.Failed))
                    if (_SeenFailedIds.Add(m.Id)) newFailed++;
            }
            else
            {
                foreach (Mission m in _Connection.Missions.Where(m => m.Status == MissionStatusEnum.Complete))
                    _SeenCompletedIds.Add(m.Id);
                foreach (Mission m in _Connection.Missions.Where(m => m.Status == MissionStatusEnum.Failed))
                    _SeenFailedIds.Add(m.Id);
                _FirstPoll = false;
            }
        }

        private int GetStatusCount(ArmadaStatus status, string key)
        {
            return status.MissionsByStatus.TryGetValue(key, out int count) ? count : 0;
        }

        private string BuildCaptainChain(Captain captain, Dictionary<string, Mission> missionLookup, Dictionary<string, Vessel> vesselLookup)
        {
            string result = captain.Name;
            if (!string.IsNullOrEmpty(captain.CurrentMissionId) && missionLookup.TryGetValue(captain.CurrentMissionId, out Mission? m))
            {
                if (!string.IsNullOrEmpty(m.VesselId) && vesselLookup.TryGetValue(m.VesselId, out Vessel? v))
                    result += " > " + v.Name;
                string title = m.Title.Length > 50 ? m.Title.Substring(0, 47) + "..." : m.Title;
                result += " > \"" + title + "\"";
            }
            return result;
        }

        private string BuildMissionChain(Mission mission, Dictionary<string, Captain> captainLookup, Dictionary<string, Vessel> vesselLookup)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(mission.CaptainId) && captainLookup.TryGetValue(mission.CaptainId, out Captain? c))
                parts.Add(c.Name);
            if (!string.IsNullOrEmpty(mission.VesselId) && vesselLookup.TryGetValue(mission.VesselId, out Vessel? v))
                parts.Add(v.Name);
            string title = mission.Title.Length > 50 ? mission.Title.Substring(0, 47) + "..." : mission.Title;
            parts.Add("\"" + title + "\"");
            return string.Join(" > ", parts);
        }

        private string FormatAge(DateTime? utc)
        {
            if (!utc.HasValue) return "never";
            return FormatTimeSince(utc.Value);
        }

        private string FormatTimeSince(DateTime utc)
        {
            TimeSpan age = DateTime.UtcNow - utc;
            if (age.TotalSeconds < 60) return (int)age.TotalSeconds + "s";
            if (age.TotalMinutes < 60) return (int)age.TotalMinutes + "m";
            if (age.TotalHours < 24) return (int)age.TotalHours + "h";
            return (int)age.TotalDays + "d";
        }

        #endregion
    }

    #region Display-Models

    /// <summary>
    /// Display model for voyage progress in dashboard.
    /// </summary>
    public class VoyageProgressItem
    {
        /// <summary>Voyage ID.</summary>
        public string VoyageId { get; set; } = "";

        /// <summary>Voyage title.</summary>
        public string Title { get; set; } = "";

        /// <summary>Completed mission count.</summary>
        public int CompletedMissions { get; set; }

        /// <summary>Total mission count.</summary>
        public int TotalMissions { get; set; }

        /// <summary>Failed mission count.</summary>
        public int FailedMissions { get; set; }

        /// <summary>In-progress mission count.</summary>
        public int InProgressMissions { get; set; }

        /// <summary>Progress percentage.</summary>
        public double Progress { get; set; }

        /// <summary>Progress display string.</summary>
        public string ProgressText => $"{CompletedMissions}/{TotalMissions}";

        /// <summary>Failed text.</summary>
        public string? FailedText => FailedMissions > 0 ? $"({FailedMissions} failed)" : null;
    }

    /// <summary>
    /// Display model for action required items.
    /// </summary>
    public class ActionItem
    {
        /// <summary>Severity: Stalled, Failed, Done.</summary>
        public string Severity { get; set; } = "";

        /// <summary>Main title/chain.</summary>
        public string Title { get; set; } = "";

        /// <summary>Detail line.</summary>
        public string Detail { get; set; } = "";

        /// <summary>Suggested action.</summary>
        public string? Suggestion { get; set; }

        /// <summary>Captain ID for stop action.</summary>
        public string? CaptainId { get; set; }

        /// <summary>Mission ID for retry action.</summary>
        public string? MissionId { get; set; }
    }

    /// <summary>
    /// Display model for recent signals.
    /// </summary>
    public class SignalItem
    {
        /// <summary>Timestamp string.</summary>
        public string Timestamp { get; set; } = "";

        /// <summary>Signal type.</summary>
        public string Type { get; set; } = "";

        /// <summary>Source.</summary>
        public string From { get; set; } = "";
    }

    /// <summary>
    /// Display model for captain cards.
    /// </summary>
    public class CaptainItem
    {
        /// <summary>Captain name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Captain state.</summary>
        public CaptainStateEnum State { get; set; }

        /// <summary>Runtime type.</summary>
        public AgentRuntimeEnum Runtime { get; set; }

        /// <summary>Current mission title.</summary>
        public string MissionTitle { get; set; } = "";

        /// <summary>Current vessel name.</summary>
        public string VesselName { get; set; } = "";

        /// <summary>Heartbeat age string.</summary>
        public string HeartbeatAge { get; set; } = "";

        /// <summary>State display string.</summary>
        public string StateText => State.ToString();
    }

    #endregion
}
