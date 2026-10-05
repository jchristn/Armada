namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Runtimes.Mcp;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// O-01: every MCP tool that reads or acts on an entity by id is tenant scoped. A dedicated server gets two tenants,
    /// each with a tenant admin and a bearer credential. Tenant A's entities are seeded directly in the database (every
    /// text field carries a marker), then every tool advertised by <c>tools/list</c> whose input schema takes an entity
    /// id is called as tenant B with tenant A's ids. Each call must answer not-found (or skip the id in a bulk tool) and
    /// must never echo the marker; afterwards every tenant A entity must be unchanged. The same read tools called by
    /// tenant A's own admin succeed, so the not-found answers are not vacuous. Tools whose id arguments the suite cannot
    /// seed are listed explicitly in <see cref="_UnseededIdTools"/> so a new by-id tool fails the suite until it is
    /// mapped here.
    /// </summary>
    public sealed class McpTenantIsolationSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.McpTenantIsolation";


        // Tools whose id arguments refer to entities this suite does not seed. Each is scoped through its service
        // (caller tenant/user) and covered by its own suite; listing them keeps the generic coverage explicit.
        private static readonly Dictionary<string, string> _UnseededIdTools = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["get_deployment"] = "deployment (DeploymentService scopes by caller)",
            ["approve_deployment"] = "deployment (DeploymentService scopes by caller)",
            ["verify_deployment"] = "deployment (DeploymentService scopes by caller)",
            ["rollback_deployment"] = "deployment (DeploymentService scopes by caller)",
            ["get_runbook"] = "runbook (RunbookService scopes by caller)",
            ["get_runbook_execution"] = "runbook execution (RunbookService scopes by caller)",
            ["start_runbook_execution"] = "runbook (RunbookService scopes by caller)",
            ["fleet_action_run_status"] = "fleet action run (FleetActionService scopes by caller)",
            ["cancel_fleet_action_run"] = "fleet action run (FleetActionService scopes by caller)",
            ["categorize_vessel_import"] = "vessel import batch (VesselImportService scopes by caller)",
            ["import_vessels"] = "vessel import batch (VesselImportService scopes by caller)",
            ["apply_fleet_recommendations"] = "vessel import batch (FleetCategorizationService scopes by caller)",
            ["delete_event"] = "event (covered by E2E.SecretsAndAudit, F-19)",
            ["delete_events"] = "event (covered by E2E.SecretsAndAudit, F-19)",
            ["get_cli_permission_request"] = "CLI permission request (CliPermissionService scopes by caller; covered by Services.CliPermissionService)",
            ["decide_cli_permission_request"] = "CLI permission request (CliPermissionService scopes by caller; covered by Services.CliPermissionService)",
            ["update_cli_permission_rule"] = "CLI permission rule (CliPermissionService scopes by caller; covered by Services.CliPermissionService)",
            ["delete_cli_permission_rule"] = "CLI permission rule (CliPermissionService scopes by caller; covered by Services.CliPermissionService)"
        };

        // Tools that take an id only as an optional filter or reference and otherwise act on the caller's own scope:
        // they must not leak tenant A's data, but a successful (empty or caller-owned) answer is correct.
        private static readonly HashSet<string> _FilterOnlyTools = new HashSet<string>(StringComparer.Ordinal)
        {
            "enumerate",
            "token_usage_summary",
            "papercut_summary",
            "list_backlog",
            "list_objectives",
            "list_backlog_refinement_sessions",
            "search_memory",
            "inbox",
            "list_cli_permission_requests",
            "list_cli_permission_rules",
            "discover_vessels"
        };

        // Every entity type the enumerate tool accepts (fleet_action_run_target needs a run id and is checked by id).
        private static readonly string[] _EnumerateEntityTypes = new[]
        {
            "objectives", "jobs", "model_endpoints", "harbors", "vessel_health", "fleets", "vessels", "captains", "missions",
            "voyages", "docks", "signals", "events", "releases", "deployments", "incidents", "runbooks", "runbook_executions",
            "merge_queue", "personas", "memories", "prompt_templates", "pipelines", "playbooks", "workflow_profiles",
            "project_profiles", "skills", "check_runs", "vessel_import_batch", "fleet_action", "fleet_action_run"
        };

        private SecurityTestServer? _Server;
        private E2ETenantUser? _TenantA;
        private E2ETenantUser? _TenantB;
        private readonly string _Marker = "tenant-a-marker-" + Guid.NewGuid().ToString("N");
        private readonly Dictionary<string, string> _Ids = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _Names = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("setup_two_tenants_and_seed_tenant_a", "Two tenants with tenant admins; tenant A's entities seeded", TestTags.Positive, async () =>
            {
                _Server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                // This suite makes well over a hundred tool calls in a burst; lift the per-client limit instead of
                // retrying when the limiter refuses.
                _Server.Settings.Mcp.ToolCallsPerSecond = 0;
                await _Server.StartAsync().ConfigureAwait(false);
                using (HttpClient admin = _Server.CreateRestClient(true))
                {
                    _TenantA = await E2ETenantUser.CreateAsync(admin, "mcpiso-a", true).ConfigureAwait(false);
                    _TenantB = await E2ETenantUser.CreateAsync(admin, "mcpiso-b", true).ConfigureAwait(false);
                }

                await SeedTenantAAsync().ConfigureAwait(false);
                AssertTrue(_Ids.Count >= 15, "expected the tenant A seed to cover the core entity types, got " + _Ids.Count);
            }));

            cases.Add(CaseAsync("owner_reads_own_entities", "Tenant A's admin reads its own entities by id over MCP (control)", TestTags.Positive, async () =>
            {
                RequireSetup();
                List<string> failures = new List<string>();
                using (McpToolClient client = CreateClient(_TenantA!))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    List<McpRemoteTool> tools = await client.ListToolsAsync().ConfigureAwait(false);
                    foreach (McpRemoteTool tool in tools.Where(t => IsOwnerReadControl(t.Name)))
                    {
                        string? args = BuildArguments(tool, out List<string> unmapped, out bool usesId);
                        if (args == null || !usesId) continue;
                        Armada.Runtimes.Mcp.McpToolCallResult result;
                        try
                        {
                            result = await client.CallToolResultAsync(tool.Name, args).ConfigureAwait(false);
                        }
                        catch (McpClientException ex)
                        {
                            failures.Add(tool.Name + ": protocol error: " + ex.Message);
                            continue;
                        }

                        McpToolResultProbe probe = McpToolResultProbe.From(result);
                        // The owner must reach its own entity: NotFound, Forbidden, or InvalidArgument (or a failed call)
                        // means it could not. Other categories (for example Unavailable from get_mission_diff when the seeded mission has
                        // no diff yet) mean the entity was found.
                        if (result.IsError
                            || probe.ErrorCode == McpToolErrorCodeEnum.NotFound
                            || probe.ErrorCode == McpToolErrorCodeEnum.Forbidden
                            || probe.ErrorCode == McpToolErrorCodeEnum.InvalidArgument)
                            failures.Add(tool.Name + ": " + (probe.ErrorCode?.ToString() ?? "isError") + ": " + Truncate(result.Text));
                    }
                }

                AssertTrue(failures.Count == 0, "tenant A could not read its own entities (seed or mapping problem):\n" + String.Join("\n", failures));
            }));

            cases.Add(CaseAsync("cross_tenant_by_id_tools_return_not_found", "Every by-id MCP tool answers not-found to tenant B for tenant A's ids", TestTags.Negative, async () =>
            {
                RequireSetup();
                List<string> failures = new List<string>();
                List<string> unmappedTools = new List<string>();
                int checkedTools = 0;
                using (McpToolClient client = CreateClient(_TenantB!))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    List<McpRemoteTool> tools = await client.ListToolsAsync().ConfigureAwait(false);
                    AssertTrue(tools.Count > 100, "expected the full tool catalog, got " + tools.Count);

                    foreach (McpRemoteTool tool in tools.OrderBy(t => t.Name, StringComparer.Ordinal))
                    {
                        string? args = BuildArguments(tool, out List<string> unmapped, out bool usesId);
                        if (unmapped.Count > 0 && !_UnseededIdTools.ContainsKey(tool.Name))
                            unmappedTools.Add(tool.Name + " (" + String.Join(", ", unmapped) + ")");
                        if (args == null || !usesId || _UnseededIdTools.ContainsKey(tool.Name)) continue;

                        checkedTools++;
                        Armada.Runtimes.Mcp.McpToolCallResult result;
                        try
                        {
                            result = await client.CallToolResultAsync(tool.Name, args).ConfigureAwait(false);
                        }
                        catch (McpClientException ex)
                        {
                            failures.Add(tool.Name + " could not be checked (protocol error): " + ex.Message);
                            continue;
                        }

                        // The marker is a unique string seeded into tenant A's data; finding it anywhere in the reply
                        // is the leak this suite exists to catch, whatever the reply's shape.
                        if (result.Text.IndexOf(_Marker, StringComparison.Ordinal) >= 0)
                        {
                            failures.Add(tool.Name + " LEAKED tenant A data: " + Truncate(result.Text));
                            continue;
                        }

                        if (_FilterOnlyTools.Contains(tool.Name)) continue;
                        if (!IsDenied(result))
                            failures.Add(tool.Name + " did not answer not-found: " + Truncate(result.Text));
                    }
                }

                AssertTrue(unmappedTools.Count == 0, "by-id tools with id arguments this suite cannot map (seed them or list them in _UnseededIdTools):\n" + String.Join("\n", unmappedTools));
                AssertTrue(checkedTools >= 60, "expected at least 60 by-id tools to be exercised, got " + checkedTools);
                AssertTrue(failures.Count == 0, "cross-tenant MCP failures:\n" + String.Join("\n", failures));
            }));

            cases.Add(CaseAsync("enumerate_every_entity_type_is_tenant_scoped", "MCP enumerate never returns tenant A's records to tenant B, on the summary and the include paths", TestTags.Negative, async () =>
            {
                RequireSetup();
                List<string> failures = new List<string>();
                using (McpToolClient client = CreateClient(_TenantB!))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    foreach (string entityType in _EnumerateEntityTypes)
                    {
                        foreach (bool include in new[] { false, true })
                        {
                            string args = JsonHelper.Serialize(new
                            {
                                entityType = entityType,
                                pageSize = 1000,
                                includeDescription = include,
                                includeContext = include,
                                includeTestOutput = include,
                                includePayload = include,
                                includeMessage = include
                            });
                            Armada.Runtimes.Mcp.McpToolCallResult result = await client.CallToolResultAsync("enumerate", args).ConfigureAwait(false);
                            string label = entityType + (include ? " (include)" : " (summary)");
                            if (result.Text.IndexOf(_Marker, StringComparison.Ordinal) >= 0)
                                failures.Add(label + " LEAKED tenant A data: " + Truncate(result.Text));
                            foreach (KeyValuePair<string, string> seeded in _Ids)
                            {
                                if (result.Text.IndexOf(seeded.Value, StringComparison.Ordinal) >= 0)
                                    failures.Add(label + " returned tenant A's " + seeded.Key + " " + seeded.Value);
                            }
                        }
                    }
                }

                AssertTrue(failures.Count == 0, "cross-tenant enumerate failures:\n" + String.Join("\n", failures));
            }));

            cases.Add(CaseAsync("enumerate_filters_apply_in_tenant_scope", "MCP enumerate applies the voyage, vessel, status, captain and fleet filters for a tenant-scoped caller", TestTags.Positive, async () =>
            {
                RequireSetup();
                // A second mission, vessel, voyage, captain, dock, signal and merge entry in tenant A that every filter below
                // must exclude; without the filters the tenant-scoped page would hold both.
                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    string tenant = _TenantA!.TenantId;
                    string user = _TenantA.UserId;
                    Fleet otherFleet = new Fleet("iso-other-fleet");
                    otherFleet.TenantId = tenant;
                    otherFleet.UserId = user;
                    otherFleet = await db.Fleets.CreateAsync(otherFleet).ConfigureAwait(false);
                    Vessel otherVessel = new Vessel("iso-other-vessel", "https://example.invalid/iso-other.git");
                    otherVessel.TenantId = tenant;
                    otherVessel.UserId = user;
                    otherVessel.FleetId = otherFleet.Id;
                    otherVessel = await db.Vessels.CreateAsync(otherVessel).ConfigureAwait(false);
                    Captain otherCaptain = new Captain("iso-other-captain");
                    otherCaptain.TenantId = tenant;
                    otherCaptain.UserId = user;
                    otherCaptain.State = CaptainStateEnum.Idle;
                    otherCaptain = await db.Captains.CreateAsync(otherCaptain).ConfigureAwait(false);
                    Voyage otherVoyage = new Voyage("iso-other-voyage", "other");
                    otherVoyage.TenantId = tenant;
                    otherVoyage.UserId = user;
                    otherVoyage.Status = VoyageStatusEnum.Complete;
                    otherVoyage = await db.Voyages.CreateAsync(otherVoyage).ConfigureAwait(false);
                    Mission otherMission = new Mission("iso-other-mission", "other");
                    otherMission.TenantId = tenant;
                    otherMission.UserId = user;
                    otherMission.VesselId = otherVessel.Id;
                    otherMission.VoyageId = otherVoyage.Id;
                    otherMission.CaptainId = otherCaptain.Id;
                    otherMission.Status = MissionStatusEnum.Complete;
                    await db.Missions.CreateAsync(otherMission).ConfigureAwait(false);
                    Dock otherDock = new Dock(otherVessel.Id);
                    otherDock.TenantId = tenant;
                    otherDock.UserId = user;
                    otherDock.BranchName = "iso-other-branch";
                    await db.Docks.CreateAsync(otherDock).ConfigureAwait(false);
                    Signal otherSignal = new Signal(SignalTypeEnum.Mail, "other");
                    otherSignal.TenantId = tenant;
                    otherSignal.UserId = user;
                    otherSignal.ToCaptainId = otherCaptain.Id;
                    await db.Signals.CreateAsync(otherSignal).ConfigureAwait(false);
                    MergeEntry otherEntry = new MergeEntry("iso-other-merge");
                    otherEntry.TenantId = tenant;
                    otherEntry.UserId = user;
                    otherEntry.VesselId = otherVessel.Id;
                    otherEntry.Status = MergeStatusEnum.Queued;
                    await db.MergeEntries.CreateAsync(otherEntry).ConfigureAwait(false);
                }

                List<string> failures = new List<string>();
                using (McpToolClient client = CreateClient(_TenantA!))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    foreach (bool include in new[] { false, true })
                    {
                        await ExpectOnlyAsync(client, failures, new { entityType = "missions", voyageId = _Ids["voyage"], includeDescription = include }, _Ids["mission"]).ConfigureAwait(false);
                        await ExpectOnlyAsync(client, failures, new { entityType = "missions", vesselId = _Ids["vessel"], includeDescription = include }, _Ids["mission"]).ConfigureAwait(false);
                        await ExpectOnlyAsync(client, failures, new { entityType = "missions", status = "Failed", includeDescription = include }, _Ids["mission"]).ConfigureAwait(false);
                        await ExpectOnlyAsync(client, failures, new { entityType = "missions", voyageId = _Ids["voyage"], status = "Complete", includeDescription = include }, null).ConfigureAwait(false);
                        await ExpectOnlyAsync(client, failures, new { entityType = "voyages", status = "Open", includeDescription = include }, _Ids["voyage"]).ConfigureAwait(false);
                        await ExpectOnlyAsync(client, failures, new { entityType = "vessels", fleetId = _Ids["fleet"], includeContext = include }, _Ids["vessel"]).ConfigureAwait(false);
                        await ExpectOnlyAsync(client, failures, new { entityType = "signals", toCaptainId = _Ids["captain"], includeMessage = include }, _Ids["signal"]).ConfigureAwait(false);
                        await ExpectOnlyAsync(client, failures, new { entityType = "merge_queue", status = "Failed", includeTestOutput = include }, _Ids["merge"]).ConfigureAwait(false);
                    }

                    await ExpectOnlyAsync(client, failures, new { entityType = "captains", status = "Working" }, _Ids["captain"]).ConfigureAwait(false);
                    await ExpectOnlyAsync(client, failures, new { entityType = "docks", vesselId = _Ids["vessel"] }, _Ids["dock"]).ConfigureAwait(false);
                }

                AssertTrue(failures.Count == 0, "tenant-scoped enumerate filter failures:\n" + String.Join("\n", failures));
            }));

            cases.Add(CaseAsync("stop_all_is_tenant_scoped", "stop_all by tenant B leaves tenant A's working captain alone", TestTags.Negative, async () =>
            {
                RequireSetup();
                using (McpToolClient client = CreateClient(_TenantB!))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    Armada.Runtimes.Mcp.McpToolCallResult result = await client.CallToolResultAsync("stop_all", "{}").ConfigureAwait(false);
                    AssertFalse(result.IsError, "stop_all failed: " + result.Text);
                    AssertEqual("all_stopped", McpToolResultProbe.From(result).Status, "stop_all status");
                }

                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    Captain? captain = await db.Captains.ReadAsync(_Ids["captain"]).ConfigureAwait(false);
                    AssertNotNull(captain, "tenant A captain");
                    AssertEqual(CaptainStateEnum.Working, captain!.State, "tenant A captain state after tenant B stop_all");
                }
            }));

            cases.Add(CaseAsync("tenant_a_entities_unchanged", "Tenant A's entities survive tenant B's calls unchanged", TestTags.Negative, async () =>
            {
                RequireSetup();
                List<string> problems = new List<string>();
                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    Fleet? fleet = await db.Fleets.ReadAsync(_Ids["fleet"]).ConfigureAwait(false);
                    if (fleet == null || !fleet.Description!.Contains(_Marker)) problems.Add("fleet");
                    Vessel? vessel = await db.Vessels.ReadAsync(_Ids["vessel"]).ConfigureAwait(false);
                    if (vessel == null || !String.Equals(vessel.ProjectContext, _Marker, StringComparison.Ordinal) || !String.Equals(vessel.Name, _Names["vessel"], StringComparison.Ordinal)) problems.Add("vessel");
                    Captain? captain = await db.Captains.ReadAsync(_Ids["captain"]).ConfigureAwait(false);
                    if (captain == null || !String.Equals(captain.Name, _Names["captain"], StringComparison.Ordinal)) problems.Add("captain");
                    Voyage? voyage = await db.Voyages.ReadAsync(_Ids["voyage"]).ConfigureAwait(false);
                    if (voyage == null || voyage.Status != VoyageStatusEnum.Open) problems.Add("voyage");
                    Mission? mission = await db.Missions.ReadAsync(_Ids["mission"]).ConfigureAwait(false);
                    if (mission == null || mission.Status != MissionStatusEnum.Failed || !String.Equals(mission.Title, _Names["mission"], StringComparison.Ordinal)) problems.Add("mission");
                    if (await db.Docks.ReadAsync(_Ids["dock"]).ConfigureAwait(false) == null) problems.Add("dock");
                    if (await db.Signals.ReadAsync(_Ids["signal"]).ConfigureAwait(false) == null) problems.Add("signal");
                    MergeEntry? entry = await db.MergeEntries.ReadAsync(_Ids["merge"]).ConfigureAwait(false);
                    if (entry == null || entry.Status != MergeStatusEnum.Failed) problems.Add("merge entry");
                    if (await db.CheckRuns.ReadAsync(_Ids["checkrun"]).ConfigureAwait(false) == null) problems.Add("check run");
                    Objective? objective = await db.Objectives.ReadAsync(_Ids["objective"]).ConfigureAwait(false);
                    if (objective == null || !String.Equals(objective.Title, _Names["objective"], StringComparison.Ordinal)) problems.Add("objective");
                    Playbook? playbook = await db.Playbooks.ReadAsync(_Ids["playbook"]).ConfigureAwait(false);
                    if (playbook == null || !playbook.Content.Contains(_Marker)) problems.Add("playbook");
                    Persona? persona = await db.Personas.ReadAsync(_Ids["persona"]).ConfigureAwait(false);
                    if (persona == null || !String.Equals(persona.Description, _Marker, StringComparison.Ordinal)) problems.Add("persona");
                    Pipeline? pipeline = await db.Pipelines.ReadAsync(_Ids["pipeline"]).ConfigureAwait(false);
                    if (pipeline == null || !String.Equals(pipeline.Description, _Marker, StringComparison.Ordinal)) problems.Add("pipeline");
                    PromptTemplate? template = await db.PromptTemplates.ReadAsync(_Ids["template"]).ConfigureAwait(false);
                    if (template == null || !String.Equals(template.Content, _Marker, StringComparison.Ordinal)) problems.Add("prompt template");
                    Memory? memory = await db.Memories.ReadAsync(_Ids["memory"]).ConfigureAwait(false);
                    if (memory == null || !String.Equals(memory.Content, _Marker, StringComparison.Ordinal)) problems.Add("memory");
                    ModelEndpoint? endpoint = await db.ModelEndpoints.ReadAsync(_Ids["endpoint"]).ConfigureAwait(false);
                    if (endpoint == null || !String.Equals(endpoint.Name, _Names["endpoint"], StringComparison.Ordinal)) problems.Add("model endpoint");
                    Harbor? harbor = await db.Harbors.ReadAsync(_Ids["harbor"]).ConfigureAwait(false);
                    if (harbor == null || !String.Equals(harbor.Name, _Names["harbor"], StringComparison.Ordinal)) problems.Add("harbor");
                    if (await db.Releases.ReadAsync(_Ids["release"]).ConfigureAwait(false) == null) problems.Add("release");
                    if (await db.FleetActions.ReadAsync(_Ids["fleetaction"]).ConfigureAwait(false) == null) problems.Add("fleet action");
                }

                AssertTrue(problems.Count == 0, "tenant A entities changed or deleted by tenant B: " + String.Join(", ", problems));
            }));

            cases.Add(CaseAsync("cleanup", "Stop the dedicated server", TestTags.Positive, () =>
            {
                _Server?.Dispose();
                _Server = null;
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "MCP Tenant Isolation (O-01)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private void RequireSetup()
        {
            if (_Server == null || _TenantA == null || _TenantB == null) throw new InvalidOperationException("setup case did not run");
        }

        private McpToolClient CreateClient(E2ETenantUser user)
        {
            Dictionary<string, string> headers = new Dictionary<string, string>
            {
                ["Authorization"] = "Bearer " + user.BearerToken
            };
            return new McpToolClient(_Server!.McpUrl + "/mcp", null, headers, null, 60);
        }

        private async Task<DatabaseDriver> OpenDatabaseAsync()
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseDriver db = DatabaseDriverFactory.Create(_Server!.Settings.Database, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        private async Task SeedTenantAAsync()
        {
            string tenant = _TenantA!.TenantId;
            string user = _TenantA.UserId;
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

            using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
            {
                Fleet fleet = new Fleet("iso-fleet-" + suffix);
                fleet.TenantId = tenant;
                fleet.UserId = user;
                fleet.Description = _Marker;
                fleet = await db.Fleets.CreateAsync(fleet).ConfigureAwait(false);
                _Ids["fleet"] = fleet.Id;

                Vessel vessel = new Vessel("iso-vessel-" + suffix, "https://example.invalid/iso-" + suffix + ".git");
                vessel.TenantId = tenant;
                vessel.UserId = user;
                vessel.FleetId = fleet.Id;
                vessel.ProjectContext = _Marker;
                vessel = await db.Vessels.CreateAsync(vessel).ConfigureAwait(false);
                _Ids["vessel"] = vessel.Id;
                _Names["vessel"] = vessel.Name;

                Captain captain = new Captain("iso-captain-" + suffix);
                captain.TenantId = tenant;
                captain.UserId = user;
                captain.State = CaptainStateEnum.Working;
                captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);
                _Ids["captain"] = captain.Id;
                _Names["captain"] = captain.Name;

                Voyage voyage = new Voyage("iso-voyage-" + suffix, _Marker);
                voyage.TenantId = tenant;
                voyage.UserId = user;
                voyage = await db.Voyages.CreateAsync(voyage).ConfigureAwait(false);
                _Ids["voyage"] = voyage.Id;

                Mission mission = new Mission("iso-mission-" + suffix, _Marker);
                mission.TenantId = tenant;
                mission.UserId = user;
                mission.VesselId = vessel.Id;
                mission.VoyageId = voyage.Id;
                mission.Status = MissionStatusEnum.Failed;
                mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                _Ids["mission"] = mission.Id;
                _Names["mission"] = mission.Title;

                Dock dock = new Dock(vessel.Id);
                dock.TenantId = tenant;
                dock.UserId = user;
                dock.BranchName = "iso-branch-" + suffix;
                dock = await db.Docks.CreateAsync(dock).ConfigureAwait(false);
                _Ids["dock"] = dock.Id;

                Signal signal = new Signal(SignalTypeEnum.Nudge, _Marker);
                signal.TenantId = tenant;
                signal.UserId = user;
                signal.ToCaptainId = captain.Id;
                signal = await db.Signals.CreateAsync(signal).ConfigureAwait(false);
                _Ids["signal"] = signal.Id;

                MergeEntry entry = new MergeEntry("iso-merge-" + suffix);
                entry.TenantId = tenant;
                entry.UserId = user;
                entry.VesselId = vessel.Id;
                entry.Status = MergeStatusEnum.Failed;
                entry.TestOutput = _Marker;
                entry = await db.MergeEntries.CreateAsync(entry).ConfigureAwait(false);
                _Ids["merge"] = entry.Id;

                CheckRun run = new CheckRun();
                run.TenantId = tenant;
                run.UserId = user;
                run.VesselId = vessel.Id;
                run.Label = "iso-check-" + suffix;
                run.Output = _Marker;
                run.Status = CheckRunStatusEnum.Failed;
                run = await db.CheckRuns.CreateAsync(run).ConfigureAwait(false);
                _Ids["checkrun"] = run.Id;

                Objective objective = new Objective();
                objective.TenantId = tenant;
                objective.UserId = user;
                objective.Title = "iso-objective-" + suffix;
                objective.Description = _Marker;
                objective = await db.Objectives.CreateAsync(objective).ConfigureAwait(false);
                _Ids["objective"] = objective.Id;
                _Names["objective"] = objective.Title;

                Playbook playbook = new Playbook("iso-" + suffix + ".md", "# " + _Marker);
                playbook.TenantId = tenant;
                playbook.UserId = user;
                playbook = await db.Playbooks.CreateAsync(playbook).ConfigureAwait(false);
                _Ids["playbook"] = playbook.Id;

                Persona persona = new Persona("iso-persona-" + suffix, "persona.worker");
                persona.TenantId = tenant;
                persona.UserId = user;
                persona.Description = _Marker;
                persona = await db.Personas.CreateAsync(persona).ConfigureAwait(false);
                _Ids["persona"] = persona.Id;
                _Names["persona"] = persona.Name;

                Pipeline pipeline = new Pipeline("iso-pipeline-" + suffix);
                pipeline.TenantId = tenant;
                pipeline.UserId = user;
                pipeline.Description = _Marker;
                pipeline.Stages = new List<PipelineStage> { new PipelineStage(1, persona.Name) };
                pipeline = await db.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);
                _Ids["pipeline"] = pipeline.Id;
                _Names["pipeline"] = pipeline.Name;

                PromptTemplate template = new PromptTemplate("iso.template." + suffix, _Marker);
                template.TenantId = tenant;
                template.UserId = user;
                template.Category = "mission";
                template = await db.PromptTemplates.CreateAsync(template).ConfigureAwait(false);
                _Ids["template"] = template.Id;
                _Names["template"] = template.Name;

                Memory memory = new Memory();
                memory.TenantId = tenant;
                memory.UserId = user;
                memory.Content = _Marker;
                memory = await db.Memories.CreateAsync(memory).ConfigureAwait(false);
                _Ids["memory"] = memory.Id;

                ModelEndpoint endpoint = new ModelEndpoint();
                endpoint.TenantId = tenant;
                endpoint.UserId = user;
                endpoint.Name = "iso-endpoint-" + suffix;
                endpoint.BaseUrl = "http://127.0.0.1:9/" + _Marker;
                endpoint.Enabled = false;
                endpoint = await db.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);
                _Ids["endpoint"] = endpoint.Id;
                _Names["endpoint"] = endpoint.Name;

                Harbor harbor = new Harbor();
                harbor.TenantId = tenant;
                harbor.UserId = user;
                harbor.Name = "iso-harbor-" + suffix;
                harbor = await db.Harbors.CreateAsync(harbor).ConfigureAwait(false);
                _Ids["harbor"] = harbor.Id;
                _Names["harbor"] = harbor.Name;

                Release release = new Release();
                release.TenantId = tenant;
                release.UserId = user;
                release.VesselId = vessel.Id;
                release.Title = "iso-release-" + suffix;
                release.Summary = _Marker;
                release = await db.Releases.CreateAsync(release).ConfigureAwait(false);
                _Ids["release"] = release.Id;

                FleetAction action = new FleetAction();
                action.TenantId = tenant;
                action.UserId = user;
                action.Name = "iso-action-" + suffix;
                action.Description = _Marker;
                action = await db.FleetActions.CreateAsync(action).ConfigureAwait(false);
                _Ids["fleetaction"] = action.Id;

                PlanningSession planning = new PlanningSession();
                planning.TenantId = tenant;
                planning.UserId = user;
                planning.CaptainId = captain.Id;
                planning.VesselId = vessel.Id;
                planning.Title = "iso-planning-" + suffix;
                planning = await db.PlanningSessions.CreateAsync(planning).ConfigureAwait(false);
                _Ids["planning"] = planning.Id;

                ObjectiveRefinementSession refinement = new ObjectiveRefinementSession();
                refinement.TenantId = tenant;
                refinement.UserId = user;
                refinement.ObjectiveId = objective.Id;
                refinement.CaptainId = captain.Id;
                refinement.Title = "iso-refinement-" + suffix;
                refinement = await db.ObjectiveRefinementSessions.CreateAsync(refinement).ConfigureAwait(false);
                _Ids["refinement"] = refinement.Id;
            }
        }

        /// <summary>
        /// Build arguments for a tool from its input schema: id-like properties get tenant A's ids, other required
        /// properties get harmless values. Returns null when the schema cannot be read.
        /// </summary>
        private string? BuildArguments(McpRemoteTool tool, out List<string> unmapped, out bool usesId)
        {
            unmapped = new List<string>();
            usesId = false;
            if (String.IsNullOrEmpty(tool.InputSchemaJson)) return "{}";

            McpToolInputSchema schema;
            try
            {
                schema = JsonHelper.Deserialize<McpToolInputSchema>(tool.InputSchemaJson);
            }
            catch (Exception)
            {
                return null;
            }

            StringBuilder sb = new StringBuilder("{");
            bool first = true;
            foreach (KeyValuePair<string, McpToolInputProperty> property in schema.Properties)
            {
                string name = property.Key;
                // set_vessel_health_override validates its optional status before resolving the vessel.
                bool required = schema.Required.Contains(name)
                    || (tool.Name == "set_vessel_health_override" && name == "status");
                string? idValue = MapId(tool.Name, name, property.Value.Type);
                string? json = null;
                if (idValue != null)
                {
                    json = idValue;
                    usesId = true;
                }
                else if (IsIdLike(tool.Name, name))
                {
                    if (required) unmapped.Add(name);
                    continue;
                }
                else if (required)
                {
                    json = DefaultValue(tool.Name, name, property.Value.Type);
                }

                if (json == null) continue;
                if (!first) sb.Append(',');
                first = false;
                sb.Append(System.Text.Json.JsonSerializer.Serialize(name)).Append(':').Append(json);
            }

            sb.Append('}');
            return sb.ToString();
        }

        private string? MapId(string tool, string property, string? type)
        {
            string? single = null;
            switch (property)
            {
                case "vesselId":
                case "sourceVesselId":
                    single = _Ids["vessel"]; break;
                case "fleetId":
                    single = _Ids["fleet"]; break;
                case "captainId":
                case "defaultCaptainId":
                case "toCaptainId":
                    single = _Ids["captain"]; break;
                case "missionId":
                case "parentMissionId":
                case "sourceMissionId":
                    single = _Ids["mission"]; break;
                case "voyageId":
                case "sourceVoyageId":
                    single = _Ids["voyage"]; break;
                case "dockId":
                    single = _Ids["dock"]; break;
                case "entryId":
                    single = _Ids["merge"]; break;
                case "checkRunId":
                    single = _Ids["checkrun"]; break;
                case "objectiveId":
                case "parentObjectiveId":
                    single = _Ids["objective"]; break;
                case "memoryId":
                    single = _Ids["memory"]; break;
                case "endpointId":
                    single = _Ids["endpoint"]; break;
                case "harborId":
                    single = _Ids["harbor"]; break;
                case "releaseId":
                    single = _Ids["release"]; break;
                case "actionId":
                    single = _Ids["fleetaction"]; break;
                case "playbookId":
                    single = _Ids["playbook"]; break;
                case "sessionId":
                    single = tool.Contains("refinement", StringComparison.Ordinal) ? _Ids["refinement"] : _Ids["planning"]; break;
                case "id":
                    if (tool.EndsWith("_playbook", StringComparison.Ordinal)) single = _Ids["playbook"];
                    break;
                case "name":
                    if (tool.StartsWith("create_", StringComparison.Ordinal)) break;
                    if (tool.EndsWith("_persona", StringComparison.Ordinal)) single = _Names["persona"];
                    else if (tool.EndsWith("_pipeline", StringComparison.Ordinal)) single = _Names["pipeline"];
                    else if (tool.EndsWith("_prompt_template", StringComparison.Ordinal)) single = _Names["template"];
                    break;
            }

            if (single != null)
            {
                return String.Equals(type, "array", StringComparison.Ordinal)
                    ? "[" + System.Text.Json.JsonSerializer.Serialize(single) + "]"
                    : System.Text.Json.JsonSerializer.Serialize(single);
            }

            string? listKey = null;
            switch (property)
            {
                case "vesselIds": listKey = "vessel"; break;
                case "missionIds": listKey = "mission"; break;
                case "voyageIds": listKey = "voyage"; break;
                case "fleetIds": listKey = "fleet"; break;
                case "checkRunIds": listKey = "checkrun"; break;
                case "releaseIds": listKey = "release"; break;
                case "planningSessionIds": listKey = "planning"; break;
                case "refinementSessionIds": listKey = "refinement"; break;
                case "blockedByObjectiveIds": listKey = "objective"; break;
                case "entryIds": listKey = "merge"; break;
                case "ids":
                    if (tool == "delete_vessels") listKey = "vessel";
                    else if (tool == "delete_fleets") listKey = "fleet";
                    else if (tool == "delete_captains") listKey = "captain";
                    else if (tool == "delete_missions") listKey = "mission";
                    else if (tool == "delete_voyages") listKey = "voyage";
                    else if (tool == "delete_docks") listKey = "dock";
                    else if (tool == "delete_signals") listKey = "signal";
                    else if (tool == "reorder_objectives" || tool == "reorder_backlog_items") listKey = "objective";
                    break;
            }

            if (listKey == null) return null;
            return "[" + System.Text.Json.JsonSerializer.Serialize(_Ids[listKey]) + "]";
        }

        private static bool IsIdLike(string tool, string property)
        {
            if (property == "id" || property == "ids") return true;
            if (property == "name")
            {
                if (tool.StartsWith("create_", StringComparison.Ordinal)) return false;
                return tool.EndsWith("_persona", StringComparison.Ordinal)
                    || tool.EndsWith("_pipeline", StringComparison.Ordinal)
                    || tool.EndsWith("_prompt_template", StringComparison.Ordinal);
            }

            return property.EndsWith("Id", StringComparison.Ordinal) || property.EndsWith("Ids", StringComparison.Ordinal);
        }

        private static string DefaultValue(string tool, string property, string? type)
        {
            switch (property)
            {
                case "status": return tool == "set_vessel_health_override" ? "\"Pass\"" : "\"Cancelled\"";
                case "criterion": return "\"Overall\"";
                case "type": return "\"Build\"";
                case "deliveryMode": return "\"InlineFullContent\"";
                case "entityType": return "\"missions\"";
            }

            switch (type)
            {
                case "integer":
                case "number":
                    return "1";
                case "boolean":
                    return "false";
                case "array":
                    return "[]";
                case "object":
                    return "{}";
                default:
                    return "\"iso-" + property + "\"";
            }
        }

        private static bool IsOwnerReadControl(string tool)
        {
            return tool.StartsWith("get_", StringComparison.Ordinal)
                || tool == "mission_status"
                || tool == "voyage_status";
        }

        private async Task ExpectOnlyAsync(McpToolClient client, List<string> failures, object arguments, string? expectedId)
        {
            string args = JsonHelper.Serialize(arguments);
            Armada.Runtimes.Mcp.McpToolCallResult result = await client.CallToolResultAsync("enumerate", args).ConfigureAwait(false);
            if (result.IsError)
            {
                failures.Add(args + ": error: " + Truncate(result.Text));
                return;
            }

            EnumerationResult<McpLengthHints> page = JsonHelper.Deserialize<EnumerationResult<McpLengthHints>>(result.Text);
            List<string> ids = page.Objects.Select(o => o.Id).ToList();
            bool ok = expectedId == null ? ids.Count == 0 : ids.Count == 1 && ids[0] == expectedId;
            if (!ok)
                failures.Add(args + ": expected " + (expectedId ?? "no records") + ", got [" + String.Join(", ", ids) + "]");
        }

                private static string Truncate(string text)
        {
            string flat = text.Replace('\n', ' ').Replace('\r', ' ');
            return flat.Length <= 300 ? flat : flat.Substring(0, 300) + "...";
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        /// <summary>
        /// True when a cross-tenant call was refused: the tool returned the typed NotFound error, or a delete or purge
        /// tool reported zero rows. A failed call (isError) is never a denial (it could be a crash or a rate limit).
        /// </summary>
        private static bool IsDenied(Armada.Runtimes.Mcp.McpToolCallResult result)
        {
            if (result.IsError) return false;
            McpToolResultProbe probe = McpToolResultProbe.From(result);
            if (probe.ErrorCode == McpToolErrorCodeEnum.NotFound) return true;
            return probe.Deleted == 0 || probe.EntriesPurged == 0;
        }
    }
}
