namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
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

    /// <summary>Real loopback stop requests must delegate process identity and acknowledgement to the owned lifecycle.</summary>
    public sealed class CaptainStopRouteSuite : IArmadaTestSuite
    {
        /// <summary>Build the HTTP stop routing and unconfirmed-stop cases.</summary>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (bool confirm in new[] { true, false })
            foreach (bool all in new[] { false, true })
            {
                bool confirmed = confirm, bulk = all;
                cases.Add(new TestCaseDescriptor("Services.CaptainStopRoute", (confirmed ? "owned_stop" : "unconfirmed_stop") + (bulk ? "_all" : "_single"),
                    (bulk ? "REST stop-all: " : "REST stop: ") + (confirmed ? "owned lifecycle is called once without constructing a local PID runtime" : "unconfirmed exits retain assignment and cannot report success"),
                    async ct =>
                    {
                        using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                        await fx.StartAsync().ConfigureAwait(false);
                        using HttpClient client = fx.CreateRestClient(true);
                        int localCreations = 0;
                        fx.Server.RuntimeFactory.Override(AgentRuntimeEnum.ClaudeCode, () =>
                        {
                            localCreations++;
                            throw new InvalidOperationException("A remote/native handle must never be treated as an Admiral-local PID by the route.");
                        });
                        LoggingModule quiet = new LoggingModule(); quiet.Settings.EnableConsole = false;
                        using DatabaseDriver db = DatabaseDriverFactory.Create(fx.Settings.Database, quiet);
                        await db.InitializeAsync().ConfigureAwait(false);
                        Captain captain = await db.Captains.CreateAsync(new Captain("route-stop") { State = CaptainStateEnum.Working, ProcessId = 177001 }).ConfigureAwait(false);
                        Mission mission = await db.Missions.CreateAsync(new Mission("held assignment") { CaptainId = captain.Id, Status = MissionStatusEnum.InProgress, ProcessId = captain.ProcessId }).ConfigureAwait(false);
                        captain.CurrentMissionId = mission.Id;
                        await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
                        IAdmiralService admiral = (IAdmiralService)typeof(Armada.Server.ArmadaServer).GetField("_Admiral", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fx.Server)!;
                        Func<Captain, Task>? originalStop = admiral.OnStopAgent;
                        int logicalStops = 0;
                        admiral.OnStopAgent = held =>
                        {
                            logicalStops++;
                            AssertEqual(captain.Id, held.Id);
                            if (!confirmed) throw new TimeoutException("Owner did not confirm exit.");
                            return Task.CompletedTask;
                        };
                        try
                        {
                            using HttpResponseMessage response = await client.PostAsync(bulk ? "/api/v1/captains/stop-all" : "/api/v1/captains/" + captain.Id + "/stop", null, ct).ConfigureAwait(false);
                            Captain after = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                            Mission active = (await db.Missions.ReadAsync(mission.Id).ConfigureAwait(false))!;
                            AssertEqual(0, localCreations, "The HTTP route must not perform a preliminary local stop.");
                            AssertEqual(1, logicalStops, "The owner-aware lifecycle must receive exactly one stop.");
                            if (confirmed)
                            {
                                AssertEqual(HttpStatusCode.OK, response.StatusCode);
                                AssertEqual(CaptainStateEnum.Idle, after.State);
                                AssertNull(after.ProcessId);
                            }
                            else
                            {
                                AssertEqual(HttpStatusCode.Conflict, response.StatusCode, "Unconfirmed stops must report a state conflict, never success.");
                                AssertEqual(CaptainStateEnum.Working, after.State);
                                AssertEqual((int?)177001, after.ProcessId);
                                AssertEqual(mission.Id, after.CurrentMissionId);
                                AssertEqual(MissionStatusEnum.InProgress, active.Status);
                                AssertContains("Recall deferred", active.FailureReason!);
                                AssertEqual(MissionFailureKindEnum.Infra, active.FailureKind, "the deferred recall records a kind");
                            }
                        }
                        finally
                        {
                            // The fixture never owns a real PID: clear its synthetic assignment before server shutdown.
                            captain.ProcessId = null; captain.CurrentMissionId = null; captain.State = CaptainStateEnum.Idle;
                            await db.Captains.UpdateAsync(captain).ConfigureAwait(false);
                            admiral.OnStopAgent = originalStop;
                        }
                    }, tags: new List<string> { TestTags.Negative }));
            }
            cases.Add(new TestCaseDescriptor("Services.CaptainStopRoute", "tenant_stop_all_scope",
                "A tenant admin stop-all cannot stop another tenant's captain", async ct =>
                {
                    using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                    await fx.StartAsync().ConfigureAwait(false);
                    using HttpClient admin = fx.CreateRestClient(true);
                    // Installed before any synthetic PID is stored, including setup/shutdown failure paths.
                    fx.Server.RuntimeFactory.Override(AgentRuntimeEnum.ClaudeCode, () => throw new InvalidOperationException("Synthetic PID fixture forbids local process operations."));
                    async Task<TenantMetadata> Tenant(string name)
                    {
                        using HttpResponseMessage response = await admin.PostAsync("/api/v1/tenants", JsonHelper.ToJsonContent(new { Name = name }), ct).ConfigureAwait(false);
                        return await JsonHelper.DeserializeAsync<TenantMetadata>(response).ConfigureAwait(false);
                    }
                    TenantMetadata ownTenant = await Tenant("stop-own").ConfigureAwait(false);
                    TenantMetadata otherTenant = await Tenant("stop-other").ConfigureAwait(false);
                    using HttpResponseMessage userResponse = await admin.PostAsync("/api/v1/users", JsonHelper.ToJsonContent(new
                    {
                        TenantId = ownTenant.Id, Email = "tenant-stop@example.test", PasswordSha256 = UserMaster.ComputePasswordHash("fixture-only"), IsTenantAdmin = true
                    }), ct).ConfigureAwait(false);
                    UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResponse).ConfigureAwait(false);
                    using HttpResponseMessage credentialResponse = await admin.PostAsync("/api/v1/credentials", JsonHelper.ToJsonContent(new { TenantId = ownTenant.Id, UserId = user.Id, Name = "fixture" }), ct).ConfigureAwait(false);
                    Credential credential = await JsonHelper.DeserializeAsync<Credential>(credentialResponse).ConfigureAwait(false);
                    using HttpResponseMessage otherUserResponse = await admin.PostAsync("/api/v1/users", JsonHelper.ToJsonContent(new
                    {
                        TenantId = otherTenant.Id, Email = "other-tenant-stop@example.test", PasswordSha256 = UserMaster.ComputePasswordHash("fixture-only")
                    }), ct).ConfigureAwait(false);
                    UserMaster otherUser = await JsonHelper.DeserializeAsync<UserMaster>(otherUserResponse).ConfigureAwait(false);
                    using HttpClient tenantClient = fx.CreateRestClient(false);
                    tenantClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.BearerToken);
                    LoggingModule quiet = new LoggingModule(); quiet.Settings.EnableConsole = false;
                    using DatabaseDriver db = DatabaseDriverFactory.Create(fx.Settings.Database, quiet);
                    await db.InitializeAsync().ConfigureAwait(false);
                    Captain own = await db.Captains.CreateAsync(new Captain("owned") { TenantId = ownTenant.Id, UserId = user.Id, State = CaptainStateEnum.Working, ProcessId = 177001 }).ConfigureAwait(false);
                    Captain other = await db.Captains.CreateAsync(new Captain("foreign") { TenantId = otherTenant.Id, UserId = otherUser.Id, State = CaptainStateEnum.Working, ProcessId = 177002 }).ConfigureAwait(false);
                    Vessel foreignVessel = await db.Vessels.CreateAsync(new Vessel("foreign vessel", "https://example.test/fixture.git") { TenantId = otherTenant.Id, UserId = otherUser.Id }).ConfigureAwait(false);
                    Objective foreignObjective = await db.Objectives.CreateAsync(new Objective { Title = "foreign objective", TenantId = otherTenant.Id, UserId = otherUser.Id }).ConfigureAwait(false);
                    PlanningSession foreignPlan = await db.PlanningSessions.CreateAsync(new PlanningSession
                    {
                        TenantId = otherTenant.Id, UserId = otherUser.Id, CaptainId = other.Id, VesselId = foreignVessel.Id, Status = PlanningSessionStatusEnum.Active
                    }).ConfigureAwait(false);
                    ObjectiveRefinementSession foreignRefinement = await db.ObjectiveRefinementSessions.CreateAsync(new ObjectiveRefinementSession
                    {
                        TenantId = otherTenant.Id, UserId = otherUser.Id, CaptainId = other.Id, ObjectiveId = foreignObjective.Id, VesselId = foreignVessel.Id, Status = ObjectiveRefinementSessionStatusEnum.Active
                    }).ConfigureAwait(false);
                    IAdmiralService admiral = (IAdmiralService)typeof(Armada.Server.ArmadaServer).GetField("_Admiral", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fx.Server)!;
                    Func<Captain, Task>? originalStop = admiral.OnStopAgent;
                    List<string> stopped = new List<string>();
                    admiral.OnStopAgent = held => { stopped.Add(held.Id); return Task.CompletedTask; };
                    try
                    {
                        using HttpResponseMessage response = await tenantClient.PostAsync("/api/v1/captains/stop-all", null, ct).ConfigureAwait(false);
                        AssertEqual(HttpStatusCode.OK, response.StatusCode);
                        AssertEqual(CaptainStateEnum.Idle, (await db.Captains.ReadAsync(own.Id).ConfigureAwait(false))!.State);
                        AssertEqual(CaptainStateEnum.Working, (await db.Captains.ReadAsync(other.Id).ConfigureAwait(false))!.State, "Foreign captain must be untouched.");
                        AssertEqual(PlanningSessionStatusEnum.Active, (await db.PlanningSessions.ReadAsync(foreignPlan.Id).ConfigureAwait(false))!.Status, "Foreign planning session must be untouched.");
                        AssertEqual(ObjectiveRefinementSessionStatusEnum.Active, (await db.ObjectiveRefinementSessions.ReadAsync(foreignRefinement.Id).ConfigureAwait(false))!.Status, "Foreign refinement session must be untouched.");
                        AssertEqual(1, stopped.Count);
                        AssertEqual(own.Id, stopped[0]);
                    }
                    finally
                    {
                        foreach (Captain seeded in new[] { own, other })
                        {
                            seeded.ProcessId = null; seeded.State = CaptainStateEnum.Idle;
                            await db.Captains.UpdateAsync(seeded).ConfigureAwait(false);
                        }
                        admiral.OnStopAgent = originalStop;
                    }
                }, tags: new List<string> { TestTags.Negative }));

            return new TestSuiteDescriptor("Services.CaptainStopRoute", "Captain stop routing", cases);
        }
    }
}
