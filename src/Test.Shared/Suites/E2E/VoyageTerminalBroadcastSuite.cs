namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Server;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// The health check settles a voyage and the owner socket receives <c>voyage.changed</c> with the status
    /// stored on that voyage.
    /// </summary>
    public sealed class VoyageTerminalBroadcastSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "E2E.VoyageTerminalBroadcast";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync(
                "health_check_broadcasts_the_stored_voyage_status",
                "HealthCheckAsync broadcasts the status settlement stored",
                TestTags.Positive,
                async () =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(this).ConfigureAwait(false);
                    E2ETenantUser owner = await E2ETenantUser.CreateAsync(fx.AuthClient, "voyage-status").ConfigureAwait(false);
                    DatabaseDriver database = ServerDatabase(fx.Server);

                    Voyage failedVoyage = await CreateInProgressVoyageAsync(
                        database,
                        owner,
                        "Mixed outcome",
                        MissionStatusEnum.Complete,
                        MissionStatusEnum.Failed).ConfigureAwait(false);
                    Voyage completeVoyage = await CreateInProgressVoyageAsync(
                        database,
                        owner,
                        "Successful outcome",
                        MissionStatusEnum.Complete).ConfigureAwait(false);

                    using (WebSocketTestClient ownerSocket = await WebSocketTestClient.ConnectAsync(fx.RestPort, owner.BearerToken).ConfigureAwait(false))
                    {
                        await ownerSocket.SendAsync(new Dictionary<string, object> { ["Route"] = "subscribe" }).ConfigureAwait(false);
                        string? snapshot = await ownerSocket.WaitForAsync(m => E2eWebSocketFrame.IsType(m, "status.snapshot")).ConfigureAwait(false);
                        AssertNotNull(snapshot, "subscribe should return a status snapshot");

                        await fx.Server.Admiral.HealthCheckAsync().ConfigureAwait(false);

                        Voyage? storedFailed = await database.Voyages.ReadAsync(failedVoyage.Id).ConfigureAwait(false);
                        Voyage? storedComplete = await database.Voyages.ReadAsync(completeVoyage.Id).ConfigureAwait(false);
                        AssertNotNull(storedFailed, "the mixed voyage is still stored");
                        AssertNotNull(storedComplete, "the successful voyage is still stored");
                        AssertEqual(VoyageStatusEnum.Failed, storedFailed!.Status, "settlement stores Failed when a mission failed");
                        AssertEqual(VoyageStatusEnum.Complete, storedComplete!.Status, "settlement stores Complete when every mission completed");

                        string? failedFrame = await ownerSocket.WaitForAsync(m => E2eWebSocketEventFrame.ParseOfType(m, "voyage.changed")?.Data?.Id == failedVoyage.Id).ConfigureAwait(false);
                        AssertNotNull(failedFrame, "the owner receives voyage.changed for the mixed voyage");
                        E2eWebSocketEventFrame failedEvent = E2eWebSocketEventFrame.ParseOfType(failedFrame, "voyage.changed")!;
                        AssertEqual(VoyageStatusEnum.Failed.ToString(), failedEvent.Data?.Status, "voyage.changed status for the mixed voyage");

                        string? completeFrame = await ownerSocket.WaitForAsync(m => E2eWebSocketEventFrame.ParseOfType(m, "voyage.changed")?.Data?.Id == completeVoyage.Id).ConfigureAwait(false);
                        AssertNotNull(completeFrame, "the owner receives voyage.changed for the successful voyage");
                        E2eWebSocketEventFrame completeEvent = E2eWebSocketEventFrame.ParseOfType(completeFrame, "voyage.changed")!;
                        AssertEqual(VoyageStatusEnum.Complete.ToString(), completeEvent.Data?.Status, "voyage.changed status for the successful voyage");
                    }
                }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Voyage terminal broadcast",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static DatabaseDriver ServerDatabase(ArmadaServer server)
        {
            FieldInfo? field = typeof(ArmadaServer).GetField("_Database", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new InvalidOperationException("The server database field is not available.");
            DatabaseDriver? database = field.GetValue(server) as DatabaseDriver;
            if (database == null) throw new InvalidOperationException("The server database is not available.");
            return database;
        }

        private static async Task<Voyage> CreateInProgressVoyageAsync(
            DatabaseDriver database,
            E2ETenantUser owner,
            string title,
            params MissionStatusEnum[] missionStatuses)
        {
            Voyage voyage = new Voyage(title);
            voyage.Status = VoyageStatusEnum.InProgress;
            voyage.TenantId = owner.TenantId;
            voyage.UserId = owner.UserId;
            voyage = await database.Voyages.CreateAsync(voyage).ConfigureAwait(false);

            for (int i = 0; i < missionStatuses.Length; i++)
            {
                Mission mission = new Mission(title + " mission " + (i + 1).ToString());
                mission.VoyageId = voyage.Id;
                mission.TenantId = owner.TenantId;
                mission.UserId = owner.UserId;
                mission.Status = missionStatuses[i];
                await database.Missions.CreateAsync(mission).ConfigureAwait(false);
            }

            return voyage;
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
    }
}
