namespace Armada.Server
{
    using System.Linq;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Handles remote-control management actions routed through the outbound tunnel.
    /// </summary>
    public class RemoteControlManagementService
    {
        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public RemoteControlManagementService(
            DatabaseDriver database,
            IAdmiralService admiral,
            Func<string, string, string?, string?, string?, string?, string?, string?, Task> emitEventAsync,
            Func<DateTime>? utcNow = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Admiral = admiral ?? throw new ArgumentNullException(nameof(admiral));
            _EmitEventAsync = emitEventAsync ?? throw new ArgumentNullException(nameof(emitEventAsync));
            _UtcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Handle a remote-control management request.
        /// </summary>
        public async Task<RemoteTunnelRequestResult> HandleAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            string method = envelope.Method?.Trim().ToLowerInvariant() ?? String.Empty;

            switch (method)
            {
                case "armada.fleets.list":
                    return await ListFleetsAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.fleet.detail":
                    return await GetFleetDetailAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.fleet.create":
                    return await CreateFleetAsync(envelope, token).ConfigureAwait(false);
                case "armada.fleet.update":
                    return await UpdateFleetAsync(envelope, token).ConfigureAwait(false);
                case "armada.vessels.list":
                    return await ListVesselsAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.vessel.detail":
                    return await GetVesselDetailAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.vessel.create":
                    return await CreateVesselAsync(envelope, token).ConfigureAwait(false);
                case "armada.vessel.update":
                    return await UpdateVesselAsync(envelope, token).ConfigureAwait(false);
                case "armada.pipelines.list":
                    return await ListPipelinesAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.playbooks.list":
                    return await ListPlaybooksAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.playbook.detail":
                    return await GetPlaybookDetailAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.playbook.create":
                    return await CreatePlaybookAsync(envelope, token).ConfigureAwait(false);
                case "armada.playbook.update":
                    return await UpdatePlaybookAsync(envelope, token).ConfigureAwait(false);
                case "armada.playbook.delete":
                    return await DeletePlaybookAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.voyages.list":
                    return await ListVoyagesAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.voyage.dispatch":
                    return await DispatchVoyageAsync(envelope, token).ConfigureAwait(false);
                case "armada.voyage.cancel":
                    return await CancelVoyageAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.missions.list":
                    return await ListMissionsAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.mission.create":
                    return await CreateMissionAsync(envelope, token).ConfigureAwait(false);
                case "armada.mission.update":
                    return await UpdateMissionAsync(envelope, token).ConfigureAwait(false);
                case "armada.mission.cancel":
                    return await CancelMissionAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                case "armada.mission.restart":
                    return await RestartMissionAsync(envelope, token).ConfigureAwait(false);
                case "armada.captain.stop":
                    return await StopCaptainAsync(DeserializeQueryRequest(envelope), token).ConfigureAwait(false);
                default:
                    return Unsupported(envelope.Method);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<RemoteTunnelRequestResult> ListFleetsAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            int limit = Clamp(request.Limit, 12, 1, 200);
            List<Fleet> fleets = await _Database.Fleets.EnumerateAsync(token).ConfigureAwait(false);
            List<Fleet> rows = fleets
                .OrderByDescending(f => f.LastUpdateUtc)
                .ThenByDescending(f => f.CreatedUtc)
                .Take(limit)
                .ToList();

            return Ok(new
            {
                limit = limit,
                count = rows.Count,
                fleets = rows
            }, "Fleet list captured.");
        }

        private async Task<RemoteTunnelRequestResult> GetFleetDetailAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            string fleetId = request.FleetId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(fleetId))
            {
                return BadRequest("missing_fleet_id", "FleetId is required.");
            }

            Fleet? fleet = await _Database.Fleets.ReadAsync(fleetId, token).ConfigureAwait(false);
            if (fleet == null)
            {
                return NotFound("Fleet not found.");
            }

            List<Vessel> vessels = await _Database.Vessels.EnumerateByFleetAsync(fleetId, token).ConfigureAwait(false);
            return Ok(new
            {
                fleet = fleet,
                vessels = vessels.OrderByDescending(v => v.LastUpdateUtc).ThenByDescending(v => v.CreatedUtc).ToList()
            }, "Fleet detail captured.");
        }

        private async Task<RemoteTunnelRequestResult> CreateFleetAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            Fleet? fleet = DeserializePayload<Fleet>(envelope);
            if (fleet == null || String.IsNullOrWhiteSpace(fleet.Name))
            {
                return BadRequest("invalid_fleet", "A fleet payload with a name is required.");
            }

            fleet.LastUpdateUtc = _UtcNow();
            fleet = await _Database.Fleets.CreateAsync(fleet, token).ConfigureAwait(false);
            await _EmitEventAsync("fleet.created", "Fleet created from proxy: " + fleet.Name, "fleet", fleet.Id, null, null, null, null).ConfigureAwait(false);
            return Created(fleet, "Fleet created.");
        }

        private async Task<RemoteTunnelRequestResult> UpdateFleetAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            FleetUpdateRequest? request = DeserializePayload<FleetUpdateRequest>(envelope);
            string fleetId = request?.FleetId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(fleetId))
            {
                return BadRequest("missing_fleet_id", "FleetId is required.");
            }

            if (request?.Fleet == null || String.IsNullOrWhiteSpace(request.Fleet.Name))
            {
                return BadRequest("invalid_fleet", "A fleet payload with a name is required.");
            }

            Fleet? existing = await _Database.Fleets.ReadAsync(fleetId, token).ConfigureAwait(false);
            if (existing == null)
            {
                return NotFound("Fleet not found.");
            }

            existing.Name = request.Fleet.Name;
            existing.Description = request.Fleet.Description;
            existing.DefaultPipelineId = request.Fleet.DefaultPipelineId;
            existing.Active = request.Fleet.Active;
            existing.LastUpdateUtc = _UtcNow();

            Fleet updated = await _Database.Fleets.UpdateAsync(existing, token).ConfigureAwait(false);
            await _EmitEventAsync("fleet.updated", "Fleet updated from proxy: " + updated.Name, "fleet", updated.Id, null, null, null, null).ConfigureAwait(false);
            return Ok(updated, "Fleet updated.");
        }

        private async Task<RemoteTunnelRequestResult> ListVesselsAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            int limit = Clamp(request.Limit, 12, 1, 200);
            IEnumerable<Vessel> vessels;

            if (!String.IsNullOrWhiteSpace(request.FleetId))
            {
                vessels = await _Database.Vessels.EnumerateByFleetAsync(request.FleetId.Trim(), token).ConfigureAwait(false);
            }
            else
            {
                vessels = await _Database.Vessels.EnumerateAsync(token).ConfigureAwait(false);
            }

            List<Vessel> rows = vessels
                .OrderByDescending(v => v.LastUpdateUtc)
                .ThenByDescending(v => v.CreatedUtc)
                .Take(limit)
                .ToList();

            return Ok(new
            {
                limit = limit,
                count = rows.Count,
                vessels = rows
            }, "Vessel list captured.");
        }

        private async Task<RemoteTunnelRequestResult> GetVesselDetailAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            string vesselId = request.VesselId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(vesselId))
            {
                return BadRequest("missing_vessel_id", "VesselId is required.");
            }

            Vessel? vessel = await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (vessel == null)
            {
                return NotFound("Vessel not found.");
            }

            List<Mission> missions = await _Database.Missions.EnumerateByVesselAsync(vesselId, token).ConfigureAwait(false);
            return Ok(new
            {
                vessel = vessel,
                recentMissions = missions
                    .OrderByDescending(m => m.LastUpdateUtc)
                    .ThenByDescending(m => m.CreatedUtc)
                    .Take(12)
                    .ToList()
            }, "Vessel detail captured.");
        }

        private async Task<RemoteTunnelRequestResult> CreateVesselAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            Vessel? vessel = DeserializePayload<Vessel>(envelope);
            if (vessel == null || String.IsNullOrWhiteSpace(vessel.Name) || String.IsNullOrWhiteSpace(vessel.RepoUrl))
            {
                return BadRequest("invalid_vessel", "A vessel payload with name and repoUrl is required.");
            }

            vessel.LastUpdateUtc = _UtcNow();
            vessel = await _Database.Vessels.CreateAsync(vessel, token).ConfigureAwait(false);
            await _EmitEventAsync("vessel.created", "Vessel created from proxy: " + vessel.Name, "vessel", vessel.Id, null, null, vessel.Id, null).ConfigureAwait(false);
            return Created(vessel, "Vessel created.");
        }

        private async Task<RemoteTunnelRequestResult> ListPipelinesAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            int limit = Clamp(request.Limit, 24, 1, 200);
            List<Pipeline> rows = (await _Database.Pipelines.EnumerateAsync(token).ConfigureAwait(false))
                .OrderByDescending(p => p.LastUpdateUtc)
                .ThenByDescending(p => p.CreatedUtc)
                .Take(limit)
                .ToList();

            return Ok(new
            {
                limit = limit,
                count = rows.Count,
                pipelines = rows
            }, "Pipeline list captured.");
        }

        private async Task<RemoteTunnelRequestResult> UpdateVesselAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            VesselUpdateRequest? request = DeserializePayload<VesselUpdateRequest>(envelope);
            string vesselId = request?.VesselId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(vesselId))
            {
                return BadRequest("missing_vessel_id", "VesselId is required.");
            }

            if (request?.Vessel == null || String.IsNullOrWhiteSpace(request.Vessel.Name) || String.IsNullOrWhiteSpace(request.Vessel.RepoUrl))
            {
                return BadRequest("invalid_vessel", "A vessel payload with name and repoUrl is required.");
            }

            Vessel? existing = await _Database.Vessels.ReadAsync(vesselId, token).ConfigureAwait(false);
            if (existing == null)
            {
                return NotFound("Vessel not found.");
            }

            existing.FleetId = request.Vessel.FleetId;
            existing.Name = request.Vessel.Name;
            existing.RepoUrl = request.Vessel.RepoUrl;
            existing.WorkingDirectory = request.Vessel.WorkingDirectory;
            existing.DefaultBranch = String.IsNullOrWhiteSpace(request.Vessel.DefaultBranch) ? "main" : request.Vessel.DefaultBranch;
            existing.ProjectContext = request.Vessel.ProjectContext;
            existing.StyleGuide = request.Vessel.StyleGuide;
            existing.EnableModelContext = request.Vessel.EnableModelContext;
            existing.ModelContext = request.Vessel.ModelContext;
            existing.LandingMode = request.Vessel.LandingMode;
            existing.BranchCleanupPolicy = request.Vessel.BranchCleanupPolicy;
            existing.AllowConcurrentMissions = request.Vessel.AllowConcurrentMissions;
            existing.DefaultPipelineId = request.Vessel.DefaultPipelineId;
            existing.Active = request.Vessel.Active;
            existing.LastUpdateUtc = _UtcNow();

            Vessel updated = await _Database.Vessels.UpdateAsync(existing, token).ConfigureAwait(false);
            await _EmitEventAsync("vessel.updated", "Vessel updated from proxy: " + updated.Name, "vessel", updated.Id, null, null, updated.Id, null).ConfigureAwait(false);
            return Ok(updated, "Vessel updated.");
        }

        private async Task<RemoteTunnelRequestResult> ListPlaybooksAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            int limit = Clamp(request.Limit, 24, 1, 200);
            List<Playbook> rows = (await _Database.Playbooks.EnumerateAsync(token).ConfigureAwait(false))
                .OrderByDescending(p => p.LastUpdateUtc)
                .ThenByDescending(p => p.CreatedUtc)
                .Take(limit)
                .ToList();

            return Ok(new
            {
                limit = limit,
                count = rows.Count,
                playbooks = rows
            }, "Playbook list captured.");
        }

        private async Task<RemoteTunnelRequestResult> GetPlaybookDetailAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            string playbookId = request.PlaybookId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(playbookId))
            {
                return BadRequest("missing_playbook_id", "PlaybookId is required.");
            }

            Playbook? playbook = await _Database.Playbooks.ReadAsync(playbookId, token).ConfigureAwait(false);
            if (playbook == null)
            {
                return NotFound("Playbook not found.");
            }

            return Ok(new
            {
                playbook = playbook
            }, "Playbook detail captured.");
        }

        private async Task<RemoteTunnelRequestResult> CreatePlaybookAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            Playbook? playbook = DeserializePayload<Playbook>(envelope);
            if (playbook == null || String.IsNullOrWhiteSpace(playbook.FileName) || String.IsNullOrWhiteSpace(playbook.Content))
            {
                return BadRequest("invalid_playbook", "A playbook payload with fileName and content is required.");
            }

            PlaybookService service = new PlaybookService(_Database, new SyslogLogging.LoggingModule());
            playbook.TenantId = Constants.DefaultTenantId;
            playbook.UserId = Constants.DefaultUserId;
            service.Validate(playbook);

            if (await _Database.Playbooks.ExistsByFileNameAsync(Constants.DefaultTenantId, playbook.FileName, token).ConfigureAwait(false))
            {
                return BadRequest("duplicate_playbook", "A playbook with that file name already exists.");
            }

            playbook.LastUpdateUtc = _UtcNow();
            playbook = await _Database.Playbooks.CreateAsync(playbook, token).ConfigureAwait(false);
            await _EmitEventAsync("playbook.created", "Playbook created from proxy: " + playbook.FileName, "playbook", playbook.Id, null, null, null, null).ConfigureAwait(false);
            return Created(playbook, "Playbook created.");
        }

        private async Task<RemoteTunnelRequestResult> UpdatePlaybookAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            PlaybookUpdateRequest? request = DeserializePayload<PlaybookUpdateRequest>(envelope);
            string playbookId = request?.PlaybookId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(playbookId))
            {
                return BadRequest("missing_playbook_id", "PlaybookId is required.");
            }

            if (request?.Playbook == null || String.IsNullOrWhiteSpace(request.Playbook.FileName) || String.IsNullOrWhiteSpace(request.Playbook.Content))
            {
                return BadRequest("invalid_playbook", "A playbook payload with fileName and content is required.");
            }

            Playbook? existing = await _Database.Playbooks.ReadAsync(playbookId, token).ConfigureAwait(false);
            if (existing == null)
            {
                return NotFound("Playbook not found.");
            }
            existing.TenantId ??= Constants.DefaultTenantId;
            existing.UserId ??= Constants.DefaultUserId;

            existing.FileName = request.Playbook.FileName;
            existing.Description = request.Playbook.Description;
            existing.Content = request.Playbook.Content;
            existing.Active = request.Playbook.Active;
            existing.LastUpdateUtc = _UtcNow();

            PlaybookService service = new PlaybookService(_Database, new SyslogLogging.LoggingModule());
            service.Validate(existing);

            Playbook? duplicate = await _Database.Playbooks.ReadByFileNameAsync(existing.TenantId ?? Constants.DefaultTenantId, existing.FileName, token).ConfigureAwait(false);
            if (duplicate != null && !String.Equals(duplicate.Id, existing.Id, StringComparison.Ordinal))
            {
                return BadRequest("duplicate_playbook", "A playbook with that file name already exists.");
            }

            Playbook updated = await _Database.Playbooks.UpdateAsync(existing, token).ConfigureAwait(false);
            await _EmitEventAsync("playbook.updated", "Playbook updated from proxy: " + updated.FileName, "playbook", updated.Id, null, null, null, null).ConfigureAwait(false);
            return Ok(updated, "Playbook updated.");
        }

        private async Task<RemoteTunnelRequestResult> DeletePlaybookAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            string playbookId = request.PlaybookId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(playbookId))
            {
                return BadRequest("missing_playbook_id", "PlaybookId is required.");
            }

            Playbook? existing = await _Database.Playbooks.ReadAsync(playbookId, token).ConfigureAwait(false);
            if (existing == null)
            {
                return NotFound("Playbook not found.");
            }

            await _Database.Playbooks.DeleteAsync(playbookId, token).ConfigureAwait(false);
            await _EmitEventAsync("playbook.deleted", "Playbook deleted from proxy: " + existing.FileName, "playbook", playbookId, null, null, null, null).ConfigureAwait(false);
            return Ok(new { status = "deleted", playbookId = playbookId }, "Playbook deleted.");
        }

        private async Task<RemoteTunnelRequestResult> ListVoyagesAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            int limit = Clamp(request.Limit, 12, 1, 200);
            IEnumerable<Voyage> voyages = await _Database.Voyages.EnumerateAsync(token).ConfigureAwait(false);

            if (!String.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse(request.Status, true, out VoyageStatusEnum status))
                {
                    return BadRequest("invalid_voyage_status", "Invalid voyage status: " + request.Status);
                }

                voyages = voyages.Where(v => v.Status == status);
            }

            List<Voyage> rows = voyages
                .OrderByDescending(v => v.LastUpdateUtc)
                .ThenByDescending(v => v.CreatedUtc)
                .Take(limit)
                .ToList();

            return Ok(new
            {
                limit = limit,
                count = rows.Count,
                voyages = rows
            }, "Voyage list captured.");
        }

        private async Task<RemoteTunnelRequestResult> DispatchVoyageAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            VoyageRequest? request = DeserializePayload<VoyageRequest>(envelope);
            if (request == null || String.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest("invalid_voyage", "A voyage payload with a title is required.");
            }

            List<MissionDescription> missions = new List<MissionDescription>();
            if (request.Missions != null)
            {
                foreach (MissionRequest mission in request.Missions)
                {
                    if (!String.IsNullOrWhiteSpace(mission.Title))
                    {
                        missions.Add(new MissionDescription(mission.Title, mission.Description));
                    }
                }
            }

            string? pipelineId = request.PipelineId;
            if (String.IsNullOrWhiteSpace(pipelineId) && !String.IsNullOrWhiteSpace(request.Pipeline))
            {
                Pipeline? pipeline = await _Database.Pipelines.ReadByNameAsync(request.Pipeline, token).ConfigureAwait(false);
                if (pipeline == null)
                {
                    return BadRequest("invalid_pipeline", "Pipeline not found: " + request.Pipeline);
                }

                pipelineId = pipeline.Id;
            }

            Voyage voyage;
            if (String.IsNullOrWhiteSpace(request.VesselId) || missions.Count == 0)
            {
                voyage = new Voyage(request.Title, request.Description);
                voyage.SelectedPlaybooks = request.SelectedPlaybooks ?? new List<SelectedPlaybook>();
                voyage.LastUpdateUtc = _UtcNow();
                voyage = await _Database.Voyages.CreateAsync(voyage, token).ConfigureAwait(false);
                if (voyage.SelectedPlaybooks.Count > 0)
                {
                    await _Database.Playbooks.SetVoyageSelectionsAsync(voyage.Id, voyage.SelectedPlaybooks, token).ConfigureAwait(false);
                }
            }
            else
            {
                voyage = await _Admiral.DispatchVoyageAsync(
                    request.Title,
                    request.Description,
                    request.VesselId,
                    missions,
                    pipelineId,
                    request.SelectedPlaybooks ?? new List<SelectedPlaybook>(),
                    token).ConfigureAwait(false);
            }

            await _EmitEventAsync("voyage.dispatched", "Voyage dispatched from proxy: " + voyage.Title, "voyage", voyage.Id, null, null, request.VesselId, voyage.Id).ConfigureAwait(false);
            return Created(voyage, "Voyage dispatched.");
        }

        private async Task<RemoteTunnelRequestResult> CancelVoyageAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            string voyageId = request.VoyageId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(voyageId))
            {
                return BadRequest("missing_voyage_id", "VoyageId is required.");
            }

            Voyage? voyage = await _Database.Voyages.ReadAsync(voyageId, token).ConfigureAwait(false);
            if (voyage == null)
            {
                return NotFound("Voyage not found.");
            }

            List<Mission> missions = await _Database.Missions.EnumerateByVoyageAsync(voyageId, token).ConfigureAwait(false);
            int cancelledCount = 0;

            foreach (Mission mission in missions)
            {
                if (IsMissionActiveForCancellation(mission.Status) && !String.IsNullOrWhiteSpace(mission.CaptainId))
                {
                    try
                    {
                        await _Admiral.RecallCaptainAsync(mission.CaptainId, token).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }

                if (IsMissionActiveForCancellation(mission.Status))
                {
                    mission.Status = MissionStatusEnum.Cancelled;
                    mission.FailureReason = "Cancelled from proxy";
                    mission.ProcessId = null;
                    mission.CompletedUtc = _UtcNow();
                    mission.LastUpdateUtc = _UtcNow();
                    await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
                    cancelledCount++;
                }
            }

            voyage.Status = VoyageStatusEnum.Cancelled;
            voyage.CompletedUtc = _UtcNow();
            voyage.LastUpdateUtc = _UtcNow();
            voyage = await _Database.Voyages.UpdateAsync(voyage, token).ConfigureAwait(false);

            await _EmitEventAsync("voyage.cancelled", "Voyage cancelled from proxy: " + voyage.Title, "voyage", voyage.Id, null, null, null, voyage.Id).ConfigureAwait(false);
            return Ok(new
            {
                voyage = voyage,
                cancelledMissions = cancelledCount
            }, "Voyage cancelled.");
        }

        private async Task<RemoteTunnelRequestResult> ListMissionsAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            int limit = Clamp(request.Limit, 16, 1, 200);
            IEnumerable<Mission> missions;

            if (!String.IsNullOrWhiteSpace(request.VoyageId))
            {
                missions = await _Database.Missions.EnumerateByVoyageAsync(request.VoyageId.Trim(), token).ConfigureAwait(false);
            }
            else if (!String.IsNullOrWhiteSpace(request.VesselId))
            {
                missions = await _Database.Missions.EnumerateByVesselAsync(request.VesselId.Trim(), token).ConfigureAwait(false);
            }
            else
            {
                missions = await _Database.Missions.EnumerateAsync(token).ConfigureAwait(false);
            }

            if (!String.IsNullOrWhiteSpace(request.Status))
            {
                if (!Enum.TryParse(request.Status, true, out MissionStatusEnum status))
                {
                    return BadRequest("invalid_mission_status", "Invalid mission status: " + request.Status);
                }

                missions = missions.Where(m => m.Status == status);
            }

            List<Mission> rows = missions
                .OrderByDescending(m => m.LastUpdateUtc)
                .ThenByDescending(m => m.CreatedUtc)
                .Take(limit)
                .ToList();

            foreach (Mission mission in rows)
            {
                mission.DiffSnapshot = null;
            }

            return Ok(new
            {
                limit = limit,
                count = rows.Count,
                missions = rows
            }, "Mission list captured.");
        }

        private async Task<RemoteTunnelRequestResult> CreateMissionAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            Mission? mission = DeserializePayload<Mission>(envelope);
            if (mission == null || String.IsNullOrWhiteSpace(mission.Title))
            {
                return BadRequest("invalid_mission", "A mission payload with a title is required.");
            }

            if (!String.IsNullOrWhiteSpace(mission.VesselId))
            {
                Vessel? vessel = await _Database.Vessels.ReadAsync(mission.VesselId, token).ConfigureAwait(false);
                if (vessel != null)
                {
                    mission.TenantId ??= vessel.TenantId;
                    mission.UserId ??= vessel.UserId;
                }
            }

            Mission created = await _Admiral.DispatchMissionAsync(mission, token).ConfigureAwait(false);
            await _EmitEventAsync("mission.created", "Mission created from proxy: " + created.Title, "mission", created.Id, created.CaptainId, created.Id, created.VesselId, created.VoyageId).ConfigureAwait(false);
            return Created(created, "Mission created.");
        }

        private async Task<RemoteTunnelRequestResult> UpdateMissionAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            MissionUpdateRequest? request = DeserializePayload<MissionUpdateRequest>(envelope);
            string missionId = request?.MissionId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(missionId))
            {
                return BadRequest("missing_mission_id", "MissionId is required.");
            }

            if (request?.Mission == null || String.IsNullOrWhiteSpace(request.Mission.Title))
            {
                return BadRequest("invalid_mission", "A mission payload with a title is required.");
            }

            Mission? existing = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (existing == null)
            {
                return NotFound("Mission not found.");
            }

            existing.Title = request.Mission.Title;
            existing.Description = request.Mission.Description;
            existing.Priority = request.Mission.Priority;
            existing.VesselId = request.Mission.VesselId;
            existing.VoyageId = request.Mission.VoyageId;
            existing.BranchName = request.Mission.BranchName;
            existing.PrUrl = request.Mission.PrUrl;
            existing.ParentMissionId = request.Mission.ParentMissionId;
            existing.Persona = request.Mission.Persona;
            existing.LastUpdateUtc = _UtcNow();

            Mission updated = await _Database.Missions.UpdateAsync(existing, token).ConfigureAwait(false);
            updated.DiffSnapshot = null;
            await _EmitEventAsync("mission.updated", "Mission updated from proxy: " + updated.Title, "mission", updated.Id, updated.CaptainId, updated.Id, updated.VesselId, updated.VoyageId).ConfigureAwait(false);
            return Ok(updated, "Mission updated.");
        }

        private async Task<RemoteTunnelRequestResult> CancelMissionAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            string missionId = request.MissionId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(missionId))
            {
                return BadRequest("missing_mission_id", "MissionId is required.");
            }

            Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null)
            {
                return NotFound("Mission not found.");
            }

            if (IsMissionActiveForCancellation(mission.Status) && !String.IsNullOrWhiteSpace(mission.CaptainId))
            {
                try
                {
                    await _Admiral.RecallCaptainAsync(mission.CaptainId, token).ConfigureAwait(false);
                }
                catch
                {
                }
            }

            mission.Status = MissionStatusEnum.Cancelled;
            mission.FailureReason = "Cancelled from proxy";
            mission.ProcessId = null;
            mission.CompletedUtc = _UtcNow();
            mission.LastUpdateUtc = _UtcNow();
            mission = await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            mission.DiffSnapshot = null;

            await _EmitEventAsync("mission.cancelled", "Mission cancelled from proxy: " + mission.Title, "mission", mission.Id, mission.CaptainId, mission.Id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);
            return Ok(mission, "Mission cancelled.");
        }

        private async Task<RemoteTunnelRequestResult> RestartMissionAsync(RemoteTunnelEnvelope envelope, CancellationToken token)
        {
            MissionRestartPayload? request = DeserializePayload<MissionRestartPayload>(envelope);
            string missionId = request?.MissionId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(missionId))
            {
                return BadRequest("missing_mission_id", "MissionId is required.");
            }

            Mission? mission = await _Database.Missions.ReadAsync(missionId, token).ConfigureAwait(false);
            if (mission == null)
            {
                return NotFound("Mission not found.");
            }

            if (mission.Status != MissionStatusEnum.Failed && mission.Status != MissionStatusEnum.Cancelled)
            {
                return BadRequest("invalid_restart_state", "Only Failed or Cancelled missions can be restarted.");
            }

            if (!String.IsNullOrWhiteSpace(request?.Title))
            {
                mission.Title = request.Title!;
            }

            if (!String.IsNullOrWhiteSpace(request?.Description))
            {
                mission.Description = request.Description;
            }

            mission.Status = MissionStatusEnum.Pending;
            mission.BranchName = null;
            mission.PrUrl = null;
            mission.CommitHash = null;
            mission.DockId = null;
            mission.ProcessId = null;
            mission.DiffSnapshot = null;
            mission.FailureReason = null;
            mission.CaptainId = null;
            mission.StartedUtc = null;
            mission.CompletedUtc = null;
            mission.LastUpdateUtc = _UtcNow();
            mission.CreatedUtc = _UtcNow();

            mission = await _Database.Missions.UpdateAsync(mission, token).ConfigureAwait(false);
            mission.DiffSnapshot = null;

            await _EmitEventAsync("mission.restarted", "Mission restarted from proxy: " + mission.Title, "mission", mission.Id, null, mission.Id, mission.VesselId, mission.VoyageId).ConfigureAwait(false);
            return Ok(mission, "Mission restarted.");
        }

        private async Task<RemoteTunnelRequestResult> StopCaptainAsync(RemoteTunnelQueryRequest request, CancellationToken token)
        {
            string captainId = request.CaptainId?.Trim() ?? String.Empty;
            if (String.IsNullOrWhiteSpace(captainId))
            {
                return BadRequest("missing_captain_id", "CaptainId is required.");
            }

            Captain? captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);
            if (captain == null)
            {
                return NotFound("Captain not found.");
            }

            await _Admiral.RecallCaptainAsync(captainId, token).ConfigureAwait(false);
            captain = await _Database.Captains.ReadAsync(captainId, token).ConfigureAwait(false);

            await _EmitEventAsync("captain.stopped", "Captain stopped from proxy: " + captainId, "captain", captainId, captainId, captain?.CurrentMissionId, null, null).ConfigureAwait(false);
            return Ok(new
            {
                captain = captain
            }, "Captain stop requested.");
        }

        private static bool IsMissionActiveForCancellation(MissionStatusEnum status)
        {
            return status == MissionStatusEnum.Pending ||
                status == MissionStatusEnum.Assigned ||
                status == MissionStatusEnum.InProgress ||
                status == MissionStatusEnum.Testing ||
                status == MissionStatusEnum.Review;
        }

        private static RemoteTunnelQueryRequest DeserializeQueryRequest(RemoteTunnelEnvelope envelope)
        {
            return DeserializePayload<RemoteTunnelQueryRequest>(envelope) ?? new RemoteTunnelQueryRequest();
        }

        private static T? DeserializePayload<T>(RemoteTunnelEnvelope envelope)
        {
            if (!envelope.Payload.HasValue)
            {
                return default;
            }

            try
            {
                return envelope.Payload.Value.Deserialize<T>(RemoteTunnelProtocol.JsonOptions);
            }
            catch (JsonException)
            {
                return default;
            }
        }

        private static int Clamp(int value, int defaultValue, int minimum, int maximum)
        {
            int effective = value <= 0 ? defaultValue : value;
            if (effective < minimum) effective = minimum;
            if (effective > maximum) effective = maximum;
            return effective;
        }

        private static RemoteTunnelRequestResult Ok(object payload, string message)
        {
            return new RemoteTunnelRequestResult
            {
                StatusCode = 200,
                Payload = payload,
                Message = message
            };
        }

        private static RemoteTunnelRequestResult Created(object payload, string message)
        {
            return new RemoteTunnelRequestResult
            {
                StatusCode = 201,
                Payload = payload,
                Message = message
            };
        }

        private static RemoteTunnelRequestResult BadRequest(string errorCode, string message)
        {
            return new RemoteTunnelRequestResult
            {
                StatusCode = 400,
                ErrorCode = errorCode,
                Message = message
            };
        }

        private static RemoteTunnelRequestResult NotFound(string message)
        {
            return new RemoteTunnelRequestResult
            {
                StatusCode = 404,
                ErrorCode = "not_found",
                Message = message
            };
        }

        private static RemoteTunnelRequestResult Unsupported(string? method)
        {
            return new RemoteTunnelRequestResult
            {
                StatusCode = 404,
                ErrorCode = "unsupported_method",
                Message = "Unsupported tunnel method " + method + "."
            };
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly IAdmiralService _Admiral;
        private readonly Func<string, string, string?, string?, string?, string?, string?, string?, Task> _EmitEventAsync;
        private readonly Func<DateTime> _UtcNow;

        private sealed class FleetUpdateRequest
        {
            public string FleetId { get; set; } = String.Empty;
            public Fleet? Fleet { get; set; } = null;
        }

        private sealed class VesselUpdateRequest
        {
            public string VesselId { get; set; } = String.Empty;
            public Vessel? Vessel { get; set; } = null;
        }

        private sealed class PlaybookUpdateRequest
        {
            public string PlaybookId { get; set; } = String.Empty;
            public Playbook? Playbook { get; set; } = null;
        }

        private sealed class MissionUpdateRequest
        {
            public string MissionId { get; set; } = String.Empty;
            public Mission? Mission { get; set; } = null;
        }

        private sealed class MissionRestartPayload
        {
            public string MissionId { get; set; } = String.Empty;
            public string? Title { get; set; } = null;
            public string? Description { get; set; } = null;
        }

        #endregion
    }
}
