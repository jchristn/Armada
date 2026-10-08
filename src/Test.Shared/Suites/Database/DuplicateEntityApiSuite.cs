namespace Test.Shared.Suites.Database
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using ApiResultEnum = WatsonWebserver.Core.ApiResultEnum;
    using static Test.Shared.Infrastructure.Asserts;
    using static Test.Shared.Infrastructure.DuplicateEntityAsserts;

    /// <summary>
    /// Duplicate values through the REST and MCP APIs of an in-process Admiral on the configured provider (run on all
    /// four through scripts/common/run-db-parity-tests.sh): a taken name, email, or file name on create or update is a
    /// 409 Conflict with Data.Code DuplicateEntity naming the entity and field (MCP: ErrorCode Conflict, Code
    /// DuplicateEntity); a unique-constraint violation that bypasses the checks (a reused id, concurrent creates) is the
    /// same typed conflict; and no response carries provider text. Before this a duplicate captain name surfaced as a
    /// 500 "SQLite Error 19: UNIQUE constraint failed: captains.name" on SQLite and was silently accepted elsewhere.
    /// </summary>
    public sealed class DuplicateEntityApiSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Database.DuplicateEntityApi";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("rest_duplicates_are_typed_409", "REST create and update with a taken name, email, or file name answer 409 DuplicateEntity naming the field", TestTags.Negative, async ct =>
            {
                string dataDir = TestTemp.NewDirectory("dup_rest");
                using (IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("duprest", dataDir, ct).ConfigureAwait(false))
                using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, isolated.Settings).ConfigureAwait(false))
                {
                    HttpClient client = server.Client;
                    string s = Suffix();

                    // Fleets
                    Fleet fleetA = await CreateAsync<Fleet>(client, "/api/v1/fleets", new { Name = "fleet-a-" + s }).ConfigureAwait(false);
                    Fleet fleetB = await CreateAsync<Fleet>(client, "/api/v1/fleets", new { Name = "fleet-b-" + s }).ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Post, "/api/v1/fleets", new { Name = fleetA.Name }, "Fleet", "Name", "A fleet named '" + fleetA.Name + "' already exists.").ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Put, "/api/v1/fleets/" + fleetB.Id, new { Name = fleetA.Name }, "Fleet", "Name", "A fleet named '" + fleetA.Name + "' already exists.").ConfigureAwait(false);
                    await ExpectStatusAsync(client, HttpMethod.Put, "/api/v1/fleets/" + fleetA.Id, new { Name = fleetA.Name, Description = "same name kept" }, HttpStatusCode.OK).ConfigureAwait(false);

                    // Vessels
                    Vessel vesselA = await CreateAsync<Vessel>(client, "/api/v1/vessels", new { Name = "vessel-a-" + s, RepoUrl = "https://example.com/a-" + s + ".git" }).ConfigureAwait(false);
                    Vessel vesselB = await CreateAsync<Vessel>(client, "/api/v1/vessels", new { Name = "vessel-b-" + s, RepoUrl = "https://example.com/b-" + s + ".git" }).ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Post, "/api/v1/vessels", new { Name = vesselA.Name, RepoUrl = "https://example.com/c-" + s + ".git" }, "Vessel", "Name", "A vessel named '" + vesselA.Name + "' already exists.").ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Put, "/api/v1/vessels/" + vesselB.Id, new { Name = vesselA.Name }, "Vessel", "Name", "A vessel named '" + vesselA.Name + "' already exists.").ConfigureAwait(false);

                    // Captains (the mobile app's case)
                    Captain captainA = await CreateAsync<Captain>(client, "/api/v1/captains", new { Name = "captain-a-" + s }).ConfigureAwait(false);
                    Captain captainB = await CreateAsync<Captain>(client, "/api/v1/captains", new { Name = "captain-b-" + s }).ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Post, "/api/v1/captains", new { Name = captainA.Name }, "Captain", "Name", "A captain named '" + captainA.Name + "' already exists.").ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Put, "/api/v1/captains/" + captainB.Id, new { Name = captainA.Name }, "Captain", "Name", "A captain named '" + captainA.Name + "' already exists.").ConfigureAwait(false);

                    // Personas, pipelines, prompt templates
                    string personaName = "persona-" + s;
                    await CreateAsync<Persona>(client, "/api/v1/personas", new { Name = personaName, PromptTemplateName = "persona.worker" }).ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Post, "/api/v1/personas", new { Name = personaName, PromptTemplateName = "persona.worker" }, "Persona", "Name", "A persona named '" + personaName + "' already exists.").ConfigureAwait(false);

                    string pipelineName = "pipeline-" + s;
                    object pipelineBody = new { Name = pipelineName, Stages = new[] { new { Order = 1, PersonaName = "Worker" } } };
                    await CreateAsync<Pipeline>(client, "/api/v1/pipelines", pipelineBody).ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Post, "/api/v1/pipelines", pipelineBody, "Pipeline", "Name", "A pipeline named '" + pipelineName + "' already exists.").ConfigureAwait(false);

                    string templateName = "dup.template." + s;
                    object templateBody = new { Name = templateName, Category = "mission", Content = "content" };
                    await CreateAsync<PromptTemplate>(client, "/api/v1/prompt-templates", templateBody).ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Post, "/api/v1/prompt-templates", templateBody, "PromptTemplate", "Name", "A prompt template named '" + templateName + "' already exists.").ConfigureAwait(false);

                    // Playbooks (file name)
                    Playbook playbookA = await CreateAsync<Playbook>(client, "/api/v1/playbooks", new { FileName = "a-" + s + ".md", Content = "# a" }).ConfigureAwait(false);
                    Playbook playbookB = await CreateAsync<Playbook>(client, "/api/v1/playbooks", new { FileName = "b-" + s + ".md", Content = "# b" }).ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Post, "/api/v1/playbooks", new { FileName = playbookA.FileName, Content = "# again" }, "Playbook", "FileName", "A playbook with file name '" + playbookA.FileName + "' already exists.").ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Put, "/api/v1/playbooks/" + playbookB.Id, new { FileName = playbookA.FileName, Content = "# b", Active = true }, "Playbook", "FileName", "A playbook with file name '" + playbookA.FileName + "' already exists.").ConfigureAwait(false);

                    // Users (email within the tenant)
                    string emailA = "a-" + s + "@example.com";
                    UserMaster userA = await CreateAsync<UserMaster>(client, "/api/v1/users", new { Email = emailA, Password = "Passw0rd!" + s, Active = true }).ConfigureAwait(false);
                    UserMaster userB = await CreateAsync<UserMaster>(client, "/api/v1/users", new { Email = "b-" + s + "@example.com", Password = "Passw0rd!" + s, Active = true }).ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Post, "/api/v1/users", new { Email = emailA, Password = "Passw0rd!" + s, Active = true }, "User", "Email", "A user with email '" + emailA + "' already exists in this tenant.").ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Put, "/api/v1/users/" + userB.Id, new { Email = emailA, Active = true }, "User", "Email", "A user with email '" + emailA + "' already exists in this tenant.").ConfigureAwait(false);
                    AssertNotNull(userA.Id, "user created");
                }
            }));

            cases.Add(Case("rest_safety_net_is_typed_409", "A unique violation the checks cannot see (a reused id) is a 409 DuplicateEntity without provider text", TestTags.Negative, async ct =>
            {
                string dataDir = TestTemp.NewDirectory("dup_rest_net");
                using (IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("dupnet", dataDir, ct).ConfigureAwait(false))
                using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, isolated.Settings).ConfigureAwait(false))
                {
                    HttpClient client = server.Client;
                    string s = Suffix();
                    Fleet fleet = await CreateAsync<Fleet>(client, "/api/v1/fleets", new { Name = "net-fleet-" + s }).ConfigureAwait(false);
                    E2eDuplicateErrorBody fleetBody = await ExpectConflictAsync(client, HttpMethod.Post, "/api/v1/fleets", new { Id = fleet.Id, Name = "net-fleet-other-" + s }, "Fleet", null, "A fleet with the same name or ID already exists.").ConfigureAwait(false);
                    AssertNull(fleetBody.Data!.Value, "the database does not say which value");

                    Captain captain = await CreateAsync<Captain>(client, "/api/v1/captains", new { Name = "net-captain-" + s }).ConfigureAwait(false);
                    await ExpectConflictAsync(client, HttpMethod.Post, "/api/v1/captains", new { Id = captain.Id, Name = "net-captain-other-" + s }, "Captain", null, "A captain with the same name or ID already exists.").ConfigureAwait(false);
                }
            }));

            cases.Add(Case("mcp_duplicates_are_typed_conflicts", "MCP create and update tools with a taken value return ErrorCode Conflict, Code DuplicateEntity", TestTags.Negative, async ct =>
            {
                string dataDir = TestTemp.NewDirectory("dup_mcp");
                using (IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("dupmcp", dataDir, ct).ConfigureAwait(false))
                using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, isolated.Settings).ConfigureAwait(false))
                using (HttpClient mcp = CreateMcpClient(server))
                {
                    string s = Suffix();

                    string fleetName = "mcp-fleet-" + s;
                    McpToolResultProbe created = await CallToolAsync(mcp, "create_fleet", new { name = fleetName }).ConfigureAwait(false);
                    AssertNull(created.ErrorCode, "first fleet created");
                    await ExpectToolConflictAsync(mcp, "create_fleet", new { name = fleetName }, "A fleet named '" + fleetName + "' already exists.").ConfigureAwait(false);
                    Fleet other = await CreateAsync<Fleet>(server.Client, "/api/v1/fleets", new { Name = "mcp-fleet-other-" + s }).ConfigureAwait(false);
                    await ExpectToolConflictAsync(mcp, "update_fleet", new { fleetId = other.Id, name = fleetName }, "A fleet named '" + fleetName + "' already exists.").ConfigureAwait(false);

                    string captainName = "mcp-captain-" + s;
                    AssertNull((await CallToolAsync(mcp, "create_captain", new { name = captainName }).ConfigureAwait(false)).ErrorCode, "first captain created");
                    await ExpectToolConflictAsync(mcp, "create_captain", new { name = captainName }, "A captain named '" + captainName + "' already exists.").ConfigureAwait(false);

                    string personaName = "mcp-persona-" + s;
                    AssertNull((await CallToolAsync(mcp, "create_persona", new { name = personaName, promptTemplateName = "persona.worker" }).ConfigureAwait(false)).ErrorCode, "first persona created");
                    await ExpectToolConflictAsync(mcp, "create_persona", new { name = personaName, promptTemplateName = "persona.worker" }, "A persona named '" + personaName + "' already exists.").ConfigureAwait(false);

                    string pipelineName = "mcp-pipeline-" + s;
                    object pipelineArgs = new { name = pipelineName, stages = new[] { new { personaName = "Worker" } } };
                    AssertNull((await CallToolAsync(mcp, "create_pipeline", pipelineArgs).ConfigureAwait(false)).ErrorCode, "first pipeline created");
                    await ExpectToolConflictAsync(mcp, "create_pipeline", pipelineArgs, "A pipeline named '" + pipelineName + "' already exists.").ConfigureAwait(false);

                    string templateName = "mcp.template." + s;
                    object templateArgs = new { name = templateName, category = "mission", content = "content" };
                    AssertNull((await CallToolAsync(mcp, "create_prompt_template", templateArgs).ConfigureAwait(false)).ErrorCode, "first template created");
                    await ExpectToolConflictAsync(mcp, "create_prompt_template", templateArgs, "A prompt template named '" + templateName + "' already exists.").ConfigureAwait(false);

                    string fileName = "mcp-" + s + ".md";
                    AssertNull((await CallToolAsync(mcp, "create_playbook", new { fileName = fileName, content = "# x" }).ConfigureAwait(false)).ErrorCode, "first playbook created");
                    await ExpectToolConflictAsync(mcp, "create_playbook", new { fileName = fileName, content = "# x" }, "A playbook with file name '" + fileName + "' already exists.").ConfigureAwait(false);
                }
            }));

            cases.Add(Case("concurrent_creates_conflict_cleanly", "Concurrent REST and MCP creates of one name: one succeeds, the rest are typed conflicts without provider text", TestTags.Negative, async ct =>
            {
                string dataDir = TestTemp.NewDirectory("dup_race");
                using (IsolatedTestDatabase isolated = await IsolatedTestDatabase.CreateAsync("duprace", dataDir, ct).ConfigureAwait(false))
                using (InProcessArmadaServer server = await InProcessArmadaServer.StartAsync(dataDir, isolated.Settings).ConfigureAwait(false))
                using (HttpClient mcp = CreateMcpClient(server))
                {
                    const int contenders = 6;
                    string s = Suffix();

                    string restName = "race-rest-" + s;
                    List<Task<HttpResponseMessage>> posts = new List<Task<HttpResponseMessage>>();
                    for (int i = 0; i < contenders; i++)
                        posts.Add(server.Client.PostAsync("/api/v1/personas", JsonHelper.ToJsonContent(new { Name = restName, PromptTemplateName = "persona.worker" })));
                    HttpResponseMessage[] responses = await Task.WhenAll(posts).ConfigureAwait(false);
                    AssertEqual(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created), "exactly one REST create wins");
                    foreach (HttpResponseMessage response in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
                    {
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        AssertEqual(409, (int)response.StatusCode, "losing REST create status (body: " + text + ")");
                        AssertNoProviderText(text, "losing REST create body");
                        E2eDuplicateErrorBody body = JsonHelper.Deserialize<E2eDuplicateErrorBody>(text);
                        AssertEqual(DuplicateEntityException.ErrorCode, body.Data?.Code, "losing REST create code");
                        AssertEqual("Persona", body.Data?.EntityType, "losing REST create entity");
                    }

                    string mcpName = "race-mcp-" + s;
                    List<Task<McpToolResultProbe>> calls = new List<Task<McpToolResultProbe>>();
                    for (int i = 0; i < contenders; i++)
                        calls.Add(CallToolAsync(mcp, "create_persona", new { name = mcpName, promptTemplateName = "persona.worker" }));
                    McpToolResultProbe[] results = await Task.WhenAll(calls).ConfigureAwait(false);
                    AssertEqual(1, results.Count(r => r.ErrorCode == null), "exactly one MCP create wins");
                    foreach (McpToolResultProbe result in results.Where(r => r.ErrorCode != null))
                    {
                        AssertEqual(McpToolErrorCodeEnum.Conflict, result.ErrorCode, "losing MCP create category");
                        AssertEqual(DuplicateEntityException.ErrorCode, result.Code, "losing MCP create code");
                        AssertNoProviderText(result.Error, "losing MCP create message");
                    }
                }
            }));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Duplicate entities through REST and MCP",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string Suffix()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 10);
        }

        private static HttpClient CreateMcpClient(InProcessArmadaServer server)
        {
            HttpClient client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + server.Settings.McpPort), Timeout = TimeSpan.FromSeconds(60) };
            client.DefaultRequestHeaders.Add("X-Api-Key", server.Settings.ApiKey);
            return client;
        }

        private static async Task<T> CreateAsync<T>(HttpClient client, string path, object body)
        {
            HttpResponseMessage response = await client.PostAsync(path, JsonHelper.ToJsonContent(body)).ConfigureAwait(false);
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertEqual(201, (int)response.StatusCode, "POST " + path + " (body: " + text + ")");
            return JsonHelper.Deserialize<T>(text);
        }

        private static async Task ExpectStatusAsync(HttpClient client, HttpMethod method, string path, object body, HttpStatusCode expected)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                request.Content = JsonHelper.ToJsonContent(body);
                using (HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    AssertEqual(expected, response.StatusCode, method + " " + path + " (body: " + text + ")");
                }
            }
        }

        private static async Task<E2eDuplicateErrorBody> ExpectConflictAsync(HttpClient client, HttpMethod method, string path, object body, string entityType, string? field, string expectedMessage)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(method, path))
            {
                request.Content = JsonHelper.ToJsonContent(body);
                using (HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    string label = method + " " + path;
                    AssertEqual(409, (int)response.StatusCode, label + " status (body: " + text + ")");
                    AssertNoProviderText(text, label + " body");
                    E2eDuplicateErrorBody parsed = JsonHelper.Deserialize<E2eDuplicateErrorBody>(text);
                    AssertEqual(ApiResultEnum.Conflict, parsed.Error, label + " Error");
                    AssertEqual(409, parsed.StatusCode, label + " StatusCode");
                    AssertEqual(expectedMessage, parsed.Message, label + " Message");
                    AssertNotNull(parsed.Data, label + " Data");
                    AssertEqual(DuplicateEntityException.ErrorCode, parsed.Data!.Code, label + " Data.Code");
                    AssertEqual(entityType, parsed.Data.EntityType, label + " Data.EntityType");
                    AssertEqual(field, parsed.Data.Field, label + " Data.Field");
                    return parsed;
                }
            }
        }

        private static async Task ExpectToolConflictAsync(HttpClient mcp, string tool, object arguments, string expectedMessage)
        {
            McpToolResultProbe probe = await CallToolAsync(mcp, tool, arguments).ConfigureAwait(false);
            AssertEqual(McpToolErrorCodeEnum.Conflict, probe.ErrorCode, tool + " ErrorCode (error: " + probe.Error + ")");
            AssertEqual(DuplicateEntityException.ErrorCode, probe.Code, tool + " Code");
            AssertEqual(expectedMessage, probe.Error, tool + " message");
        }

        /// <summary>
        /// Call a tool over the MCP HTTP endpoint and probe its result. A JSON-RPC error or an isError result (an
        /// exception the tool did not map) fails the test with the text, which is also checked for provider wording.
        /// </summary>
        private static async Task<McpToolResultProbe> CallToolAsync(HttpClient mcp, string tool, object arguments)
        {
            HttpRequestMessage init = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            init.Content = JsonHelper.ToJsonContent(new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = "2024-11-05", capabilities = new { }, clientInfo = new { name = "duplicate-test", version = "1.0" } }
            });
            HttpResponseMessage initResponse = await mcp.SendAsync(init).ConfigureAwait(false);
            string sessionId = initResponse.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values) ? values.First() : String.Empty;

            HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, "/rpc");
            message.Content = JsonHelper.ToJsonContent(new { jsonrpc = "2.0", id = 2, method = "tools/call", @params = new { name = tool, arguments = arguments } });
            if (!String.IsNullOrEmpty(sessionId)) message.Headers.Add("Mcp-Session-Id", sessionId);
            HttpResponseMessage response = await mcp.SendAsync(message).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            AssertNoProviderText(body, "MCP " + tool + " response");
            if (!response.IsSuccessStatusCode) throw new AssertionException("MCP " + tool + " failed with HTTP " + (int)response.StatusCode + ": " + body);
            E2eMcpToolEnvelope envelope = JsonHelper.Deserialize<E2eMcpToolEnvelope>(body);
            if (envelope.Error != null) throw new AssertionException("MCP " + tool + " returned a JSON-RPC error: " + body);
            if (envelope.Result == null || envelope.Result.Content.Count == 0) throw new AssertionException("MCP " + tool + " returned no content: " + body);
            if (envelope.Result.IsError) throw new AssertionException("MCP " + tool + " returned an untyped tool error: " + body);
            return McpToolResultProbe.FromText(envelope.Result.Content[0].Text);
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<CancellationToken, Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: body,
                tags: new List<string> { tag, TestTags.EndToEnd });
        }

        #endregion
    }
}
