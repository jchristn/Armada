namespace Test.Shared.Infrastructure
{
    using System;

    /// <summary>
    /// Canned server responses for TUI tests (PascalCase JSON like the real server).
    /// </summary>
    public static class TuiFixtures
    {
        #region Public-Methods

        /// <summary>
        /// A stub with health, whoami, inbox, jobs, catalog, and login routes for a tenant admin.
        /// </summary>
        /// <param name="tenants">Tenants returned by the lookup.</param>
        /// <returns>Stub.</returns>
        public static StubHttpHandler SignedInServer(int tenants = 1)
        {
            StubHttpHandler stub = new StubHttpHandler();
            stub.Json("GET", "/api/v1/status/health", "{\"Status\":\"healthy\",\"Version\":\"0.9.0\",\"Ports\":{\"Admiral\":7890,\"Mcp\":7891}}");
            stub.On("GET", "/api/v1/whoami", body => StubHttpHandler.Response(System.Net.HttpStatusCode.OK, WhoAmI()));
            stub.Json("GET", "/api/v1/inbox", "[{\"Kind\":\"MissionReview\",\"Severity\":\"Warning\",\"Title\":\"Review: Fix tables\",\"Detail\":\"msn_1\",\"Href\":\"/missions/msn_1\"}]");
            stub.Json("GET", "/api/v1/jobs", "{\"Success\":true,\"PageNumber\":1,\"PageSize\":100,\"TotalPages\":1,\"TotalRecords\":0,\"Objects\":[]}");
            stub.Json("GET", "/proxy-api/v1/session/context", "{}", System.Net.HttpStatusCode.NotFound);
            string list = tenants == 0 ? "[]" : tenants == 1
                ? "[{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\"}]"
                : "[{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\"},{\"Id\":\"ten_two\",\"Name\":\"Second Tenant\"}]";
            stub.Json("POST", "/api/v1/tenants/lookup", "{\"Tenants\":" + list + "}");
            stub.On("POST", "/api/v1/authenticate", body => JsonHelper.Deserialize<Armada.Core.Models.AuthenticateRequest>(body).Password == "password"
                ? StubHttpHandler.Response(System.Net.HttpStatusCode.OK, "{\"Success\":true,\"Token\":\"tok_session\"}")
                : StubHttpHandler.Response(System.Net.HttpStatusCode.Unauthorized, "{\"Error\":\"Unauthorized\",\"Message\":\"Authentication failed.\"}"));
            return stub;
        }

        /// <summary>
        /// whoami JSON for admin@armada in Default Tenant.
        /// </summary>
        /// <returns>JSON.</returns>
        public static string WhoAmI()
        {
            return "{\"Tenant\":{\"Id\":\"ten_default\",\"Name\":\"Default Tenant\",\"Active\":true},\"User\":{\"Id\":\"usr_admin\",\"TenantId\":\"ten_default\",\"Email\":\"admin@armada\",\"IsAdmin\":true,\"IsTenantAdmin\":true,\"Active\":true}}";
        }

        #endregion
    }
}
