namespace Armada.DemoSeed
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Writes the demo world through the database driver: fleets, vessels, an open model endpoint, captains on open and
    /// frontier models with model profiles, a mixed open and frontier pipeline, and voyages with missions in every status
    /// the Admiral leaves alone without a running agent. Every name, host and repository is fictional.
    ///
    /// In-flight missions are Testing, WorkProduced, PullRequestOpen or Review, with a start time and no process. The
    /// Admiral treats an InProgress or Assigned mission without a live agent process as interrupted and re-dispatches it,
    /// so those two statuses are not seeded. Pending missions depend on in-flight ones, so nothing is dispatched, and every
    /// vessel points at a repository that does not exist, so a dispatch could not clone it anyway.
    /// </summary>
    public class DemoWorldSeeder
    {
        #region Private-Members

        private readonly DatabaseDriver _Db;
        private readonly string _Tenant = Constants.DefaultTenantId;
        private readonly string _User;
        private readonly DateTime _Now = DateTime.UtcNow;
        private readonly Dictionary<string, Vessel> _Vessels = new Dictionary<string, Vessel>(StringComparer.Ordinal);
        private readonly Dictionary<string, Captain> _Captains = new Dictionary<string, Captain>(StringComparer.Ordinal);
        private readonly List<Mission> _Finished = new List<Mission>();
        private ModelProfile? _OpenProfile = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="db">Initialized database driver.</param>
        /// <param name="ownerUserId">User that owns the seeded rows (the reviewer, so a non-admin login sees them).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public DemoWorldSeeder(DatabaseDriver db, string ownerUserId)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _User = ownerUserId ?? throw new ArgumentNullException(nameof(ownerUserId));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Seed the world and return the ids the screenshot script needs.
        /// </summary>
        /// <returns>Ids.</returns>
        public async Task<DemoSeedIds> SeedAsync()
        {
            await SeedFleetsAsync().ConfigureAwait(false);
            await SeedCaptainsAsync().ConfigureAwait(false);
            await SeedPipelineAsync().ConfigureAwait(false);

            DemoSeedIds ids = new DemoSeedIds();
            Voyage mixed = await SeedMixedVoyageAsync(ids).ConfigureAwait(false);
            ids.VoyageId = mixed.Id;
            await SeedOtherVoyagesAsync().ConfigureAwait(false);

            DemoHistorySeeder history = new DemoHistorySeeder(_Db, _Tenant, _User, _Now);
            await history.SeedOutcomesAsync(_Finished, new List<Captain>(_Captains.Values)).ConfigureAwait(false);
            await history.SeedScorecardsAsync(new List<Captain>(_Captains.Values)).ConfigureAwait(false);

            _OpenProfile = history.ProfileFor(_Captains["qwen-worker-1"]);
            ids.ProfileId = _OpenProfile?.Id ?? "";
            return ids;
        }

        #endregion

        #region Private-Methods

        private async Task SeedFleetsAsync()
        {
            Fleet platform = await CreateFleetAsync("Platform", "Backend services and shared libraries").ConfigureAwait(false);
            Fleet apps = await CreateFleetAsync("Apps", "Web storefront, mobile app and documentation").ConfigureAwait(false);

            await CreateVesselAsync(platform, "payments-api", "C#").ConfigureAwait(false);
            await CreateVesselAsync(platform, "inventory-service", "C#").ConfigureAwait(false);
            await CreateVesselAsync(platform, "event-bus", "Go").ConfigureAwait(false);
            await CreateVesselAsync(apps, "web-storefront", "TypeScript").ConfigureAwait(false);
            await CreateVesselAsync(apps, "mobile-app", "TypeScript").ConfigureAwait(false);
        }

        private async Task<Fleet> CreateFleetAsync(string name, string description)
        {
            Fleet fleet = new Fleet(name);
            fleet.TenantId = _Tenant;
            fleet.UserId = _User;
            fleet.Description = description;
            return await _Db.Fleets.CreateAsync(fleet).ConfigureAwait(false);
        }

        private async Task CreateVesselAsync(Fleet fleet, string name, string language)
        {
            // github.com/example is a reserved-looking placeholder owner; a clone attempt fails, so no agent can run here.
            Vessel vessel = new Vessel(name, "https://github.com/example/" + name + ".git");
            vessel.TenantId = _Tenant;
            vessel.UserId = _User;
            vessel.FleetId = fleet.Id;
            vessel.DefaultBranch = "main";
            vessel = await _Db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
            _Vessels[name] = vessel;

            VesselHealth health = new VesselHealth();
            health.TenantId = _Tenant;
            health.VesselId = vessel.Id;
            health.OverallStatus = name == "event-bus" ? VesselHealthStatusEnum.Warn : VesselHealthStatusEnum.Pass;
            health.EvaluatedUtc = _Now.AddHours(-2);
            health.CurrentBranch = "main";
            health.IsDirty = false;
            health.LastCommitUtc = _Now.AddHours(-5);
            health.PrimaryLanguage = language;
            health.DependencyStatus = name == "event-bus" ? VesselHealthStatusEnum.Warn : VesselHealthStatusEnum.Pass;
            health.WorkingTreeStatus = VesselHealthStatusEnum.Pass;
            health.BranchStatus = VesselHealthStatusEnum.Pass;
            await _Db.VesselHealth.UpsertAsync(health).ConfigureAwait(false);
        }

        private async Task SeedCaptainsAsync()
        {
            ModelEndpoint endpoint = new ModelEndpoint();
            endpoint.TenantId = _Tenant;
            endpoint.UserId = _User;
            endpoint.Name = "Open model server";
            endpoint.Kind = ModelEndpointKindEnum.Inference;
            endpoint.Provider = ModelProviderEnum.OpenAICompatible;
            endpoint.BaseUrl = "https://models.example.test/v1";
            endpoint.Model = "qwen3.8-coder";
            endpoint.Enabled = true;
            endpoint = await _Db.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);

            await CreateCaptainAsync("opus-architect", AgentRuntimeEnum.ClaudeCode, "claude-opus-5-5", CaptainTierEnum.Premium, null, PersonaCatalog.Architect).ConfigureAwait(false);
            await CreateCaptainAsync("opus-judge", AgentRuntimeEnum.ClaudeCode, "claude-opus-5-5", CaptainTierEnum.Premium, null, PersonaCatalog.Judge).ConfigureAwait(false);
            await CreateCaptainAsync("gpt-worker", AgentRuntimeEnum.Codex, "gpt-6.1", CaptainTierEnum.Premium, null, PersonaCatalog.Worker).ConfigureAwait(false);
            await CreateCaptainAsync("sonnet-worker", AgentRuntimeEnum.ClaudeCode, "claude-sonnet-5-5", CaptainTierEnum.Standard, null, PersonaCatalog.Worker).ConfigureAwait(false);
            await CreateCaptainAsync("qwen-worker-1", AgentRuntimeEnum.ApiEndpoint, "qwen3.8-coder", CaptainTierEnum.Economy, endpoint.Id, PersonaCatalog.Worker).ConfigureAwait(false);
            await CreateCaptainAsync("qwen-worker-2", AgentRuntimeEnum.ApiEndpoint, "qwen3.8-coder", CaptainTierEnum.Economy, endpoint.Id, PersonaCatalog.Worker).ConfigureAwait(false);
        }

        private async Task CreateCaptainAsync(string name, AgentRuntimeEnum runtime, string model, CaptainTierEnum tier, string? endpointId, string persona)
        {
            Captain captain = new Captain(name, runtime);
            captain.TenantId = _Tenant;
            captain.UserId = _User;
            captain.Model = model;
            captain.ModelEndpointId = endpointId;
            captain.Tier = tier;
            captain.PreferredPersona = persona;
            captain.State = CaptainStateEnum.Idle;
            captain.LastHeartbeatUtc = _Now.AddMinutes(-1);
            _Captains[name] = await _Db.Captains.CreateAsync(captain).ConfigureAwait(false);
        }

        private async Task SeedPipelineAsync()
        {
            Pipeline pipeline = new Pipeline("Mixed open and frontier");
            pipeline.TenantId = _Tenant;
            pipeline.UserId = _User;
            pipeline.Description = "A frontier Architect plans, open-model Workers implement, and a frontier Judge reviews.";
            pipeline.Stages = new List<PipelineStage>
            {
                new PipelineStage(1, PersonaCatalog.Architect) { Description = "Plan the change on Claude Opus" },
                new PipelineStage(2, PersonaCatalog.Worker) { Description = "Implement on Qwen3.8, escalating to GPT-6.1 when needed" },
                new PipelineStage(3, PersonaCatalog.Judge) { Description = "Review on Claude Opus", RequiresReview = true }
            };
            await _Db.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);
        }

        private async Task<Voyage> SeedMixedVoyageAsync(DemoSeedIds ids)
        {
            Voyage voyage = await CreateVoyageAsync("Add partial refunds to the payments API", VoyageStatusEnum.InProgress, 180,
                "Mixed open and frontier pipeline: Opus plans, Qwen3.8 implements, Opus judges.").ConfigureAwait(false);
            Vessel vessel = _Vessels["payments-api"];

            Mission plan = await CreateMissionAsync(voyage, vessel, "Plan partial refunds", PersonaCatalog.Architect, "opus-architect", MissionStatusEnum.Complete, 175, 160).ConfigureAwait(false);
            await DemoHistorySeeder.AddOutcomeAsync(_Db, plan, _Captains["opus-architect"], 1, StageDispositionEnum.Completed, null, null, plan.StartedUtc!.Value, plan.CompletedUtc!.Value).ConfigureAwait(false);

            // The mission behind the Attempts screenshot: Qwen3.8 needed an output repair, then failed its tests and was
            // escalated to GPT-6.1, which finished it.
            Mission refunds = NewMission(voyage, vessel, "Implement the refund endpoint and its validation", PersonaCatalog.Worker, "gpt-worker", MissionStatusEnum.Complete, 158, 95);
            refunds.Tier = CaptainTierEnum.Premium;
            refunds.EscalationCount = 1;
            refunds.EscalatedFromCaptainId = _Captains["qwen-worker-2"].Id;
            refunds.EscalatedFromModel = "qwen3.8-coder";
            refunds.OutputRepairAttempts = 1;
            refunds.DependsOnMissionId = plan.Id;
            refunds.CommitHash = "4f2a9c1e7b3d";
            refunds = await _Db.Missions.CreateAsync(refunds).ConfigureAwait(false);
            DateTime start = refunds.StartedUtc!.Value;
            await DemoHistorySeeder.AddOutcomeAsync(_Db, refunds, _Captains["qwen-worker-2"], 1, StageDispositionEnum.OutputRepair, null, ContractRepairKindEnum.CompletionLineMissing, start, start.AddMinutes(14)).ConfigureAwait(false);
            await DemoHistorySeeder.AddOutcomeAsync(_Db, refunds, _Captains["qwen-worker-2"], 2, StageDispositionEnum.Escalated, MissionFailureKindEnum.TestFail, null, start.AddMinutes(15), start.AddMinutes(31)).ConfigureAwait(false);
            await DemoHistorySeeder.AddOutcomeAsync(_Db, refunds, _Captains["gpt-worker"], 3, StageDispositionEnum.Completed, null, null, start.AddMinutes(32), refunds.CompletedUtc!.Value).ConfigureAwait(false);
            ids.MissionId = refunds.Id;

            Mission ledger = await CreateMissionAsync(voyage, vessel, "Record refunds in the ledger export", PersonaCatalog.Worker, "qwen-worker-1", MissionStatusEnum.Testing, 40, null, plan.Id).ConfigureAwait(false);

            Mission review = await CreateMissionAsync(voyage, vessel, "Review: refund endpoint and validation", PersonaCatalog.Judge, "opus-judge", MissionStatusEnum.Review, 60, null, refunds.Id).ConfigureAwait(false);
            await DemoHistorySeeder.AddOutcomeAsync(_Db, review, _Captains["opus-judge"], 1, StageDispositionEnum.Completed, null, null, review.StartedUtc!.Value, _Now.AddMinutes(-35)).ConfigureAwait(false);

            await CreateMissionAsync(voyage, vessel, "Review: ledger export", PersonaCatalog.Judge, "opus-judge", MissionStatusEnum.Pending, 30, null, ledger.Id).ConfigureAwait(false);
            return voyage;
        }

        private async Task SeedOtherVoyagesAsync()
        {
            Voyage checkout = await CreateVoyageAsync("Redesign the storefront checkout", VoyageStatusEnum.InProgress, 600, null).ConfigureAwait(false);
            Vessel web = _Vessels["web-storefront"];
            await CreateMissionAsync(checkout, web, "Split the checkout form into steps", PersonaCatalog.Worker, "sonnet-worker", MissionStatusEnum.PullRequestOpen, 580, null).ConfigureAwait(false);
            await CreateMissionAsync(checkout, web, "Add address autocomplete", PersonaCatalog.Worker, "qwen-worker-2", MissionStatusEnum.WorkProduced, 400, null).ConfigureAwait(false);
            await CreateMissionAsync(checkout, web, "Review: checkout steps", PersonaCatalog.Judge, "opus-judge", MissionStatusEnum.Review, 120, null).ConfigureAwait(false);
            Mission tests = await CreateMissionAsync(checkout, web, "Checkout end-to-end tests", PersonaCatalog.Worker, "sonnet-worker", MissionStatusEnum.Testing, 25, null).ConfigureAwait(false);
            await CreateMissionAsync(checkout, web, "Update the checkout screenshots in the docs", PersonaCatalog.Worker, null, MissionStatusEnum.Pending, 20, null, tests.Id).ConfigureAwait(false);

            Voyage inventory = await CreateVoyageAsync("Harden inventory sync", VoyageStatusEnum.Complete, 60 * 26, null).ConfigureAwait(false);
            Vessel inv = _Vessels["inventory-service"];
            string[] titles = new string[] { "Retry transient sync failures", "Add idempotency keys to stock updates", "Log sync drift per warehouse", "Backfill missing SKUs", "Alert on stale snapshots", "Document the sync protocol" };
            string[] workers = new string[] { "qwen-worker-1", "qwen-worker-2", "sonnet-worker", "gpt-worker", "qwen-worker-1", "sonnet-worker" };
            for (int i = 0; i < titles.Length; i++)
                await CreateMissionAsync(inventory, inv, titles[i], PersonaCatalog.Worker, workers[i], MissionStatusEnum.Complete, 60 * 26 - i * 20, 60 * 25 - i * 20).ConfigureAwait(false);

            Voyage docs = await CreateVoyageAsync("Upgrade the event bus client", VoyageStatusEnum.Failed, 60 * 30, null).ConfigureAwait(false);
            Vessel bus = _Vessels["event-bus"];
            Mission failed = NewMission(docs, bus, "Move to the v3 client API", PersonaCatalog.Worker, "qwen-worker-2", MissionStatusEnum.Failed, 60 * 30, 60 * 29);
            failed.FailureKind = MissionFailureKindEnum.TestFail;
            failed.FailureReason = "3 tests failed in BusClientTests after the API change";
            failed = await _Db.Missions.CreateAsync(failed).ConfigureAwait(false);
            _Finished.Add(failed);
            Mission landing = NewMission(docs, bus, "Regenerate the protocol bindings", PersonaCatalog.Worker, "gpt-worker", MissionStatusEnum.LandingFailed, 60 * 28, null);
            landing.FailureReason = "Merge conflict in bindings/events.go";
            await _Db.Missions.CreateAsync(landing).ConfigureAwait(false);

            Voyage mobile = await CreateVoyageAsync("Notification settings in the mobile app", VoyageStatusEnum.Cancelled, 60 * 50, null).ConfigureAwait(false);
            Vessel app = _Vessels["mobile-app"];
            await CreateMissionAsync(mobile, app, "Per-category notification toggles", PersonaCatalog.Worker, "sonnet-worker", MissionStatusEnum.Cancelled, 60 * 50, 60 * 49).ConfigureAwait(false);
            Mission blocked = NewMission(mobile, app, "Quiet hours", PersonaCatalog.Worker, null, MissionStatusEnum.Blocked, 60 * 48, null);
            blocked.FailureReason = "Waiting on the design for quiet hours";
            await _Db.Missions.CreateAsync(blocked).ConfigureAwait(false);
        }

        private async Task<Voyage> CreateVoyageAsync(string title, VoyageStatusEnum status, int minutesAgo, string? description)
        {
            Voyage voyage = new Voyage(title, description);
            voyage.TenantId = _Tenant;
            voyage.UserId = _User;
            voyage.Status = status;
            voyage.CreatedUtc = _Now.AddMinutes(-minutesAgo);
            return await _Db.Voyages.CreateAsync(voyage).ConfigureAwait(false);
        }

        private Mission NewMission(Voyage voyage, Vessel vessel, string title, string persona, string? captain, MissionStatusEnum status, int startedMinutesAgo, int? completedMinutesAgo)
        {
            Mission mission = new Mission(title, title + ".");
            mission.TenantId = _Tenant;
            mission.UserId = _User;
            mission.VoyageId = voyage.Id;
            mission.VesselId = vessel.Id;
            mission.Persona = persona;
            mission.CaptainId = captain != null ? _Captains[captain].Id : null;
            mission.Tier = captain != null ? _Captains[captain].Tier : null;
            mission.Status = status;
            mission.CreatedUtc = _Now.AddMinutes(-startedMinutesAgo - 2);
            if (status != MissionStatusEnum.Pending && status != MissionStatusEnum.Blocked) mission.StartedUtc = _Now.AddMinutes(-startedMinutesAgo);
            if (completedMinutesAgo.HasValue) mission.CompletedUtc = _Now.AddMinutes(-completedMinutesAgo.Value);
            if (status == MissionStatusEnum.PullRequestOpen) mission.PrUrl = "https://github.com/example/" + vessel.Name + "/pull/42";
            mission.BranchName = "armada/demo/" + title.ToLowerInvariant().Replace(' ', '-').Replace(":", "");
            mission.LastUpdateUtc = mission.CompletedUtc ?? mission.StartedUtc ?? mission.CreatedUtc;
            return mission;
        }

        private async Task<Mission> CreateMissionAsync(Voyage voyage, Vessel vessel, string title, string persona, string? captain, MissionStatusEnum status, int startedMinutesAgo, int? completedMinutesAgo, string? dependsOn = null)
        {
            Mission mission = NewMission(voyage, vessel, title, persona, captain, status, startedMinutesAgo, completedMinutesAgo);
            mission.DependsOnMissionId = dependsOn;
            Mission created = await _Db.Missions.CreateAsync(mission).ConfigureAwait(false);
            if (status == MissionStatusEnum.Complete && !String.Equals(persona, PersonaCatalog.Architect, StringComparison.Ordinal)) _Finished.Add(created);
            return created;
        }

        #endregion
    }
}
