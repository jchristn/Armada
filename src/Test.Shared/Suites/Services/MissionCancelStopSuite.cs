namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Reflection;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Cancelling a mission or a voyage on any surface (REST, MCP, WebSocket) stops the captain's agent through the owned
    /// lifecycle before the mission is marked Cancelled. An unconfirmed stop leaves the mission, captain and process
    /// untouched so nothing else is assigned while the agent may still run.
    /// </summary>
    public sealed class MissionCancelStopSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Services.MissionCancelStop";
        private const int SyntheticPid = 178001;

        #endregion

        #region Public-Methods

        /// <summary>Build one case per surface and target, with a confirmed and an unconfirmed stop.</summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (string surface in new[] { "rest", "mcp", "ws" })
            foreach (bool voyage in new[] { false, true })
            foreach (bool confirm in new[] { true, false })
            {
                string s = surface;
                bool v = voyage, confirmed = confirm;
                string name = s + (v ? "_voyage" : "_mission") + (confirmed ? "_stops_agent" : "_unconfirmed_stop_keeps_assignment");
                string description = s.ToUpperInvariant() + (v ? " voyage cancel: " : " mission cancel: ")
                    + (confirmed ? "the agent is stopped once before the mission is cancelled and the captain released"
                                 : "an unconfirmed stop is reported and leaves the mission, captain and process unchanged");
                cases.Add(new TestCaseDescriptor(Suite, name, description, async ct => await RunAsync(s, v, confirmed).ConfigureAwait(false),
                    tags: confirmed ? null : new List<string> { TestTags.Negative }));
            }

            return new TestSuiteDescriptor(Suite, "Mission cancel stops the agent", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task RunAsync(string surface, bool voyageCancel, bool confirmed)
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            // Installed before any synthetic PID is stored: no path may treat it as a local process.
            fx.Server.RuntimeFactory.Override(AgentRuntimeEnum.ClaudeCode, () => throw new InvalidOperationException("Synthetic PID fixture forbids local process operations."));

            LoggingModule quiet = new LoggingModule(); quiet.Settings.EnableConsole = false;
            using DatabaseDriver db = DatabaseDriverFactory.Create(fx.Settings.Database, quiet);
            await db.InitializeAsync().ConfigureAwait(false);

            Voyage voyage = await db.Voyages.CreateAsync(new Voyage("cancel-stop voyage")).ConfigureAwait(false);
            Captain captain = await CaptainAssignmentFixture.CreateAsync(db, new Captain("cancel-stop") { State = CaptainStateEnum.Working, ProcessId = SyntheticPid }).ConfigureAwait(false);
            // Voyage cancel only touches Pending, Assigned and Blocked missions, so the voyage case holds an Assigned one.
            MissionStatusEnum seededStatus = voyageCancel ? MissionStatusEnum.Assigned : MissionStatusEnum.InProgress;
            Mission mission = await db.Missions.CreateAsync(new Mission("running assignment")
            {
                VoyageId = voyage.Id, CaptainId = captain.Id, Status = seededStatus, ProcessId = SyntheticPid
            }).ConfigureAwait(false);
            captain.Assignment = mission;
            await CaptainAssignmentFixture.UpdateAsync(db, captain).ConfigureAwait(false);

            IAdmiralService admiral = (IAdmiralService)typeof(Armada.Server.ArmadaServer).GetField("_Admiral", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fx.Server)!;
            Func<Captain, Task>? originalStop = admiral.OnStopAgent;
            List<string> stops = new List<string>();
            admiral.OnStopAgent = held =>
            {
                stops.Add(held.Id + "|" + held.ActiveMissionIds.SingleOrDefault());
                if (!confirmed) throw new TimeoutException("Owner did not confirm exit.");
                return Task.CompletedTask;
            };

            try
            {
                string reply = await CancelAsync(fx, surface, voyageCancel, voyageCancel ? voyage.Id : mission.Id, confirmed).ConfigureAwait(false);
                Captain after = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                Mission afterMission = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;

                AssertEqual(1, stops.Count, "The owned lifecycle must receive exactly one stop. Reply: " + reply);
                AssertEqual(captain.Id + "|" + mission.Id, stops[0], "The stop must run while the captain still points at the mission, so the Harbor owner can be resolved.");

                if (confirmed)
                {
                    AssertEqual(MissionStatusEnum.Cancelled, afterMission.Status, "Reply: " + reply);
                    AssertNull(afterMission.ProcessId);
                    AssertEqual(CaptainStateEnum.Idle, after.State);
                    AssertNull(after.ProcessId);
                    AssertNull(after.ActiveMissionIds.SingleOrDefault());
                }
                else
                {
                    AssertEqual(seededStatus, afterMission.Status, "An unconfirmed stop must not cancel the mission. Reply: " + reply);
                    AssertEqual(CaptainStateEnum.Working, after.State);
                    AssertNull(after.ProcessId);
                    AssertEqual((int?)SyntheticPid, afterMission.ProcessId);
                    AssertEqual(mission.Id, after.ActiveMissionIds.SingleOrDefault());
                    AssertContains("did not confirm it stopped", reply);
                    if (voyageCancel) AssertContains(mission.Id, reply, "The voyage reply must name the mission it could not cancel.");
                }
            }
            finally
            {
                // The fixture never owns a real PID: clear the synthetic assignment before server shutdown.
                captain = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                captain.ProcessId = null; captain.Assignment = null; captain.State = CaptainStateEnum.Idle;
                await CaptainAssignmentFixture.UpdateAsync(db, captain).ConfigureAwait(false);
                admiral.OnStopAgent = originalStop;
            }
        }

        private static async Task<string> CancelAsync(SecurityTestServer fx, string surface, bool voyageCancel, string id, bool confirmed)
        {
            switch (surface)
            {
                case "rest":
                {
                    using HttpClient client = fx.CreateRestClient(true);
                    using HttpResponseMessage response = await client.DeleteAsync((voyageCancel ? "/api/v1/voyages/" : "/api/v1/missions/") + id).ConfigureAwait(false);
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    HttpStatusCode expected = voyageCancel || confirmed ? HttpStatusCode.OK : HttpStatusCode.Conflict;
                    AssertEqual(expected, response.StatusCode, "REST cancel status. Body: " + body);
                    return body;
                }
                case "mcp":
                {
                    using HttpClient mcp = fx.CreateMcpClient(true);
                    return await McpTestCalls.CallToolAsync(mcp, voyageCancel ? "cancel_voyage" : "cancel_mission",
                        voyageCancel ? (object)new { voyageId = id } : new { missionId = id }).ConfigureAwait(false);
                }
                default:
                {
                    Uri rest = new Uri(fx.BaseUrl);
                    using WebSocketTestClient ws = await WebSocketTestClient.ConnectAsync(rest.Port, null, new Dictionary<string, string> { ["X-Api-Key"] = fx.ApiKey }).ConfigureAwait(false);
                    string action = voyageCancel ? "cancel_voyage" : "cancel_mission";
                    await ws.SendAsync(new { Route = "command", action = action, id = id }).ConfigureAwait(false);
                    string? reply = await ws.WaitForAsync(m => m.Contains("\"" + action + "\"") && (m.Contains("command.result") || m.Contains("command.error"))).ConfigureAwait(false);
                    AssertNotNull(reply, "No WebSocket reply to " + action);
                    return reply!;
                }
            }
        }

        #endregion
    }
}
