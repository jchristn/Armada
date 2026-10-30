namespace Test.Shared.Suites.E2E
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Server;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Persona writes behave the same on REST, MCP, and WebSocket (one shared write service): required fields, routing
    /// validation, default-captain presence semantics, the built-in delete refusal, and ownership. Also covers the MCP
    /// captain tools accepting a managed model endpoint, a regular user writing personas over MCP like over REST,
    /// and captain create/update model-binding validation on the REST and MCP owners.
    /// </summary>
    public sealed class PersonaSurfaceParitySuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string Suite = "E2E.PersonaSurfaceParity";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the suite descriptor.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            foreach (string surface in new[] { "rest", "mcp", "ws" })
            {
                string s = surface;
                cases.Add(new TestCaseDescriptor(Suite, s + "_persona_write_rules",
                    s.ToUpperInvariant() + ": persona create, update, and delete apply the shared rules",
                    async ct => await PersonaRulesAsync(s).ConfigureAwait(false)));
            }

            cases.Add(new TestCaseDescriptor(Suite, "rest_ignores_client_built_in", "REST create ignores a client-sent IsBuiltIn, so the persona stays deletable", async ct =>
            {
                using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                await fx.StartAsync().ConfigureAwait(false);
                using HttpClient rest = fx.CreateRestClient(true);
                using HttpResponseMessage created = await rest.PostAsync("/api/v1/personas", JsonHelper.ToJsonContent(new { Name = "spoof-builtin", PromptTemplateName = "persona.worker", IsBuiltIn = true })).ConfigureAwait(false);
                AssertEqual(HttpStatusCode.Created, created.StatusCode);
                AssertFalse((await JsonHelper.DeserializeAsync<Persona>(created).ConfigureAwait(false)).IsBuiltIn, "IsBuiltIn is ignored on create");
                using HttpResponseMessage deleted = await rest.DeleteAsync("/api/v1/personas/spoof-builtin").ConfigureAwait(false);
                AssertEqual(HttpStatusCode.NoContent, deleted.StatusCode);
            }));

            cases.Add(new TestCaseDescriptor(Suite, "mcp_regular_user_writes_own_persona", "A regular user creates a UserSpecific persona over MCP, as over REST, and cannot edit a tenant-wide one", async ct =>
            {
                using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                await fx.StartAsync().ConfigureAwait(false);
                using HttpClient admin = fx.CreateRestClient(true);
                string bearer = await CreateRegularUserAsync(admin).ConfigureAwait(false);
                using HttpClient mcp = fx.CreateMcpClient(false);
                mcp.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);

                string created = await McpTestCalls.CallToolAsync(mcp, "create_persona", new { name = "mine", promptTemplateName = "persona.worker", scope = "TenantWide" }).ConfigureAwait(false);
                AssertContains("UserSpecific", created, "a regular user's persona is user-specific even when TenantWide is requested");
                string edit = await McpTestCalls.CallToolAsync(mcp, "update_persona", new { name = "mine", description = "edited" }).ConfigureAwait(false);
                AssertContains("edited", edit);
                string worker = await McpTestCalls.CallToolAsync(mcp, "update_persona", new { name = "Worker", description = "hijack" }).ConfigureAwait(false);
                AssertContains("Forbidden", worker, "a tenant-wide built-in persona needs a tenant admin");
            }));

            cases.Add(new TestCaseDescriptor(Suite, "mcp_captain_model_endpoint", "MCP create_captain and update_captain set and clear a managed model endpoint", async ct =>
            {
                using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                await fx.StartAsync().ConfigureAwait(false);
                LoggingModule quiet = new LoggingModule(); quiet.Settings.EnableConsole = false;
                using DatabaseDriver db = DatabaseDriverFactory.Create(fx.Settings.Database, quiet);
                await db.InitializeAsync().ConfigureAwait(false);
                ModelEndpoint endpoint = await db.ModelEndpoints.CreateAsync(new ModelEndpoint
                {
                    Name = "parity-endpoint", Kind = ModelEndpointKindEnum.Inference, Provider = ModelProviderEnum.OpenAICompatible,
                    BaseUrl = "http://127.0.0.1:9", Model = "parity-model", Enabled = true
                }).ConfigureAwait(false);

                using HttpClient mcp = fx.CreateMcpClient(true);
                string created = await McpTestCalls.CallToolAsync(mcp, "create_captain", new { name = "api-captain", runtime = "ApiEndpoint", modelEndpointId = endpoint.Id }).ConfigureAwait(false);
                AssertContains(endpoint.Id, created, "the API captain is bound to the endpoint");
                Captain captain = (await db.Captains.EnumerateAsync().ConfigureAwait(false)).Single(c => c.Name == "api-captain");
                AssertEqual(endpoint.Id, captain.ModelEndpointId);

                string missing = await McpTestCalls.CallToolAsync(mcp, "update_captain", new { captainId = captain.Id, modelEndpointId = "mep_missing" }).ConfigureAwait(false);
                AssertContains("EndpointNotFound", missing, "an unknown endpoint is refused with the REST validation");
                AssertEqual(endpoint.Id, (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!.ModelEndpointId, "a refused update changes nothing");
            }));

            cases.Add(new TestCaseDescriptor(Suite, "rest_name_persists_when_stored_endpoint_disabled",
                "REST name-only update persists after the stored inference endpoint is disabled", async ct =>
                await RestNamePersistsWhenEndpointDisabledAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "mcp_name_persists_when_stored_endpoint_disabled",
                "MCP name-only update persists after the stored inference endpoint is disabled", async ct =>
                await McpNamePersistsWhenEndpointDisabledAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "rest_name_persists_when_cli_model_exits_nonzero",
                "REST name-only update persists when the stored CLI model process exits non-zero", async ct =>
                await RestNamePersistsWhenCliExitsNonZeroAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "mcp_name_persists_when_cli_model_exits_nonzero",
                "MCP name-only update persists when the stored CLI model process exits non-zero", async ct =>
                await McpNamePersistsWhenCliExitsNonZeroAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "rest_model_change_rejected_when_endpoint_disabled",
                "REST model change is rejected and stored unchanged when the endpoint is disabled", async ct =>
                await RestModelChangeRejectedAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "mcp_model_change_rejected_when_endpoint_disabled",
                "MCP model change is rejected and stored unchanged when the endpoint is disabled", async ct =>
                await McpModelChangeRejectedAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "rest_endpoint_change_rejected_when_target_disabled",
                "REST endpoint change to a disabled endpoint is rejected and the stored endpoint stays", async ct =>
                await RestEndpointChangeRejectedAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "mcp_endpoint_change_rejected_when_target_disabled",
                "MCP endpoint change to a disabled endpoint is rejected and the stored endpoint stays", async ct =>
                await McpEndpointChangeRejectedAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "rest_runtime_change_rejected_without_mux_endpoint",
                "REST runtime change to Mux without a named endpoint is rejected and the stored runtime stays", async ct =>
                await RestRuntimeChangeRejectedAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "mcp_runtime_change_rejected_without_mux_endpoint",
                "MCP runtime change to Mux without a named endpoint is rejected and the stored runtime stays", async ct =>
                await McpRuntimeChangeRejectedAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "rest_create_rejects_disabled_endpoint",
                "REST create rejects an API-endpoint captain bound to a disabled endpoint and stores nothing", async ct =>
                await RestCreateRejectsDisabledEndpointAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "mcp_create_rejects_disabled_endpoint",
                "MCP create rejects an API-endpoint captain bound to a disabled endpoint and stores nothing", async ct =>
                await McpCreateRejectsDisabledEndpointAsync().ConfigureAwait(false)));
            cases.Add(new TestCaseDescriptor(Suite, "mcp_captain_pi_runtime", "MCP create_captain creates a Pi captain and update_captain keeps it", async ct =>
            {
                using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
                await fx.StartAsync().ConfigureAwait(false);
                LoggingModule quiet = new LoggingModule(); quiet.Settings.EnableConsole = false;
                using DatabaseDriver db = DatabaseDriverFactory.Create(fx.Settings.Database, quiet);
                await db.InitializeAsync().ConfigureAwait(false);

                using HttpClient mcp = fx.CreateMcpClient(true);
                await McpTestCalls.CallToolAsync(mcp, "create_captain", new { name = "pi-captain", runtime = "Pi", reasoningEffort = "High" }).ConfigureAwait(false);
                Captain captain = (await db.Captains.EnumerateAsync().ConfigureAwait(false)).Single(c => c.Name == "pi-captain");
                AssertEqual(AgentRuntimeEnum.Pi, captain.Runtime);
                AssertEqual(ReasoningEffortEnum.High, captain.ReasoningEffort);

                await McpTestCalls.CallToolAsync(mcp, "update_captain", new { captainId = captain.Id, name = "pi-captain-2", runtime = "pi" }).ConfigureAwait(false);
                Captain updated = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
                AssertEqual("pi-captain-2", updated.Name);
                AssertEqual(AgentRuntimeEnum.Pi, updated.Runtime);
            }));

            return new TestSuiteDescriptor(Suite, "Persona writes match across surfaces", cases);
        }

        #endregion

        #region Private-Methods

        private static async Task PersonaRulesAsync(string surface)
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            LoggingModule quiet = new LoggingModule(); quiet.Settings.EnableConsole = false;
            using DatabaseDriver db = DatabaseDriverFactory.Create(fx.Settings.Database, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            Captain captain = await db.Captains.CreateAsync(new Captain("parity-default")).ConfigureAwait(false);
            PersonaSurface api = new PersonaSurface(fx, surface);

            // Required fields.
            AssertEqual("invalid", await api.CreateAsync(new { Name = "no-template", PromptTemplateName = "" }).ConfigureAwait(false), "PromptTemplateName is required");

            // Create with routing fields and a default captain.
            AssertEqual("ok", await api.CreateAsync(new
            {
                Name = "parity", PromptTemplateName = "persona.worker", DefaultCaptainId = captain.Id,
                CaptainPriorities = new[] { new { Runtime = "ClaudeCode" } }
            }).ConfigureAwait(false));
            Persona stored = (await db.Personas.ReadByNameAsync("parity").ConfigureAwait(false))!;
            AssertEqual(captain.Id, stored.DefaultCaptainId);
            AssertEqual(1, stored.CaptainPriorities.Count);

            // Routing fields update (WebSocket used to drop them), an omitted default captain is kept.
            AssertEqual("ok", await api.UpdateAsync("parity", new { CaptainPriorities = new[] { new { Runtime = "Codex" }, new { Runtime = "ClaudeCode" } }, PriorityExhaustion = "UseEligibleCaptain" }).ConfigureAwait(false));
            stored = (await db.Personas.ReadByNameAsync("parity").ConfigureAwait(false))!;
            AssertEqual(2, stored.CaptainPriorities.Count, "priorities applied");
            AssertEqual(AgentRuntimeEnum.Codex, stored.CaptainPriorities[0].Runtime);
            AssertEqual(PersonaPriorityExhaustionEnum.UseEligibleCaptain, stored.PriorityExhaustion);
            AssertEqual(captain.Id, stored.DefaultCaptainId, "an omitted default captain is kept");

            // Invalid routing and an unknown default captain are refused the same way.
            AssertEqual("invalid", await api.UpdateAsync("parity", new { CaptainPriorities = new[] { new { } } }).ConfigureAwait(false), "an empty priority entry is invalid");
            AssertEqual("invalid", await api.UpdateAsync("parity", new { DefaultCaptainId = "cpt_missing" }).ConfigureAwait(false), "an unknown default captain is invalid");

            // An empty string clears the default captain on every surface; REST and WebSocket also take an explicit null
            // (MCP arguments are schema-checked as strings).
            AssertEqual("ok", await api.UpdateAsync("parity", new { DefaultCaptainId = "" }).ConfigureAwait(false));
            AssertNull((await db.Personas.ReadByNameAsync("parity").ConfigureAwait(false))!.DefaultCaptainId, "an empty string clears the default captain");
            if (surface != "mcp")
            {
                AssertEqual("ok", await api.UpdateAsync("parity", new { DefaultCaptainId = captain.Id }).ConfigureAwait(false));
                AssertEqual("ok", await api.UpdateAsync("parity", new Dictionary<string, object?> { ["DefaultCaptainId"] = null }).ConfigureAwait(false));
                AssertNull((await db.Personas.ReadByNameAsync("parity").ConfigureAwait(false))!.DefaultCaptainId, "an explicit null clears the default captain");
            }

            // Built-in personas cannot be deleted; a custom one can.
            AssertEqual("invalid", await api.DeleteAsync("Worker").ConfigureAwait(false), "built-in delete is refused as invalid on every surface");
            AssertEqual("notfound", await api.DeleteAsync("does-not-exist").ConfigureAwait(false));
            AssertEqual("ok", await api.DeleteAsync("parity").ConfigureAwait(false));
        }

        private static async Task<string> CreateRegularUserAsync(HttpClient admin)
        {
            using HttpResponseMessage userResponse = await admin.PostAsync("/api/v1/users", JsonHelper.ToJsonContent(new
            {
                TenantId = Armada.Core.Constants.DefaultTenantId, Email = "persona-parity@example.test", PasswordSha256 = UserMaster.ComputePasswordHash("fixture-only")
            })).ConfigureAwait(false);
            UserMaster user = await JsonHelper.DeserializeAsync<UserMaster>(userResponse).ConfigureAwait(false);
            using HttpResponseMessage credentialResponse = await admin.PostAsync("/api/v1/credentials", JsonHelper.ToJsonContent(new { TenantId = Armada.Core.Constants.DefaultTenantId, UserId = user.Id, Name = "fixture" })).ConfigureAwait(false);
            Credential credential = await JsonHelper.DeserializeAsync<Credential>(credentialResponse).ConfigureAwait(false);
            return credential.BearerToken;
        }

        private static async Task RestNamePersistsWhenEndpointDisabledAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            ModelEndpoint endpoint = await CreateInferenceEndpointAsync(db, "rest-name-endpoint", true).ConfigureAwait(false);
            using HttpClient rest = fx.CreateRestClient(true);
            Captain captain = await CreateApiCaptainRestAsync(rest, "rest-name", endpoint.Id).ConfigureAwait(false);
            await DisableEndpointAsync(db, endpoint).ConfigureAwait(false);
            string renamed = captain.Name + "-renamed";
            using HttpResponseMessage response = await rest.PutAsync("/api/v1/captains/" + captain.Id, JsonHelper.ToJsonContent(new { Name = renamed })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode, body);
            await AssertStoredBindingAsync(db, captain.Id, renamed, "binding-model", AgentRuntimeEnum.ApiEndpoint, endpoint.Id, body).ConfigureAwait(false);
        }

        private static async Task McpNamePersistsWhenEndpointDisabledAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            ModelEndpoint endpoint = await CreateInferenceEndpointAsync(db, "mcp-name-endpoint", true).ConfigureAwait(false);
            using HttpClient mcp = fx.CreateMcpClient(true);
            Captain captain = await CreateApiCaptainMcpAsync(mcp, db, "mcp-name", endpoint.Id).ConfigureAwait(false);
            await DisableEndpointAsync(db, endpoint).ConfigureAwait(false);
            string renamed = captain.Name + "-renamed";
            string body = await McpTestCalls.CallToolAsync(mcp, "update_captain", new { captainId = captain.Id, name = renamed }).ConfigureAwait(false);
            await AssertStoredBindingAsync(db, captain.Id, renamed, "binding-model", AgentRuntimeEnum.ApiEndpoint, endpoint.Id, body).ConfigureAwait(false);
        }

        private static async Task RestNamePersistsWhenCliExitsNonZeroAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            ImmediateCli cli = new ImmediateCli();
            cli.Install(fx.Server);
            using HttpClient rest = fx.CreateRestClient(true);
            Captain captain = await CreateCliCaptainRestAsync(rest, "rest-cli-name").ConfigureAwait(false);
            cli.Fail = true;
            string renamed = captain.Name + "-renamed";
            using HttpResponseMessage response = await rest.PutAsync("/api/v1/captains/" + captain.Id, JsonHelper.ToJsonContent(new { Name = renamed })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.OK, response.StatusCode, body);
            Captain stored = await ReadCaptainAsync(fx, captain.Id).ConfigureAwait(false);
            AssertEqual(renamed, stored.Name, body);
            AssertEqual("binding-model", stored.Model, body);
            AssertEqual(AgentRuntimeEnum.ClaudeCode, stored.Runtime, body);
        }

        private static async Task McpNamePersistsWhenCliExitsNonZeroAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            ImmediateCli cli = new ImmediateCli();
            cli.Install(fx.Server);
            using HttpClient mcp = fx.CreateMcpClient(true);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            Captain captain = await CreateCliCaptainMcpAsync(mcp, db, "mcp-cli-name").ConfigureAwait(false);
            cli.Fail = true;
            string renamed = captain.Name + "-renamed";
            string body = await McpTestCalls.CallToolAsync(mcp, "update_captain", new { captainId = captain.Id, name = renamed }).ConfigureAwait(false);
            Captain stored = (await db.Captains.ReadAsync(captain.Id).ConfigureAwait(false))!;
            AssertEqual(renamed, stored.Name, body);
            AssertEqual("binding-model", stored.Model, body);
            AssertEqual(AgentRuntimeEnum.ClaudeCode, stored.Runtime, body);
        }

        private static async Task RestModelChangeRejectedAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            ModelEndpoint endpoint = await CreateInferenceEndpointAsync(db, "rest-model-endpoint", true).ConfigureAwait(false);
            using HttpClient rest = fx.CreateRestClient(true);
            Captain captain = await CreateApiCaptainRestAsync(rest, "rest-model", endpoint.Id).ConfigureAwait(false);
            await DisableEndpointAsync(db, endpoint).ConfigureAwait(false);
            using HttpResponseMessage response = await rest.PutAsync("/api/v1/captains/" + captain.Id, JsonHelper.ToJsonContent(new { Model = "other-model" })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
            AssertContains("disabled", body, body);
            await AssertStoredBindingAsync(db, captain.Id, captain.Name, "binding-model", AgentRuntimeEnum.ApiEndpoint, endpoint.Id, body).ConfigureAwait(false);
        }

        private static async Task McpModelChangeRejectedAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            ModelEndpoint endpoint = await CreateInferenceEndpointAsync(db, "mcp-model-endpoint", true).ConfigureAwait(false);
            using HttpClient mcp = fx.CreateMcpClient(true);
            Captain captain = await CreateApiCaptainMcpAsync(mcp, db, "mcp-model", endpoint.Id).ConfigureAwait(false);
            await DisableEndpointAsync(db, endpoint).ConfigureAwait(false);
            string body = await McpTestCalls.CallToolAsync(mcp, "update_captain", new { captainId = captain.Id, model = "other-model" }).ConfigureAwait(false);
            AssertContains("EndpointDisabled", body, body);
            await AssertStoredBindingAsync(db, captain.Id, captain.Name, "binding-model", AgentRuntimeEnum.ApiEndpoint, endpoint.Id, body).ConfigureAwait(false);
        }

        private static async Task RestEndpointChangeRejectedAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            ModelEndpoint current = await CreateInferenceEndpointAsync(db, "rest-current-endpoint", true).ConfigureAwait(false);
            ModelEndpoint target = await CreateInferenceEndpointAsync(db, "rest-disabled-target", false).ConfigureAwait(false);
            using HttpClient rest = fx.CreateRestClient(true);
            Captain captain = await CreateApiCaptainRestAsync(rest, "rest-endpoint", current.Id).ConfigureAwait(false);
            using HttpResponseMessage response = await rest.PutAsync("/api/v1/captains/" + captain.Id, JsonHelper.ToJsonContent(new { ModelEndpointId = target.Id })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
            AssertContains("disabled", body, body);
            await AssertStoredBindingAsync(db, captain.Id, captain.Name, "binding-model", AgentRuntimeEnum.ApiEndpoint, current.Id, body).ConfigureAwait(false);
        }

        private static async Task McpEndpointChangeRejectedAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            ModelEndpoint current = await CreateInferenceEndpointAsync(db, "mcp-current-endpoint", true).ConfigureAwait(false);
            ModelEndpoint target = await CreateInferenceEndpointAsync(db, "mcp-disabled-target", false).ConfigureAwait(false);
            using HttpClient mcp = fx.CreateMcpClient(true);
            Captain captain = await CreateApiCaptainMcpAsync(mcp, db, "mcp-endpoint", current.Id).ConfigureAwait(false);
            string body = await McpTestCalls.CallToolAsync(mcp, "update_captain", new { captainId = captain.Id, modelEndpointId = target.Id }).ConfigureAwait(false);
            AssertContains("EndpointDisabled", body, body);
            await AssertStoredBindingAsync(db, captain.Id, captain.Name, "binding-model", AgentRuntimeEnum.ApiEndpoint, current.Id, body).ConfigureAwait(false);
        }

        private static async Task RestRuntimeChangeRejectedAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            ModelEndpoint endpoint = await CreateInferenceEndpointAsync(db, "rest-runtime-endpoint", true).ConfigureAwait(false);
            using HttpClient rest = fx.CreateRestClient(true);
            Captain captain = await CreateApiCaptainRestAsync(rest, "rest-runtime", endpoint.Id).ConfigureAwait(false);
            using HttpResponseMessage response = await rest.PutAsync("/api/v1/captains/" + captain.Id, JsonHelper.ToJsonContent(new { Runtime = "Mux" })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
            AssertContains("named endpoint", body, body);
            await AssertStoredBindingAsync(db, captain.Id, captain.Name, "binding-model", AgentRuntimeEnum.ApiEndpoint, endpoint.Id, body).ConfigureAwait(false);
        }

        private static async Task McpRuntimeChangeRejectedAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            ModelEndpoint endpoint = await CreateInferenceEndpointAsync(db, "mcp-runtime-endpoint", true).ConfigureAwait(false);
            using HttpClient mcp = fx.CreateMcpClient(true);
            Captain captain = await CreateApiCaptainMcpAsync(mcp, db, "mcp-runtime", endpoint.Id).ConfigureAwait(false);
            string body = await McpTestCalls.CallToolAsync(mcp, "update_captain", new { captainId = captain.Id, runtime = "Mux" }).ConfigureAwait(false);
            AssertContains("NamedEndpointRequired", body, body);
            await AssertStoredBindingAsync(db, captain.Id, captain.Name, "binding-model", AgentRuntimeEnum.ApiEndpoint, endpoint.Id, body).ConfigureAwait(false);
        }

        private static async Task RestCreateRejectsDisabledEndpointAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            ModelEndpoint endpoint = await CreateInferenceEndpointAsync(db, "rest-create-endpoint", false).ConfigureAwait(false);
            using HttpClient rest = fx.CreateRestClient(true);
            string name = "rest-create-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using HttpResponseMessage response = await rest.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new
            {
                Name = name, Runtime = "ApiEndpoint", Model = "binding-model", ModelEndpointId = endpoint.Id
            })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, body);
            AssertContains("disabled", body, body);
            AssertFalse((await db.Captains.EnumerateAsync().ConfigureAwait(false)).Any(c => c.Name == name), body);
        }

        private static async Task McpCreateRejectsDisabledEndpointAsync()
        {
            using SecurityTestServer fx = await SecurityTestServer.PrepareAsync("127.0.0.1", null).ConfigureAwait(false);
            await fx.StartAsync().ConfigureAwait(false);
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            ModelEndpoint endpoint = await CreateInferenceEndpointAsync(db, "mcp-create-endpoint", false).ConfigureAwait(false);
            using HttpClient mcp = fx.CreateMcpClient(true);
            string name = "mcp-create-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string body = await McpTestCalls.CallToolAsync(mcp, "create_captain", new
            {
                name = name, runtime = "ApiEndpoint", model = "binding-model", modelEndpointId = endpoint.Id
            }).ConfigureAwait(false);
            AssertContains("EndpointDisabled", body, body);
            AssertFalse((await db.Captains.EnumerateAsync().ConfigureAwait(false)).Any(c => c.Name == name), body);
        }

        private static async Task<DatabaseDriver> OpenDatabaseAsync(SecurityTestServer fx)
        {
            LoggingModule quiet = new LoggingModule();
            quiet.Settings.EnableConsole = false;
            DatabaseDriver db = DatabaseDriverFactory.Create(fx.Settings.Database, quiet);
            await db.InitializeAsync().ConfigureAwait(false);
            return db;
        }

        private static async Task<ModelEndpoint> CreateInferenceEndpointAsync(DatabaseDriver db, string name, bool enabled)
        {
            ModelEndpoint endpoint = await db.ModelEndpoints.CreateAsync(new ModelEndpoint
            {
                Name = name,
                Kind = ModelEndpointKindEnum.Inference,
                Provider = ModelProviderEnum.OpenAICompatible,
                BaseUrl = "http://127.0.0.1:9",
                Model = "binding-model",
                Enabled = enabled
            }).ConfigureAwait(false);
            ModelEndpoint stored = (await db.ModelEndpoints.ReadAsync(endpoint.Id).ConfigureAwait(false))!;
            AssertEqual(enabled, stored.Enabled, name);
            return stored;
        }

        private static async Task DisableEndpointAsync(DatabaseDriver db, ModelEndpoint endpoint)
        {
            endpoint.Enabled = false;
            await db.ModelEndpoints.UpdateAsync(endpoint).ConfigureAwait(false);
            ModelEndpoint stored = (await db.ModelEndpoints.ReadAsync(endpoint.Id).ConfigureAwait(false))!;
            AssertFalse(stored.Enabled, "the stored endpoint is disabled before the captain update");
        }

        private static async Task<Captain> CreateApiCaptainRestAsync(HttpClient rest, string namePrefix, string endpointId)
        {
            string name = namePrefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using HttpResponseMessage response = await rest.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new
            {
                Name = name, Runtime = "ApiEndpoint", Model = "binding-model", ModelEndpointId = endpointId
            })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode, body);
            Captain captain = JsonHelper.Deserialize<Captain>(body);
            AssertEqual(endpointId, captain.ModelEndpointId, body);
            AssertEqual("binding-model", captain.Model, body);
            return captain;
        }

        private static async Task<Captain> CreateApiCaptainMcpAsync(HttpClient mcp, DatabaseDriver db, string namePrefix, string endpointId)
        {
            string name = namePrefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string body = await McpTestCalls.CallToolAsync(mcp, "create_captain", new
            {
                name = name, runtime = "ApiEndpoint", model = "binding-model", modelEndpointId = endpointId
            }).ConfigureAwait(false);
            AssertContains(endpointId, body, body);
            Captain captain = (await db.Captains.EnumerateAsync().ConfigureAwait(false)).Single(c => c.Name == name);
            AssertEqual("binding-model", captain.Model, body);
            AssertEqual(AgentRuntimeEnum.ApiEndpoint, captain.Runtime, body);
            return captain;
        }

        private static async Task<Captain> CreateCliCaptainRestAsync(HttpClient rest, string namePrefix)
        {
            string name = namePrefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            using HttpResponseMessage response = await rest.PostAsync("/api/v1/captains", JsonHelper.ToJsonContent(new
            {
                Name = name, Runtime = "ClaudeCode", Model = "binding-model"
            })).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(HttpStatusCode.Created, response.StatusCode, body);
            Captain captain = JsonHelper.Deserialize<Captain>(body);
            AssertEqual("binding-model", captain.Model, body);
            AssertEqual(AgentRuntimeEnum.ClaudeCode, captain.Runtime, body);
            return captain;
        }

        private static async Task<Captain> CreateCliCaptainMcpAsync(HttpClient mcp, DatabaseDriver db, string namePrefix)
        {
            string name = namePrefix + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string body = await McpTestCalls.CallToolAsync(mcp, "create_captain", new
            {
                name = name, runtime = "ClaudeCode", model = "binding-model"
            }).ConfigureAwait(false);
            Captain captain = (await db.Captains.EnumerateAsync().ConfigureAwait(false)).Single(c => c.Name == name);
            AssertEqual("binding-model", captain.Model, body);
            AssertEqual(AgentRuntimeEnum.ClaudeCode, captain.Runtime, body);
            return captain;
        }

        private static async Task<Captain> ReadCaptainAsync(SecurityTestServer fx, string captainId)
        {
            using DatabaseDriver db = await OpenDatabaseAsync(fx).ConfigureAwait(false);
            return (await db.Captains.ReadAsync(captainId).ConfigureAwait(false))!;
        }

        private static async Task AssertStoredBindingAsync(DatabaseDriver db, string captainId, string name, string model, AgentRuntimeEnum runtime, string endpointId, string detail)
        {
            Captain stored = (await db.Captains.ReadAsync(captainId).ConfigureAwait(false))!;
            AssertEqual(name, stored.Name, detail);
            AssertEqual(model, stored.Model, detail);
            AssertEqual(runtime, stored.Runtime, detail);
            AssertEqual(endpointId, stored.ModelEndpointId, detail);
        }

        #endregion

        #region Private-Types

        /// <summary>
        /// Replaces the Claude Code runtime with a process that exits at once. <see cref="Fail"/> selects exit 1.
        /// The factory reads <see cref="Fail"/> when validation creates the runtime.
        /// </summary>
        private sealed class ImmediateCli
        {
            /// <summary>
            /// When true, the stand-in process exits 1. When false, it exits 0.
            /// </summary>
            public bool Fail { get; set; }

            /// <summary>
            /// Install the stand-in on the server. Later calls replace the Claude Code factory again.
            /// </summary>
            /// <param name="server">Started in-process server.</param>
            public void Install(ArmadaServer server)
            {
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                server.RuntimeFactory.Override(AgentRuntimeEnum.ClaudeCode, () =>
                {
                    TestAgentRuntime runtime = new TestAgentRuntime(logging);
                    if (OperatingSystem.IsWindows())
                    {
                        runtime.CommandOverride = "cmd.exe";
                        runtime.ArgsOverride = new List<string> { "/c", Fail ? "exit /b 1" : "exit /b 0" };
                    }
                    else
                    {
                        runtime.CommandOverride = "/bin/sh";
                        runtime.ArgsOverride = new List<string> { "-c", Fail ? "exit 1" : "exit 0" };
                    }

                    return runtime;
                });
            }
        }

        #endregion
    }
}
