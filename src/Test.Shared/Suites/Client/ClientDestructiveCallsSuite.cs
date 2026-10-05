namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Client.Models;
    using Armada.Core.Enums;
    using Test.Shared.Infrastructure;
    using Test.Shared.Suites.Tui;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Armada.Client calls that would stop, restart, reset, rebuild, roll back, or restore the server, checked against a
    /// recording stub (request method, path, body, and reply parsing) instead of the shared live test server.
    /// </summary>
    public sealed class ClientDestructiveCallsSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "Client.Destructive";

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(TuiCase.Async(Suite, "server_lifecycle", "Stop, restart, and reset post to their routes; errors map to ArmadaApiException", async () =>
            {
                StubHttpHandler stub = new StubHttpHandler();
                stub.Json("POST", "/api/v1/server/stop", "{}");
                stub.Json("POST", "/api/v1/server/restart", "{}");
                stub.On("POST", "/api/v1/server/reset", body => StubHttpHandler.Response(HttpStatusCode.Forbidden, "{\"Error\":\"Forbidden\",\"Message\":\"Global admin required\"}"));
                using (ArmadaClient client = new ArmadaClient(new ArmadaClientOptions("http://127.0.0.1:9"), stub))
                {
                    await client.StopServerAsync();
                    await client.RestartServerAsync();
                    ArmadaApiException ex = await ClientContract.ExpectErrorAsync(() => client.ResetServerAsync(), "reset refused", 403, 403);
                    AssertEqual("Global admin required", ex.Message, "server message");
                }

                AssertEqual(1, stub.Count("POST /api/v1/server/stop"), "stop");
                AssertEqual(1, stub.Count("POST /api/v1/server/restart"), "restart");
                AssertEqual(1, stub.Count("POST /api/v1/server/reset"), "reset");
            }));

            cases.Add(TuiCase.Async(Suite, "rebuild_and_rollback", "Rebuild posts a request body and reads the status; rollback reads the status", async () =>
            {
                StubHttpHandler stub = new StubHttpHandler();
                stub.Json("POST", "/api/v1/server/rebuild", "{\"RebuildId\":\"rb_1\",\"Slot\":\"slot-2\",\"Status\":\"Building\"}");
                stub.Json("POST", "/api/v1/server/rollback", "{\"Slot\":\"slot-1\",\"PreviousSlot\":\"slot-2\",\"Status\":\"RolledBack\"}");
                using (ArmadaClient client = new ArmadaClient(new ArmadaClientOptions("http://127.0.0.1:9"), stub))
                {
                    RebuildStatus? rebuild = await client.RebuildServerAsync();
                    AssertEqual("rb_1", rebuild?.RebuildId, "rebuild id");
                    AssertEqual<ServerRebuildStatusEnum?>(ServerRebuildStatusEnum.Building, rebuild?.Status, "rebuild status");
                    RebuildStatus? rollback = await client.RollbackServerAsync();
                    AssertEqual("slot-2", rollback?.PreviousSlot, "rollback slot");
                }

                AssertTrue(stub.Bodies.Any(b => b.StartsWith("{", StringComparison.Ordinal)), "rebuild sends a JSON body");
            }));

            cases.Add(TuiCase.Async(Suite, "restore_backup", "Restore posts the archive bytes with the original file name", async () =>
            {
                StubHttpHandler stub = new StubHttpHandler();
                stub.Json("POST", "/api/v1/restore", "{\"Success\":true}");
                using (ArmadaClient client = new ArmadaClient(new ArmadaClientOptions("http://127.0.0.1:9"), stub))
                {
                    ArmadaRawJson reply = await client.RestoreBackupAsync(new byte[] { 80, 75, 3, 4 }, "armada-backup.zip");
                    AssertContains("\"Success\":true", reply.Json, "reply");
                }

                AssertEqual(1, stub.Count("POST /api/v1/restore"), "posted");
                AssertEqual("PK\u0003\u0004", stub.Bodies.Last(), "archive bytes sent as the body");
            }));

            return new TestSuiteDescriptor(suiteId: Suite, displayName: "Armada.Client destructive server calls (recording stub)", cases: cases);
        }

        #endregion
    }
}
