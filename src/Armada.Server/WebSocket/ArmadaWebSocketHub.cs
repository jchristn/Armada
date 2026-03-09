namespace Armada.Server.WebSocket
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using SyslogLogging;
    using SwiftStack;
    using SwiftStack.Websockets;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;

    /// <summary>
    /// WebSocket hub for real-time event broadcasting.
    /// Supports subscribe/command routes and broadcasts mission/captain state changes.
    /// Provides full command parity with the REST API and MCP tools.
    /// </summary>
    public class ArmadaWebSocketHub
    {
        #region Private-Members

        private string _Header = "[WebSocketHub] ";
        private LoggingModule _Logging;
        private SwiftStackApp _App;
        private WebsocketsApp _WsApp;
        private IAdmiralService _Admiral;
        private DatabaseDriver _Database;
        private IMergeQueueService _MergeQueue;
        private ArmadaSettings? _Settings;
        private IGitService? _Git;
        private Action? _OnStop;

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the WebSocket hub.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="app">SwiftStack application instance.</param>
        /// <param name="port">WebSocket port.</param>
        /// <param name="ssl">Whether to enable SSL/TLS.</param>
        /// <param name="admiral">Admiral service for command handling.</param>
        /// <param name="database">Database driver for data access.</param>
        /// <param name="mergeQueue">Merge queue service.</param>
        /// <param name="settings">Optional Armada settings for log/diff paths.</param>
        /// <param name="git">Optional git service for diff generation.</param>
        /// <param name="onStop">Optional callback invoked when stop_server is requested.</param>
        public ArmadaWebSocketHub(LoggingModule logging, SwiftStackApp app, int port, bool ssl, IAdmiralService admiral, DatabaseDriver database, IMergeQueueService mergeQueue, ArmadaSettings? settings = null, IGitService? git = null, Action? onStop = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _App = app ?? throw new ArgumentNullException(nameof(app));
            _Admiral = admiral ?? throw new ArgumentNullException(nameof(admiral));
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _MergeQueue = mergeQueue ?? throw new ArgumentNullException(nameof(mergeQueue));
            _Settings = settings;
            _Git = git;
            _OnStop = onStop;

            _WsApp = new WebsocketsApp(_App);
            _WsApp.WebsocketSettings = new WatsonWebsocket.WebsocketSettings
            {
                Hostnames = new List<string> { "localhost" },
                Port = port,
                Ssl = ssl
            };
            _WsApp.QuietStartup = true;

            _WsApp.OnConnection += (sender, args) =>
            {
                _Logging.Info(_Header + "client connected: " + args.Client.IpPort);
            };

            _WsApp.OnDisconnection += (sender, args) =>
            {
                _Logging.Info(_Header + "client disconnected: " + args.Client.IpPort);
            };

            RegisterRoutes();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the WebSocket server.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public async Task StartAsync(CancellationToken token = default)
        {
            await _WsApp.Run(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Broadcast a mission state change to all connected clients.
        /// </summary>
        /// <param name="missionId">Mission ID.</param>
        /// <param name="status">New status.</param>
        /// <param name="title">Mission title.</param>
        public void BroadcastMissionChange(string missionId, string status, string? title = null)
        {
            object payload = new
            {
                type = "mission.changed",
                missionId = missionId,
                status = status,
                title = title,
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a captain state change to all connected clients.
        /// </summary>
        /// <param name="captainId">Captain ID.</param>
        /// <param name="state">New state.</param>
        /// <param name="name">Captain name.</param>
        public void BroadcastCaptainChange(string captainId, string state, string? name = null)
        {
            object payload = new
            {
                type = "captain.changed",
                captainId = captainId,
                state = state,
                name = name,
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        /// <summary>
        /// Broadcast a generic event to all connected clients.
        /// </summary>
        /// <param name="eventType">Event type string.</param>
        /// <param name="message">Event message.</param>
        /// <param name="data">Optional additional data.</param>
        public void BroadcastEvent(string eventType, string message, object? data = null)
        {
            object payload = new
            {
                type = eventType,
                message = message,
                data = data,
                timestamp = DateTime.UtcNow
            };

            BroadcastEvent(payload);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Reads all text from a file using FileShare.ReadWrite to avoid locking conflicts with writer processes.
        /// </summary>
        private async Task<string> ReadFileSharedAsync(string path)
        {
            using System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            using System.IO.StreamReader reader = new System.IO.StreamReader(fs);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Reads all lines from a file using FileShare.ReadWrite to avoid locking conflicts with writer processes.
        /// </summary>
        private async Task<string[]> ReadLinesSharedAsync(string path)
        {
            List<string> lines = new List<string>();
            using System.IO.FileStream fs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            using System.IO.StreamReader reader = new System.IO.StreamReader(fs);
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                lines.Add(line);
            }
            return lines.ToArray();
        }

        private void RegisterRoutes()
        {
            // Subscribe route — clients connect here to receive broadcasts.
            // On connect, send current status as initial payload.
            _WsApp.AddRoute("subscribe", async (msg, token) =>
            {
                try
                {
                    ArmadaStatus status = await _Admiral.GetStatusAsync().ConfigureAwait(false);
                    object initial = new
                    {
                        type = "status.snapshot",
                        data = status,
                        timestamp = DateTime.UtcNow
                    };

                    string json = JsonSerializer.Serialize(initial, _JsonOptions);
                    await msg.RespondAsync(json).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error sending initial status: " + ex.Message);
                }
            });

            // Command route — clients can send commands to the Admiral.
            _WsApp.AddRoute("command", async (msg, token) =>
            {
                try
                {
                    string body = msg.DataAsString();
                    WebSocketCommand command = JsonSerializer.Deserialize<WebSocketCommand>(body, _JsonOptions) ?? new WebSocketCommand();
                    string action = command.Action;

                    object result;

                    switch (action)
                    {
                        // ── Status & Control ──────────────────────────────────────

                        case "status":
                            ArmadaStatus cmdStatus = await _Admiral.GetStatusAsync().ConfigureAwait(false);
                            result = new { type = "command.result", action = "status", data = (object)cmdStatus };
                            break;

                        case "stop_captain":
                            string captainId = command.CaptainId ?? "";
                            await _Admiral.RecallCaptainAsync(captainId).ConfigureAwait(false);
                            result = new { type = "command.result", action = "stop_captain", data = (object)new { status = "stopped", captainId = captainId } };
                            break;

                        case "stop_all":
                            await _Admiral.RecallAllAsync().ConfigureAwait(false);
                            result = new { type = "command.result", action = "stop_all", data = (object)new { status = "all_stopped" } };
                            break;

                        case "stop_server":
                            if (_OnStop != null)
                            {
                                _ = Task.Run(async () =>
                                {
                                    await Task.Delay(500).ConfigureAwait(false);
                                    _OnStop();
                                });
                            }
                            result = new { type = "command.result", action = "stop_server", data = (object)new { status = "shutting_down" } };
                            break;

                        // ── Fleet actions ──────────────────────────────────────────

                        case "list_fleets":
                            EnumerationQuery fleetQuery = command.Query ?? new EnumerationQuery();
                            Stopwatch fleetSw = Stopwatch.StartNew();
                            EnumerationResult<Fleet> fleetResult = await _Database.Fleets.EnumerateAsync(fleetQuery).ConfigureAwait(false);
                            fleetResult.TotalMs = Math.Round(fleetSw.Elapsed.TotalMilliseconds, 2);
                            result = new { type = "command.result", action = "list_fleets", data = (object)fleetResult };
                            break;

                        case "get_fleet":
                            string getFleetId = command.Id ?? "";
                            Fleet? foundFleet = await _Database.Fleets.ReadAsync(getFleetId).ConfigureAwait(false);
                            if (foundFleet == null)
                                result = new { type = "command.error", action = "get_fleet", error = "Fleet not found" };
                            else
                            {
                                List<Vessel> fleetVessels = await _Database.Vessels.EnumerateByFleetAsync(getFleetId).ConfigureAwait(false);
                                result = new { type = "command.result", action = "get_fleet", data = (object)new { Fleet = foundFleet, Vessels = fleetVessels } };
                            }
                            break;

                        case "create_fleet":
                            Fleet newFleet = JsonSerializer.Deserialize<WebSocketDataCommand<Fleet>>(body, _JsonOptions)?.Data!;
                            newFleet = await _Database.Fleets.CreateAsync(newFleet).ConfigureAwait(false);
                            result = new { type = "command.result", action = "create_fleet", data = (object)newFleet };
                            break;

                        case "update_fleet":
                            string updFleetId = command.Id ?? "";
                            Fleet? existFleet = await _Database.Fleets.ReadAsync(updFleetId).ConfigureAwait(false);
                            if (existFleet == null)
                                result = new { type = "command.error", action = "update_fleet", error = "Fleet not found" };
                            else
                            {
                                Fleet updFleet = JsonSerializer.Deserialize<WebSocketDataCommand<Fleet>>(body, _JsonOptions)?.Data!;
                                updFleet.Id = updFleetId;
                                updFleet = await _Database.Fleets.UpdateAsync(updFleet).ConfigureAwait(false);
                                result = new { type = "command.result", action = "update_fleet", data = (object)updFleet };
                            }
                            break;

                        case "delete_fleet":
                            string delFleetId = command.Id ?? "";
                            await _Database.Fleets.DeleteAsync(delFleetId).ConfigureAwait(false);
                            result = new { type = "command.result", action = "delete_fleet", data = (object)new { status = "deleted" } };
                            break;

                        // ── Vessel actions ─────────────────────────────────────────

                        case "list_vessels":
                            EnumerationQuery vesselQuery = command.Query ?? new EnumerationQuery();
                            Stopwatch vesselSw = Stopwatch.StartNew();
                            EnumerationResult<Vessel> vesselResult = await _Database.Vessels.EnumerateAsync(vesselQuery).ConfigureAwait(false);
                            vesselResult.TotalMs = Math.Round(vesselSw.Elapsed.TotalMilliseconds, 2);
                            result = new { type = "command.result", action = "list_vessels", data = (object)vesselResult };
                            break;

                        case "get_vessel":
                            string getVesselId = command.Id ?? "";
                            Vessel? foundVessel = await _Database.Vessels.ReadAsync(getVesselId).ConfigureAwait(false);
                            if (foundVessel == null)
                                result = new { type = "command.error", action = "get_vessel", error = "Vessel not found" };
                            else
                                result = new { type = "command.result", action = "get_vessel", data = (object)foundVessel };
                            break;

                        case "create_vessel":
                            Vessel newVessel = JsonSerializer.Deserialize<WebSocketDataCommand<Vessel>>(body, _JsonOptions)?.Data!;
                            newVessel = await _Database.Vessels.CreateAsync(newVessel).ConfigureAwait(false);
                            result = new { type = "command.result", action = "create_vessel", data = (object)newVessel };
                            break;

                        case "update_vessel":
                            string updVesselId = command.Id ?? "";
                            Vessel? existVessel = await _Database.Vessels.ReadAsync(updVesselId).ConfigureAwait(false);
                            if (existVessel == null)
                                result = new { type = "command.error", action = "update_vessel", error = "Vessel not found" };
                            else
                            {
                                Vessel updVessel = JsonSerializer.Deserialize<WebSocketDataCommand<Vessel>>(body, _JsonOptions)?.Data!;
                                updVessel.Id = updVesselId;
                                updVessel = await _Database.Vessels.UpdateAsync(updVessel).ConfigureAwait(false);
                                result = new { type = "command.result", action = "update_vessel", data = (object)updVessel };
                            }
                            break;

                        case "update_vessel_context":
                            string ctxVesselId = command.Id ?? "";
                            Vessel? ctxVessel = await _Database.Vessels.ReadAsync(ctxVesselId).ConfigureAwait(false);
                            if (ctxVessel == null)
                                result = new { type = "command.error", action = "update_vessel_context", error = "Vessel not found" };
                            else
                            {
                                Vessel ctxPatch = JsonSerializer.Deserialize<WebSocketDataCommand<Vessel>>(body, _JsonOptions)?.Data!;
                                if (ctxPatch.ProjectContext != null)
                                    ctxVessel.ProjectContext = ctxPatch.ProjectContext;
                                if (ctxPatch.StyleGuide != null)
                                    ctxVessel.StyleGuide = ctxPatch.StyleGuide;
                                ctxVessel = await _Database.Vessels.UpdateAsync(ctxVessel).ConfigureAwait(false);
                                result = new { type = "command.result", action = "update_vessel_context", data = (object)ctxVessel };
                            }
                            break;

                        case "delete_vessel":
                            string delVesselId = command.Id ?? "";
                            await _Database.Vessels.DeleteAsync(delVesselId).ConfigureAwait(false);
                            result = new { type = "command.result", action = "delete_vessel", data = (object)new { status = "deleted" } };
                            break;

                        // ── Voyage actions ─────────────────────────────────────────

                        case "list_voyages":
                            EnumerationQuery voyageQuery = command.Query ?? new EnumerationQuery();
                            Stopwatch voyageSw = Stopwatch.StartNew();
                            EnumerationResult<Voyage> voyageResult = await _Database.Voyages.EnumerateAsync(voyageQuery).ConfigureAwait(false);
                            voyageResult.TotalMs = Math.Round(voyageSw.Elapsed.TotalMilliseconds, 2);
                            result = new { type = "command.result", action = "list_voyages", data = (object)voyageResult };
                            break;

                        case "get_voyage":
                            string getVoyageId = command.Id ?? "";
                            Voyage? foundVoyage = await _Database.Voyages.ReadAsync(getVoyageId).ConfigureAwait(false);
                            if (foundVoyage == null)
                                result = new { type = "command.error", action = "get_voyage", error = "Voyage not found" };
                            else
                            {
                                List<Mission> voyageMissions = await _Database.Missions.EnumerateByVoyageAsync(getVoyageId).ConfigureAwait(false);
                                result = new { type = "command.result", action = "get_voyage", data = (object)new { voyage = foundVoyage, missions = voyageMissions } };
                            }
                            break;

                        case "create_voyage":
                            WebSocketVoyageData voyageData = JsonSerializer.Deserialize<WebSocketDataCommand<WebSocketVoyageData>>(body, _JsonOptions)?.Data ?? new WebSocketVoyageData();
                            string voyTitle = voyageData.Title ?? "";
                            string voyDesc = voyageData.Description ?? "";
                            string voyVesselId = voyageData.VesselId ?? "";

                            List<MissionDescription> missionDescs = voyageData.Missions ?? new List<MissionDescription>();

                            Voyage createdVoyage;
                            if (String.IsNullOrEmpty(voyVesselId) || missionDescs.Count == 0)
                            {
                                createdVoyage = new Voyage(voyTitle, voyDesc);
                                createdVoyage = await _Database.Voyages.CreateAsync(createdVoyage).ConfigureAwait(false);
                            }
                            else
                            {
                                createdVoyage = await _Admiral.DispatchVoyageAsync(voyTitle, voyDesc, voyVesselId, missionDescs).ConfigureAwait(false);
                            }
                            result = new { type = "command.result", action = "create_voyage", data = (object)createdVoyage };
                            break;

                        case "cancel_voyage":
                            string cvId = command.Id ?? "";
                            Voyage? cvVoyage = await _Database.Voyages.ReadAsync(cvId).ConfigureAwait(false);
                            if (cvVoyage == null)
                                result = new { type = "command.error", action = "cancel_voyage", error = "Voyage not found" };
                            else
                            {
                                cvVoyage.Status = VoyageStatusEnum.Cancelled;
                                cvVoyage.CompletedUtc = DateTime.UtcNow;
                                cvVoyage.LastUpdateUtc = DateTime.UtcNow;
                                await _Database.Voyages.UpdateAsync(cvVoyage).ConfigureAwait(false);
                                List<Mission> cvMissions = await _Database.Missions.EnumerateByVoyageAsync(cvId).ConfigureAwait(false);
                                foreach (Mission m in cvMissions)
                                {
                                    if (m.Status == MissionStatusEnum.Pending || m.Status == MissionStatusEnum.Assigned)
                                    {
                                        if (!String.IsNullOrEmpty(m.CaptainId))
                                        {
                                            Captain? captain = await _Database.Captains.ReadAsync(m.CaptainId).ConfigureAwait(false);
                                            if (captain != null && captain.CurrentMissionId == m.Id)
                                            {
                                                List<Mission> otherMissions = (await _Database.Missions.EnumerateByCaptainAsync(captain.Id).ConfigureAwait(false))
                                                    .Where(om => om.Id != m.Id && (om.Status == MissionStatusEnum.InProgress || om.Status == MissionStatusEnum.Assigned)).ToList();
                                                if (otherMissions.Count == 0)
                                                {
                                                    captain.State = CaptainStateEnum.Idle;
                                                    captain.CurrentMissionId = null;
                                                    captain.CurrentDockId = null;
                                                    captain.ProcessId = null;
                                                    captain.RecoveryAttempts = 0;
                                                    captain.LastUpdateUtc = DateTime.UtcNow;
                                                    await _Database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                                                }
                                            }
                                        }

                                        m.Status = MissionStatusEnum.Cancelled;
                                        m.CompletedUtc = DateTime.UtcNow;
                                        m.LastUpdateUtc = DateTime.UtcNow;
                                        await _Database.Missions.UpdateAsync(m).ConfigureAwait(false);
                                    }
                                }
                                int cvCancelled = cvMissions.Count(m => m.Status == MissionStatusEnum.Cancelled);
                                result = new { type = "command.result", action = "cancel_voyage", data = (object)new { Voyage = cvVoyage, CancelledMissions = cvCancelled } };
                            }
                            break;

                        case "purge_voyage":
                            string pvId = command.Id ?? "";
                            Voyage? pvVoyage = await _Database.Voyages.ReadAsync(pvId).ConfigureAwait(false);
                            if (pvVoyage == null)
                                result = new { type = "command.error", action = "purge_voyage", error = "Voyage not found" };
                            else if (pvVoyage.Status == VoyageStatusEnum.Open || pvVoyage.Status == VoyageStatusEnum.InProgress)
                                result = new { type = "command.error", action = "purge_voyage", error = "Cannot delete voyage while status is " + pvVoyage.Status + ". Cancel the voyage first." };
                            else
                            {
                                List<Mission> pvMissions = await _Database.Missions.EnumerateByVoyageAsync(pvId).ConfigureAwait(false);
                                int pvActiveCount = pvMissions.Count(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress);
                                if (pvActiveCount > 0)
                                    result = new { type = "command.error", action = "purge_voyage", error = "Cannot delete voyage with " + pvActiveCount + " active mission(s) in Assigned or InProgress status. Cancel or complete them first." };
                                else
                                {
                                    foreach (Mission m in pvMissions)
                                        await _Database.Missions.DeleteAsync(m.Id).ConfigureAwait(false);
                                    await _Database.Voyages.DeleteAsync(pvId).ConfigureAwait(false);
                                    result = new { type = "command.result", action = "purge_voyage", data = (object)new { status = "deleted", voyageId = pvId, missionsDeleted = pvMissions.Count } };
                                }
                            }
                            break;

                        // ── Mission actions ────────────────────────────────────────

                        case "list_missions":
                            EnumerationQuery missionQuery = command.Query ?? new EnumerationQuery();
                            Stopwatch missionSw = Stopwatch.StartNew();
                            EnumerationResult<Mission> missionResult = await _Database.Missions.EnumerateAsync(missionQuery).ConfigureAwait(false);
                            missionResult.TotalMs = Math.Round(missionSw.Elapsed.TotalMilliseconds, 2);
                            result = new { type = "command.result", action = "list_missions", data = (object)missionResult };
                            break;

                        case "get_mission":
                            string getMissionId = command.Id ?? "";
                            Mission? foundMission = await _Database.Missions.ReadAsync(getMissionId).ConfigureAwait(false);
                            if (foundMission == null)
                                result = new { type = "command.error", action = "get_mission", error = "Mission not found" };
                            else
                                result = new { type = "command.result", action = "get_mission", data = (object)foundMission };
                            break;

                        case "create_mission":
                            Mission newMission = JsonSerializer.Deserialize<WebSocketDataCommand<Mission>>(body, _JsonOptions)?.Data!;
                            newMission = await _Admiral.DispatchMissionAsync(newMission).ConfigureAwait(false);
                            result = new { type = "command.result", action = "create_mission", data = (object)newMission };
                            break;

                        case "update_mission":
                            string updMissionId = command.Id ?? "";
                            Mission? existMission = await _Database.Missions.ReadAsync(updMissionId).ConfigureAwait(false);
                            if (existMission == null)
                                result = new { type = "command.error", action = "update_mission", error = "Mission not found" };
                            else
                            {
                                Mission updMission = JsonSerializer.Deserialize<WebSocketDataCommand<Mission>>(body, _JsonOptions)?.Data!;
                                updMission.Id = updMissionId;
                                updMission = await _Database.Missions.UpdateAsync(updMission).ConfigureAwait(false);
                                result = new { type = "command.result", action = "update_mission", data = (object)updMission };
                            }
                            break;

                        case "transition_mission_status":
                            string tmId = command.Id ?? "";
                            string tmStatus = command.Status ?? "";
                            Mission? tmMission = await _Database.Missions.ReadAsync(tmId).ConfigureAwait(false);
                            if (tmMission == null)
                            {
                                result = new { type = "command.error", action = "transition_mission_status", error = "Mission not found" };
                            }
                            else if (!Enum.TryParse<MissionStatusEnum>(tmStatus, true, out MissionStatusEnum tmNewStatus))
                            {
                                result = new { type = "command.error", action = "transition_mission_status", error = "Invalid status: " + tmStatus };
                            }
                            else if (!IsValidTransition(tmMission.Status, tmNewStatus))
                            {
                                result = new { type = "command.error", action = "transition_mission_status", error = "Invalid transition from " + tmMission.Status + " to " + tmNewStatus };
                            }
                            else
                            {
                                tmMission.Status = tmNewStatus;
                                tmMission.LastUpdateUtc = DateTime.UtcNow;
                                if (tmNewStatus == MissionStatusEnum.Complete || tmNewStatus == MissionStatusEnum.Failed || tmNewStatus == MissionStatusEnum.Cancelled)
                                    tmMission.CompletedUtc = DateTime.UtcNow;
                                await _Database.Missions.UpdateAsync(tmMission).ConfigureAwait(false);
                                Signal tmSignal = new Signal(SignalTypeEnum.Progress, "Mission " + tmId + " transitioned to " + tmNewStatus);
                                if (!String.IsNullOrEmpty(tmMission.CaptainId)) tmSignal.FromCaptainId = tmMission.CaptainId;
                                await _Database.Signals.CreateAsync(tmSignal).ConfigureAwait(false);
                                result = new { type = "command.result", action = "transition_mission_status", data = (object)tmMission };
                            }
                            break;

                        case "cancel_mission":
                            string cmId = command.Id ?? "";
                            Mission? cmMission = await _Database.Missions.ReadAsync(cmId).ConfigureAwait(false);
                            if (cmMission == null)
                                result = new { type = "command.error", action = "cancel_mission", error = "Mission not found" };
                            else
                            {
                                if (!String.IsNullOrEmpty(cmMission.CaptainId))
                                {
                                    Captain? cmCaptain = await _Database.Captains.ReadAsync(cmMission.CaptainId).ConfigureAwait(false);
                                    if (cmCaptain != null && cmCaptain.CurrentMissionId == cmMission.Id)
                                    {
                                        List<Mission> cmOther = (await _Database.Missions.EnumerateByCaptainAsync(cmCaptain.Id).ConfigureAwait(false))
                                            .Where(om => om.Id != cmMission.Id && (om.Status == MissionStatusEnum.InProgress || om.Status == MissionStatusEnum.Assigned)).ToList();
                                        if (cmOther.Count == 0)
                                        {
                                            cmCaptain.State = CaptainStateEnum.Idle;
                                            cmCaptain.CurrentMissionId = null;
                                            cmCaptain.CurrentDockId = null;
                                            cmCaptain.ProcessId = null;
                                            cmCaptain.RecoveryAttempts = 0;
                                            cmCaptain.LastUpdateUtc = DateTime.UtcNow;
                                            await _Database.Captains.UpdateAsync(cmCaptain).ConfigureAwait(false);
                                        }
                                    }
                                }

                                cmMission.Status = MissionStatusEnum.Cancelled;
                                cmMission.CompletedUtc = DateTime.UtcNow;
                                cmMission.LastUpdateUtc = DateTime.UtcNow;
                                cmMission = await _Database.Missions.UpdateAsync(cmMission).ConfigureAwait(false);
                                result = new { type = "command.result", action = "cancel_mission", data = (object)cmMission };
                            }
                            break;

                        case "purge_mission":
                            string pmId = command.Id ?? "";
                            Mission? pmMission = await _Database.Missions.ReadAsync(pmId).ConfigureAwait(false);
                            if (pmMission == null)
                                result = new { type = "command.error", action = "purge_mission", error = "Mission not found" };
                            else
                            {
                                await _Database.Missions.DeleteAsync(pmId).ConfigureAwait(false);
                                result = new { type = "command.result", action = "purge_mission", data = (object)new { status = "deleted", missionId = pmId } };
                            }
                            break;

                        case "restart_mission":
                            string rmId = command.Id ?? "";
                            Mission? rmMission = await _Database.Missions.ReadAsync(rmId).ConfigureAwait(false);
                            if (rmMission == null)
                                result = new { type = "command.error", action = "restart_mission", error = "Mission not found" };
                            else if (rmMission.Status != MissionStatusEnum.Failed && rmMission.Status != MissionStatusEnum.Cancelled)
                                result = new { type = "command.error", action = "restart_mission", error = "Only Failed or Cancelled missions can be restarted" };
                            else
                            {
                                WebSocketDataCommand<MissionRestartData>? rmData = null;
                                try { rmData = JsonSerializer.Deserialize<WebSocketDataCommand<MissionRestartData>>(body, _JsonOptions); } catch { }
                                if (rmData?.Data != null)
                                {
                                    if (!String.IsNullOrEmpty(rmData.Data.Title)) rmMission.Title = rmData.Data.Title;
                                    if (!String.IsNullOrEmpty(rmData.Data.Description)) rmMission.Description = rmData.Data.Description;
                                }

                                rmMission.Status = MissionStatusEnum.Pending;
                                rmMission.CaptainId = null;
                                rmMission.BranchName = null;
                                rmMission.PrUrl = null;
                                rmMission.StartedUtc = null;
                                rmMission.CompletedUtc = null;
                                rmMission.LastUpdateUtc = DateTime.UtcNow;
                                rmMission = await _Database.Missions.UpdateAsync(rmMission).ConfigureAwait(false);

                                Signal rmSignal = new Signal(SignalTypeEnum.Progress, "Mission " + rmId + " restarted");
                                await _Database.Signals.CreateAsync(rmSignal).ConfigureAwait(false);

                                result = new { type = "command.result", action = "restart_mission", data = (object)rmMission };
                            }
                            break;

                        case "get_mission_diff":
                            string mdId = command.Id ?? "";
                            Mission? mdMission = await _Database.Missions.ReadAsync(mdId).ConfigureAwait(false);
                            if (mdMission == null)
                                result = new { type = "command.error", action = "get_mission_diff", error = "Mission not found" };
                            else if (_Settings == null)
                                result = new { type = "command.error", action = "get_mission_diff", error = "Diff not available — settings not configured" };
                            else
                            {
                                string savedDiffPath = System.IO.Path.Combine(_Settings.LogDirectory, "diffs", mdId + ".diff");
                                if (System.IO.File.Exists(savedDiffPath))
                                {
                                    string savedDiff = await ReadFileSharedAsync(savedDiffPath).ConfigureAwait(false);
                                    result = new { type = "command.result", action = "get_mission_diff", data = (object)new { MissionId = mdId, Branch = mdMission.BranchName ?? "", Diff = savedDiff } };
                                }
                                else if (!String.IsNullOrEmpty(mdMission.DiffSnapshot))
                                {
                                    result = new { type = "command.result", action = "get_mission_diff", data = (object)new { MissionId = mdId, Branch = mdMission.BranchName ?? "", Diff = mdMission.DiffSnapshot } };
                                }
                                else
                                {
                                    Dock? mdDock = null;
                                    if (!String.IsNullOrEmpty(mdMission.DockId))
                                    {
                                        mdDock = await _Database.Docks.ReadAsync(mdMission.DockId).ConfigureAwait(false);
                                    }
                                    if (mdDock == null && !String.IsNullOrEmpty(mdMission.CaptainId))
                                    {
                                        Captain? mdCaptain = await _Database.Captains.ReadAsync(mdMission.CaptainId).ConfigureAwait(false);
                                        if (mdCaptain != null && !String.IsNullOrEmpty(mdCaptain.CurrentDockId))
                                            mdDock = await _Database.Docks.ReadAsync(mdCaptain.CurrentDockId).ConfigureAwait(false);
                                    }
                                    if (mdDock == null && !String.IsNullOrEmpty(mdMission.BranchName) && !String.IsNullOrEmpty(mdMission.VesselId))
                                    {
                                        List<Dock> mdDocks = await _Database.Docks.EnumerateByVesselAsync(mdMission.VesselId).ConfigureAwait(false);
                                        mdDock = mdDocks.FirstOrDefault(d => d.BranchName == mdMission.BranchName && d.Active);
                                    }
                                    if (mdDock == null || String.IsNullOrEmpty(mdDock.WorktreePath) || !System.IO.Directory.Exists(mdDock.WorktreePath))
                                        result = new { type = "command.error", action = "get_mission_diff", error = "No diff available — worktree was already reclaimed and no saved diff exists" };
                                    else if (_Git == null)
                                        result = new { type = "command.error", action = "get_mission_diff", error = "Git service not available" };
                                    else
                                    {
                                        string baseBranch = "main";
                                        if (!String.IsNullOrEmpty(mdMission.VesselId))
                                        {
                                            Vessel? mdVessel = await _Database.Vessels.ReadAsync(mdMission.VesselId).ConfigureAwait(false);
                                            if (mdVessel != null) baseBranch = mdVessel.DefaultBranch;
                                        }
                                        string diff = await _Git.DiffAsync(mdDock.WorktreePath, baseBranch).ConfigureAwait(false);
                                        result = new { type = "command.result", action = "get_mission_diff", data = (object)new { MissionId = mdId, Branch = mdDock.BranchName ?? "", Diff = diff } };
                                    }
                                }
                            }
                            break;

                        case "get_mission_log":
                            string mlId = command.Id ?? "";
                            Mission? mlMission = await _Database.Missions.ReadAsync(mlId).ConfigureAwait(false);
                            if (mlMission == null)
                                result = new { type = "command.error", action = "get_mission_log", error = "Mission not found" };
                            else if (_Settings == null)
                                result = new { type = "command.error", action = "get_mission_log", error = "Logs not available — settings not configured" };
                            else
                            {
                                string mlLogPath = System.IO.Path.Combine(_Settings.LogDirectory, "missions", mlId + ".log");
                                if (!System.IO.File.Exists(mlLogPath))
                                    result = new { type = "command.result", action = "get_mission_log", data = (object)new { MissionId = mlId, Log = "", Lines = 0, TotalLines = 0 } };
                                else
                                {
                                    string[] mlAllLines = await ReadLinesSharedAsync(mlLogPath).ConfigureAwait(false);
                                    int mlTotalLines = mlAllLines.Length;
                                    int mlOffset = command.Offset ?? 0;
                                    int mlLineCount = command.Lines ?? 100;
                                    string[] mlSlice = mlAllLines.Skip(mlOffset).Take(mlLineCount).ToArray();
                                    string mlLog = String.Join("\n", mlSlice);
                                    result = new { type = "command.result", action = "get_mission_log", data = (object)new { MissionId = mlId, Log = mlLog, Lines = mlSlice.Length, TotalLines = mlTotalLines } };
                                }
                            }
                            break;

                        // ── Captain actions ────────────────────────────────────────

                        case "list_captains":
                            EnumerationQuery captainQuery = command.Query ?? new EnumerationQuery();
                            Stopwatch captainSw = Stopwatch.StartNew();
                            EnumerationResult<Captain> captainResult = await _Database.Captains.EnumerateAsync(captainQuery).ConfigureAwait(false);
                            captainResult.TotalMs = Math.Round(captainSw.Elapsed.TotalMilliseconds, 2);
                            result = new { type = "command.result", action = "list_captains", data = (object)captainResult };
                            break;

                        case "get_captain":
                            string getCaptainId = command.Id ?? "";
                            Captain? foundCaptain = await _Database.Captains.ReadAsync(getCaptainId).ConfigureAwait(false);
                            if (foundCaptain == null)
                                result = new { type = "command.error", action = "get_captain", error = "Captain not found" };
                            else
                                result = new { type = "command.result", action = "get_captain", data = (object)foundCaptain };
                            break;

                        case "create_captain":
                            Captain newCaptain = JsonSerializer.Deserialize<WebSocketDataCommand<Captain>>(body, _JsonOptions)?.Data!;
                            newCaptain = await _Database.Captains.CreateAsync(newCaptain).ConfigureAwait(false);
                            result = new { type = "command.result", action = "create_captain", data = (object)newCaptain };
                            break;

                        case "update_captain":
                            string updCptId = command.Id ?? "";
                            Captain? existCpt = await _Database.Captains.ReadAsync(updCptId).ConfigureAwait(false);
                            if (existCpt == null)
                                result = new { type = "command.error", action = "update_captain", error = "Captain not found" };
                            else
                            {
                                Captain updCpt = JsonSerializer.Deserialize<WebSocketDataCommand<Captain>>(body, _JsonOptions)?.Data!;
                                updCpt.Id = updCptId;
                                updCpt.State = existCpt.State;
                                updCpt.CurrentMissionId = existCpt.CurrentMissionId;
                                updCpt.CurrentDockId = existCpt.CurrentDockId;
                                updCpt.ProcessId = existCpt.ProcessId;
                                updCpt.RecoveryAttempts = existCpt.RecoveryAttempts;
                                updCpt.LastHeartbeatUtc = existCpt.LastHeartbeatUtc;
                                updCpt.CreatedUtc = existCpt.CreatedUtc;
                                updCpt.LastUpdateUtc = DateTime.UtcNow;
                                updCpt = await _Database.Captains.UpdateAsync(updCpt).ConfigureAwait(false);
                                result = new { type = "command.result", action = "update_captain", data = (object)updCpt };
                            }
                            break;

                        case "delete_captain":
                            string delCptId = command.Id ?? "";
                            Captain? delCpt = await _Database.Captains.ReadAsync(delCptId).ConfigureAwait(false);
                            if (delCpt == null)
                                result = new { type = "command.error", action = "delete_captain", error = "Captain not found" };
                            else if (delCpt.State == CaptainStateEnum.Working)
                                result = new { type = "command.error", action = "delete_captain", error = "Cannot delete captain while state is Working. Stop the captain first." };
                            else
                            {
                                List<Mission> delCptMissions = await _Database.Missions.EnumerateByCaptainAsync(delCptId).ConfigureAwait(false);
                                int delCptActiveCount = delCptMissions.Count(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress);
                                if (delCptActiveCount > 0)
                                    result = new { type = "command.error", action = "delete_captain", error = "Cannot delete captain with " + delCptActiveCount + " active mission(s) in Assigned or InProgress status. Cancel or complete them first." };
                                else
                                {
                                    await _Database.Captains.DeleteAsync(delCptId).ConfigureAwait(false);
                                    result = new { type = "command.result", action = "delete_captain", data = (object)new { status = "deleted" } };
                                }
                            }
                            break;

                        case "get_captain_log":
                            string clId = command.Id ?? "";
                            Captain? clCaptain = await _Database.Captains.ReadAsync(clId).ConfigureAwait(false);
                            if (clCaptain == null)
                                result = new { type = "command.error", action = "get_captain_log", error = "Captain not found" };
                            else if (_Settings == null)
                                result = new { type = "command.error", action = "get_captain_log", error = "Logs not available — settings not configured" };
                            else
                            {
                                string clPointerPath = System.IO.Path.Combine(_Settings.LogDirectory, "captains", clId + ".current");
                                string? clLogPath = null;
                                if (System.IO.File.Exists(clPointerPath))
                                {
                                    string clTarget = (await ReadFileSharedAsync(clPointerPath).ConfigureAwait(false)).Trim();
                                    if (System.IO.File.Exists(clTarget))
                                        clLogPath = clTarget;
                                }
                                if (clLogPath == null)
                                    result = new { type = "command.result", action = "get_captain_log", data = (object)new { CaptainId = clId, Log = "", Lines = 0, TotalLines = 0 } };
                                else
                                {
                                    string[] clAllLines = await ReadLinesSharedAsync(clLogPath).ConfigureAwait(false);
                                    int clTotalLines = clAllLines.Length;
                                    int clOffset = command.Offset ?? 0;
                                    int clLineCount = command.Lines ?? 100;
                                    string[] clSlice = clAllLines.Skip(clOffset).Take(clLineCount).ToArray();
                                    string clLog = String.Join("\n", clSlice);
                                    result = new { type = "command.result", action = "get_captain_log", data = (object)new { CaptainId = clId, Log = clLog, Lines = clSlice.Length, TotalLines = clTotalLines } };
                                }
                            }
                            break;

                        // ── Signal actions ─────────────────────────────────────────

                        case "list_signals":
                            EnumerationQuery signalQuery = command.Query ?? new EnumerationQuery();
                            Stopwatch signalSw = Stopwatch.StartNew();
                            EnumerationResult<Signal> signalResult = await _Database.Signals.EnumerateAsync(signalQuery).ConfigureAwait(false);
                            signalResult.TotalMs = Math.Round(signalSw.Elapsed.TotalMilliseconds, 2);
                            result = new { type = "command.result", action = "list_signals", data = (object)signalResult };
                            break;

                        case "send_signal":
                            Signal newSignal = JsonSerializer.Deserialize<WebSocketDataCommand<Signal>>(body, _JsonOptions)?.Data!;
                            newSignal = await _Database.Signals.CreateAsync(newSignal).ConfigureAwait(false);
                            result = new { type = "command.result", action = "send_signal", data = (object)newSignal };
                            break;

                        // ── Event actions ──────────────────────────────────────────

                        case "list_events":
                            EnumerationQuery eventQuery = command.Query ?? new EnumerationQuery();
                            Stopwatch eventSw = Stopwatch.StartNew();
                            EnumerationResult<ArmadaEvent> eventResult = await _Database.Events.EnumerateAsync(eventQuery).ConfigureAwait(false);
                            eventResult.TotalMs = Math.Round(eventSw.Elapsed.TotalMilliseconds, 2);
                            result = new { type = "command.result", action = "list_events", data = (object)eventResult };
                            break;

                        // ── Dock actions ───────────────────────────────────────────

                        case "list_docks":
                            EnumerationQuery dockQuery = command.Query ?? new EnumerationQuery();
                            Stopwatch dockSw = Stopwatch.StartNew();
                            EnumerationResult<Dock> dockResult = await _Database.Docks.EnumerateAsync(dockQuery).ConfigureAwait(false);
                            dockResult.TotalMs = Math.Round(dockSw.Elapsed.TotalMilliseconds, 2);
                            result = new { type = "command.result", action = "list_docks", data = (object)dockResult };
                            break;

                        // ── Merge Queue actions ───────────────────────────────────

                        case "list_merge_queue":
                            EnumerationQuery mergeQuery = command.Query ?? new EnumerationQuery();
                            Stopwatch mergeSw = Stopwatch.StartNew();
                            List<MergeEntry> mergeAll = await _MergeQueue.ListAsync().ConfigureAwait(false);
                            int mergeTotal = mergeAll.Count;
                            List<MergeEntry> mergePage = mergeAll.Skip(mergeQuery.Offset).Take(mergeQuery.PageSize).ToList();
                            EnumerationResult<MergeEntry> mergeResult = EnumerationResult<MergeEntry>.Create(mergeQuery, mergePage, mergeTotal);
                            mergeResult.TotalMs = Math.Round(mergeSw.Elapsed.TotalMilliseconds, 2);
                            result = new { type = "command.result", action = "list_merge_queue", data = (object)mergeResult };
                            break;

                        case "get_merge_entry":
                            string meId = command.Id ?? "";
                            MergeEntry? foundEntry = await _MergeQueue.GetAsync(meId).ConfigureAwait(false);
                            if (foundEntry == null)
                                result = new { type = "command.error", action = "get_merge_entry", error = "Merge entry not found" };
                            else
                                result = new { type = "command.result", action = "get_merge_entry", data = (object)foundEntry };
                            break;

                        case "enqueue_merge":
                            MergeEntry newEntry = JsonSerializer.Deserialize<WebSocketDataCommand<MergeEntry>>(body, _JsonOptions)?.Data!;
                            newEntry = await _MergeQueue.EnqueueAsync(newEntry).ConfigureAwait(false);
                            result = new { type = "command.result", action = "enqueue_merge", data = (object)newEntry };
                            break;

                        case "cancel_merge":
                            string cmEntryId = command.Id ?? "";
                            await _MergeQueue.CancelAsync(cmEntryId).ConfigureAwait(false);
                            result = new { type = "command.result", action = "cancel_merge", data = (object)new { status = "cancelled" } };
                            break;

                        case "process_merge_queue":
                            await _MergeQueue.ProcessQueueAsync().ConfigureAwait(false);
                            result = new { type = "command.result", action = "process_merge_queue", data = (object)new { status = "processed" } };
                            break;

                        // ── Enumerate ────────────────────────────────────────────

                        case "enumerate":
                            string entityType = (command.EntityType ?? "").ToLowerInvariant();
                            EnumerationQuery enumQuery = command.Query ?? new EnumerationQuery();
                            Stopwatch enumSw = Stopwatch.StartNew();

                            object? enumData = null;
                            switch (entityType)
                            {
                                case "fleets":
                                case "fleet":
                                    EnumerationResult<Fleet> enumFleets = await _Database.Fleets.EnumerateAsync(enumQuery).ConfigureAwait(false);
                                    enumFleets.TotalMs = Math.Round(enumSw.Elapsed.TotalMilliseconds, 2);
                                    enumData = enumFleets;
                                    break;
                                case "vessels":
                                case "vessel":
                                    EnumerationResult<Vessel> enumVessels = await _Database.Vessels.EnumerateAsync(enumQuery).ConfigureAwait(false);
                                    enumVessels.TotalMs = Math.Round(enumSw.Elapsed.TotalMilliseconds, 2);
                                    enumData = enumVessels;
                                    break;
                                case "captains":
                                case "captain":
                                    EnumerationResult<Captain> enumCaptains = await _Database.Captains.EnumerateAsync(enumQuery).ConfigureAwait(false);
                                    enumCaptains.TotalMs = Math.Round(enumSw.Elapsed.TotalMilliseconds, 2);
                                    enumData = enumCaptains;
                                    break;
                                case "missions":
                                case "mission":
                                    EnumerationResult<Mission> enumMissions = await _Database.Missions.EnumerateAsync(enumQuery).ConfigureAwait(false);
                                    enumMissions.TotalMs = Math.Round(enumSw.Elapsed.TotalMilliseconds, 2);
                                    enumData = enumMissions;
                                    break;
                                case "voyages":
                                case "voyage":
                                    EnumerationResult<Voyage> enumVoyages = await _Database.Voyages.EnumerateAsync(enumQuery).ConfigureAwait(false);
                                    enumVoyages.TotalMs = Math.Round(enumSw.Elapsed.TotalMilliseconds, 2);
                                    enumData = enumVoyages;
                                    break;
                                case "docks":
                                case "dock":
                                    EnumerationResult<Dock> enumDocks = await _Database.Docks.EnumerateAsync(enumQuery).ConfigureAwait(false);
                                    enumDocks.TotalMs = Math.Round(enumSw.Elapsed.TotalMilliseconds, 2);
                                    enumData = enumDocks;
                                    break;
                                case "signals":
                                case "signal":
                                    EnumerationResult<Signal> enumSignals = await _Database.Signals.EnumerateAsync(enumQuery).ConfigureAwait(false);
                                    enumSignals.TotalMs = Math.Round(enumSw.Elapsed.TotalMilliseconds, 2);
                                    enumData = enumSignals;
                                    break;
                                case "events":
                                case "event":
                                    EnumerationResult<ArmadaEvent> enumEvents = await _Database.Events.EnumerateAsync(enumQuery).ConfigureAwait(false);
                                    enumEvents.TotalMs = Math.Round(enumSw.Elapsed.TotalMilliseconds, 2);
                                    enumData = enumEvents;
                                    break;
                                case "merge_queue":
                                case "merge-queue":
                                case "mergequeue":
                                    List<MergeEntry> enumMqAll = await _MergeQueue.ListAsync().ConfigureAwait(false);
                                    int enumMqTotal = enumMqAll.Count;
                                    List<MergeEntry> enumMqPage = enumMqAll.Skip(enumQuery.Offset).Take(enumQuery.PageSize).ToList();
                                    EnumerationResult<MergeEntry> enumMerge = EnumerationResult<MergeEntry>.Create(enumQuery, enumMqPage, enumMqTotal);
                                    enumMerge.TotalMs = Math.Round(enumSw.Elapsed.TotalMilliseconds, 2);
                                    enumData = enumMerge;
                                    break;
                            }

                            if (enumData == null)
                                result = new { type = "command.error", action = "enumerate", error = "Unknown entity type: " + entityType + ". Valid types: fleets, vessels, captains, missions, voyages, docks, signals, events, merge_queue" };
                            else
                                result = new { type = "command.result", action = "enumerate", data = enumData };
                            break;

                        // ── Default ────────────────────────────────────────────────

                        default:
                            result = new { type = "command.error", action = action, error = "Unknown action: " + action };
                            break;
                    }

                    string json = JsonSerializer.Serialize(result, _JsonOptions);
                    await msg.RespondAsync(json).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _Logging.Warn(_Header + "error handling command: " + ex.Message);
                    string errorJson = JsonSerializer.Serialize(
                        new { type = "command.error", error = ex.Message }, _JsonOptions);
                    await msg.RespondAsync(errorJson).ConfigureAwait(false);
                }
            });

            // Default route for unspecified routes
            _WsApp.DefaultRoute = (sender, msg) =>
            {
                string json = JsonSerializer.Serialize(
                    new { type = "error", message = "Send a message with route 'subscribe' or 'command'" }, _JsonOptions);
                msg.RespondAsync(json).Wait();
            };

            // Not-found route
            _WsApp.NotFoundRoute = (sender, msg) =>
            {
                string json = JsonSerializer.Serialize(
                    new { type = "error", message = "Unknown route: " + (msg.Route ?? "null") }, _JsonOptions);
                msg.RespondAsync(json).Wait();
            };
        }

        private bool IsValidTransition(MissionStatusEnum current, MissionStatusEnum target)
        {
            return (current, target) switch
            {
                (MissionStatusEnum.Pending, MissionStatusEnum.Assigned) => true,
                (MissionStatusEnum.Pending, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.Assigned, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Assigned, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Testing) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Review) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.InProgress, MissionStatusEnum.Cancelled) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Review) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.Testing, MissionStatusEnum.Failed) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.Complete) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.InProgress) => true,
                (MissionStatusEnum.Review, MissionStatusEnum.Failed) => true,
                _ => false
            };
        }

        private void BroadcastEvent(object payload)
        {
            try
            {
                if (_WsApp.WebsocketServer == null) return;

                string json = JsonSerializer.Serialize(payload, _JsonOptions);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);

                List<WatsonWebsocket.ClientMetadata> clients = _WsApp.WebsocketServer.ListClients().ToList();
                foreach (WatsonWebsocket.ClientMetadata client in clients)
                {
                    try
                    {
                        _WsApp.WebsocketServer.SendAsync(client.Guid, bytes).Wait();
                    }
                    catch
                    {
                        // Client may have disconnected
                    }
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "broadcast error: " + ex.Message);
            }
        }

        #endregion
    }

    /// <summary>
    /// Data payload for the restart_mission WebSocket command.
    /// </summary>
    public class MissionRestartData
    {
        /// <summary>
        /// Optional new title. If null or empty, the original title is preserved.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Optional new description/instructions. If null or empty, the original description is preserved.
        /// </summary>
        public string? Description { get; set; }
    }
}
