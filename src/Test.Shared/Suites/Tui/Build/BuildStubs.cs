namespace Test.Shared.Suites.Tui.Build
{
    using System;
    using System.Net;
    using Test.Shared.Infrastructure;

    /// <summary>
    /// Canned server responses for the BUILD screen suites (PascalCase JSON like the real server).
    /// </summary>
    internal static class BuildStubs
    {
        #region Public-Methods

        /// <summary>
        /// A signed-in stub with two fleets, two vessels, two captains, pipelines, and docks.
        /// </summary>
        /// <returns>Stub.</returns>
        public static StubHttpHandler Server()
        {
            StubHttpHandler stub = TuiFixtures.SignedInServer();
            stub.Json("GET", "/api/v1/fleets", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":9999,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" +
                "{\"Id\":\"flt_web\",\"Name\":\"Web\",\"Description\":\"Frontend repos\",\"DefaultPipelineId\":\"ppl_std\",\"Active\":true,\"CreatedUtc\":\"2026-10-01T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T10:00:00Z\"}," +
                "{\"Id\":\"flt_ops\",\"Name\":\"Ops\",\"Description\":\"Infra\",\"Active\":true,\"CreatedUtc\":\"2026-10-03T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-03T10:00:00Z\"}]}");
            stub.Json("GET", "/api/v1/vessels", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":9999,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" + Vessel("vsl_demo", "DemoRepo", "flt_web", "LocalMerge") + "," + Vessel("vsl_api", "ApiRepo", "flt_web", "PullRequest") + "]}");
            stub.Json("GET", "/api/v1/pipelines", "{\"Success\":true,\"Objects\":[{\"Id\":\"ppl_std\",\"Name\":\"Standard\",\"Stages\":[{\"PersonaName\":\"Worker\",\"Order\":1},{\"PersonaName\":\"Judge\",\"Order\":2}]}],\"TotalRecords\":1}");
            stub.Json("GET", "/api/v1/captains", "{\"Success\":true,\"Objects\":[" +
                "{\"Id\":\"cpt_1\",\"Name\":\"claude-1\",\"Runtime\":\"ClaudeCode\",\"State\":\"Idle\",\"Tier\":\"Premium\",\"CreatedUtc\":\"2026-10-01T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-01T10:00:00Z\"}," +
                "{\"Id\":\"cpt_2\",\"Name\":\"mux-1\",\"Runtime\":\"Mux\",\"State\":\"Quarantined\",\"QuarantineReason\":\"Too many failures\",\"RuntimeOptionsJson\":\"{\\\"schemaVersion\\\":1,\\\"endpoint\\\":\\\"local-llm\\\"}\",\"CurrentMissionId\":\"msn_1234567890\",\"CreatedUtc\":\"2026-10-02T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-02T10:00:00Z\"}],\"TotalRecords\":2}");
            stub.Json("GET", "/api/v1/docks", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":25,\"TotalPages\":1,\"TotalRecords\":2,\"Objects\":[" +
                "{\"Id\":\"dck_1\",\"VesselId\":\"vsl_demo\",\"CaptainId\":\"cpt_1\",\"BranchName\":\"armada/msn_1\",\"WorktreePath\":\"/tmp/docks/one\",\"Active\":true,\"CreatedUtc\":\"2026-10-04T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-04T09:00:00Z\"}," +
                "{\"Id\":\"dck_2\",\"VesselId\":\"vsl_api\",\"BranchName\":\"armada/msn_2\",\"WorktreePath\":\"/tmp/docks/two\",\"Active\":false,\"CreatedUtc\":\"2026-10-03T09:00:00Z\",\"LastUpdateUtc\":\"2026-10-03T09:00:00Z\"}]}");
            stub.Json("GET", "/api/v1/users", "{\"Success\":true,\"Objects\":[],\"TotalRecords\":0}");
            stub.Json("GET", "/api/v1/model-endpoints", "[]");
            return stub;
        }

        /// <summary>
        /// A vessel's JSON.
        /// </summary>
        /// <param name="id">Id.</param>
        /// <param name="name">Name.</param>
        /// <param name="fleetId">Fleet id.</param>
        /// <param name="landingMode">Landing mode.</param>
        /// <returns>JSON.</returns>
        public static string Vessel(string id, string name, string fleetId, string landingMode)
        {
            return "{\"Id\":\"" + id + "\",\"Name\":\"" + name + "\",\"FleetId\":\"" + fleetId + "\",\"RepoUrl\":\"https://github.com/acme/" + name.ToLowerInvariant() + ".git\",\"DefaultBranch\":\"main\",\"LandingMode\":\"" + landingMode +
                "\",\"LocalPath\":\"/tmp/" + name + "\",\"WorkingDirectory\":\"/work/" + name + "\",\"EnableModelContext\":true,\"ModelContext\":\"Uses xunit.\",\"ProtectedPathPatterns\":[\".env*\"],\"ReleaseBranchPrefix\":\"release/\",\"HotfixBranchPrefix\":\"hotfix/\",\"Active\":true,\"CreatedUtc\":\"2026-10-01T10:00:00Z\",\"LastUpdateUtc\":\"2026-10-01T10:00:00Z\"}";
        }

        /// <summary>
        /// A 204 response.
        /// </summary>
        /// <returns>Response.</returns>
        public static System.Net.Http.HttpResponseMessage NoContent()
        {
            return StubHttpHandler.Response(HttpStatusCode.NoContent, "");
        }

        #endregion
    }
}
