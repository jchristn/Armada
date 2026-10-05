namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Mcp;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// O-20 / O-04: a mission-scoped MCP token (minted for a captain's launch) authenticates on the MCP server as the
    /// mission's owner, only while the mission is assigned to or running on that captain. It is refused on REST, stops
    /// working when the mission ends, and adds exactly one permission to the owner's: updating the context of the
    /// mission's own vessel.
    /// </summary>
    public sealed class McpMissionTokenSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.McpMissionToken";

        private SecurityTestServer? _Server;
        private SessionTokenService? _Tokens;
        private E2ETenantUser? _Owner;
        private string _MissionId = "";
        private string _CaptainId = "";
        private string _MissionVesselId = "";
        private string _OtherVesselId = "";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("setup", "A regular user owns an in-progress mission on a vessel owned by another user", TestTags.Positive, async () =>
            {
                _Server = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                _Server.Settings.Mcp.ToolCallsPerSecond = 0;
                await _Server.StartAsync().ConfigureAwait(false);
                _Tokens = new SessionTokenService(_Server.Settings.SessionTokenEncryptionKey);
                E2ETenantUser vesselOwner;
                using (HttpClient admin = _Server.CreateRestClient(true))
                {
                    vesselOwner = await E2ETenantUser.CreateAsync(admin, "mtok-admin", true).ConfigureAwait(false);
                    _Owner = await E2ETenantUser.CreateAsync(admin, "mtok-user", false, vesselOwner.TenantId).ConfigureAwait(false);
                }

                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
                    Vessel missionVessel = new Vessel("mtok-vessel-" + suffix, "https://example.invalid/mtok-" + suffix + ".git");
                    missionVessel.TenantId = vesselOwner.TenantId;
                    missionVessel.UserId = vesselOwner.UserId;
                    missionVessel = await db.Vessels.CreateAsync(missionVessel).ConfigureAwait(false);
                    _MissionVesselId = missionVessel.Id;

                    Vessel otherVessel = new Vessel("mtok-other-" + suffix, "https://example.invalid/mtok-other-" + suffix + ".git");
                    otherVessel.TenantId = vesselOwner.TenantId;
                    otherVessel.UserId = vesselOwner.UserId;
                    otherVessel = await db.Vessels.CreateAsync(otherVessel).ConfigureAwait(false);
                    _OtherVesselId = otherVessel.Id;

                    Captain captain = new Captain("mtok-captain-" + suffix);
                    captain.TenantId = _Owner!.TenantId;
                    captain.UserId = _Owner.UserId;
                    captain.State = CaptainStateEnum.Working;
                    captain = await db.Captains.CreateAsync(captain).ConfigureAwait(false);
                    _CaptainId = captain.Id;

                    Mission mission = new Mission("mtok-mission-" + suffix, "mission token test");
                    mission.TenantId = _Owner.TenantId;
                    mission.UserId = _Owner.UserId;
                    mission.VesselId = missionVessel.Id;
                    mission.CaptainId = captain.Id;
                    mission.Status = MissionStatusEnum.InProgress;
                    mission = await db.Missions.CreateAsync(mission).ConfigureAwait(false);
                    _MissionId = mission.Id;
                }
            }));

            cases.Add(CaseAsync("mission_token_acts_as_owner_on_mcp", "The mission token authenticates on MCP as the mission's owner", TestTags.Positive, async () =>
            {
                RequireSetup();
                using (McpToolClient client = CreateClient(MintToken(_CaptainId)))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    Armada.Runtimes.Mcp.McpToolCallResult result = await client.CallToolResultAsync("enumerate", JsonHelper.Serialize(new { entityType = "missions" })).ConfigureAwait(false);
                    AssertFalse(result.IsError, "enumerate works with the mission token: " + result.Text);
                    EnumerationResult<McpLengthHints> page = JsonHelper.Deserialize<EnumerationResult<McpLengthHints>>(result.Text);
                    AssertTrue(page.Objects.Any(o => o.Id == _MissionId), "the owner's mission is visible to its captain");
                }
            }));

            cases.Add(CaseAsync("mission_token_updates_only_its_own_vessel_context", "The mission token may update the context of the mission's vessel only", TestTags.Negative, async () =>
            {
                RequireSetup();
                string marker = "model context " + Guid.NewGuid().ToString("N");
                using (McpToolClient client = CreateClient(MintToken(_CaptainId)))
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    Armada.Runtimes.Mcp.McpToolCallResult own = await client.CallToolResultAsync("update_vessel_context", JsonHelper.Serialize(new { vesselId = _MissionVesselId, modelContext = marker })).ConfigureAwait(false);
                    AssertFalse(own.IsError, "own vessel: " + own.Text);
                    AssertNull(McpToolResultProbe.From(own).ErrorCode, "own vessel context update succeeds: " + own.Text);

                    Armada.Runtimes.Mcp.McpToolCallResult other = await client.CallToolResultAsync("update_vessel_context", JsonHelper.Serialize(new { vesselId = _OtherVesselId, modelContext = marker })).ConfigureAwait(false);
                    AssertEqual(McpToolErrorCodeEnum.Forbidden, McpToolResultProbe.From(other).ErrorCode, "another vessel is refused for a regular owner");
                }

                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    Vessel? updated = await db.Vessels.ReadAsync(_MissionVesselId).ConfigureAwait(false);
                    AssertEqual(marker, updated?.ModelContext, "the mission vessel's context was updated");
                    Vessel? untouched = await db.Vessels.ReadAsync(_OtherVesselId).ConfigureAwait(false);
                    AssertFalse(String.Equals(marker, untouched?.ModelContext, StringComparison.Ordinal), "the other vessel is unchanged");
                }
            }));

            cases.Add(CaseAsync("mission_token_refused_on_rest", "REST refuses a mission-scoped token", TestTags.Negative, async () =>
            {
                RequireSetup();
                using (HttpClient rest = new HttpClient())
                {
                    rest.BaseAddress = new Uri(_Server!.BaseUrl);
                    rest.DefaultRequestHeaders.Add("X-Token", MintToken(_CaptainId));
                    HttpResponseMessage response = await rest.GetAsync("/api/v1/missions").ConfigureAwait(false);
                    AssertEqual(HttpStatusCode.Unauthorized, response.StatusCode, "mission tokens are MCP only");
                }
            }));

            cases.Add(CaseAsync("mission_token_for_another_captain_refused", "A token minted for a different captain is refused", TestTags.Negative, async () =>
            {
                RequireSetup();
                await AssertRefusedAsync(MintToken("cpt_not_the_mission_captain")).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("mission_token_stops_when_mission_ends", "The token stops working once the mission is no longer in progress", TestTags.Negative, async () =>
            {
                RequireSetup();
                string token = MintToken(_CaptainId);
                using (DatabaseDriver db = await OpenDatabaseAsync().ConfigureAwait(false))
                {
                    Mission? mission = await db.Missions.ReadAsync(_MissionId).ConfigureAwait(false);
                    mission!.Status = MissionStatusEnum.Complete;
                    await db.Missions.UpdateAsync(mission).ConfigureAwait(false);
                }

                await AssertRefusedAsync(token).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("cleanup", "Stop the dedicated server", TestTags.Positive, () =>
            {
                _Server?.Dispose();
                _Server = null;
                return Task.CompletedTask;
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "MCP Mission-Scoped Tokens (O-20, O-04)",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private void RequireSetup()
        {
            if (_Server == null || _Tokens == null || _Owner == null) throw new InvalidOperationException("setup case did not run");
        }

        private string MintToken(string captainId)
        {
            return _Tokens!.CreateMissionScopedToken(_Owner!.TenantId, _Owner.UserId, _MissionId, captainId, TimeSpan.FromMinutes(30)).Token!;
        }

        private McpToolClient CreateClient(string token)
        {
            return new McpToolClient(_Server!.McpUrl + "/mcp", token, null, null, 60);
        }

        private async Task AssertRefusedAsync(string token)
        {
            bool refused = false;
            using (McpToolClient client = CreateClient(token))
            {
                try
                {
                    await client.InitializeAsync().ConfigureAwait(false);
                    Armada.Runtimes.Mcp.McpToolCallResult result = await client.CallToolResultAsync("enumerate", JsonHelper.Serialize(new { entityType = "missions" })).ConfigureAwait(false);
                    refused = result.IsError;
                }
                catch (McpClientException ex)
                {
                    refused = ex.HttpStatusCode == 401;
                }
            }

            AssertTrue(refused, "the MCP server refuses the token with 401");
        }

        private async Task<DatabaseDriver> OpenDatabaseAsync()
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseDriver db = DatabaseDriverFactory.Create(_Server!.Settings.Database, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        private TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
