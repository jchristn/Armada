namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using PolyPrompt.Clients;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ModelEndpointService"/>: CRUD over a live SQLite store, provider/kind
    /// capability guards, API-key preservation on update, and base-URL health deduplication. Health
    /// cases hold the probe request, then save configuration or overlap a second probe. Positive
    /// cases assert creation, scoped enumeration, key preservation, and URL normalization; negative cases
    /// assert rejection of the unsupported Anthropic-embedding and Voyage-inference combinations, the
    /// missing-base-URL path, and the audited null-id / not-found paths.
    /// </summary>
    public sealed class ModelEndpointServiceSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the ModelEndpointService suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("validation_notes_admiral_local_addresses", "Validating an Admiral-local address says the check did not reach a Harbor's computer; a remote address gets no note", TestTags.Positive, () =>
            {
                foreach (string local in new[] { "http://localhost:11434", "http://127.0.0.1:8080/v1", "http://[::1]:11434", "http://ollama.localhost" })
                {
                    string? note = ModelEndpointService.LocalAddressNote(local);
                    AssertNotNull(note, local);
                    AssertContains("Harbor", note!);
                }
                AssertNull(ModelEndpointService.LocalAddressNote("https://ollama.com:443"));
                AssertNull(ModelEndpointService.LocalAddressNote("http://view.homedns.org:8900/v1.0/api/x/"));
                AssertNull(ModelEndpointService.LocalAddressNote(null));
                AssertNull(ModelEndpointService.LocalAddressNote("not a url"));
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("duplicate_inherits_secrets_and_resets_identity_health", "Edited endpoint copies credentials server-side with fresh identity and health, leaving source unchanged", TestTags.Positive, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(db.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_copy", "usr_owner", false, true, "Test");
                ModelEndpoint source = NewInference("source", "https://ollama.com:443");
                source.Provider = ModelProviderEnum.OllamaCloud; source.Model = "first-model"; source.AuthType = ModelEndpointAuthTypeEnum.Bearer; source.ApiKey = "copy-secret";
                source.Headers = new List<ModelEndpointHeader> { new ModelEndpointHeader("X-Org", "org-secret"), new ModelEndpointHeader("X-Remove", "remove-secret") };
                source.InferenceOptions = new InferenceOptions { MaxIterations = 7, ReadOnlyTools = true };
                source = await service.CreateAsync(auth, source).ConfigureAwait(false);
                source.HealthHistory.Add(new ModelEndpointHealthRecord { TimestampUtc = DateTime.UtcNow, Success = true });
                await db.Driver.ModelEndpoints.UpdateAsync(source).ConfigureAwait(false);
                ModelEndpoint edited = NewInference("copy", source.BaseUrl); edited.Provider = source.Provider; edited.AuthType = source.AuthType; edited.Model = "second-model";
                edited.Headers = new List<ModelEndpointHeader> { new ModelEndpointHeader("x-org", null), new ModelEndpointHeader("X-New", "new-value") };
                edited.Id = source.Id; edited.HealthHistory.Add(new ModelEndpointHealthRecord { TimestampUtc = DateTime.UtcNow, Success = false });
                ModelEndpoint copy = await service.DuplicateAsync(auth, source.Id, edited).ConfigureAwait(false);
                AssertTrue(copy.Id != source.Id, "Copied endpoint must have an independent ID.");
                AssertEqual("copy-secret", copy.ApiKey); AssertEqual("org-secret", copy.Headers[0].Value); AssertEqual(2, copy.Headers.Count);
                AssertEqual("second-model", copy.Model); AssertEqual(7, copy.InferenceOptions.MaxIterations);
                AssertEqual(0, copy.HealthHistory.Count); AssertEqual(EndpointHealthStatusEnum.Unknown, copy.HealthStatus);
                AssertNull(copy.LastHealthCheckUtc);
                string response = System.Text.Json.JsonSerializer.Serialize(copy);
                AssertFalse(response.Contains("copy-secret") || response.Contains("org-secret") || response.Contains("new-value"), "Copy response must never disclose credentials.");
                ModelEndpoint original = (await db.Driver.ModelEndpoints.ReadAsync(source.Id).ConfigureAwait(false))!;
                AssertEqual("source", original.Name); AssertEqual("first-model", original.Model); AssertEqual(1, original.HealthHistory.Count); AssertEqual("X-Remove", original.Headers[1].Name);
            }));

            cases.Add(CaseAsync("duplicate_requires_source_edit_permission", "Readers cannot copy shared credentials and invisible sources are not disclosed", TestTags.Negative, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(db.Driver, CreateLogging());
                AuthContext admin = AuthContext.Authenticated("ten_copy", "usr_admin", false, true, "Test");
                ModelEndpoint source = await service.CreateAsync(admin, NewInference("shared", "http://localhost:1")).ConfigureAwait(false);
                AuthContext reader = AuthContext.Authenticated("ten_copy", "usr_reader", false, false, "Test");
                bool denied = false;
                try { await service.DuplicateAsync(reader, source.Id, NewInference("copy", "http://elsewhere:1")).ConfigureAwait(false); } catch (UnauthorizedAccessException) { denied = true; }
                AssertTrue(denied, "Read access does not authorize exporting a stored credential.");
                bool hidden = false;
                try { await service.DuplicateAsync(AuthContext.Authenticated("ten_other", "usr_other", false, true, "Test"), source.Id, NewInference("copy", "http://localhost:1")).ConfigureAwait(false); } catch (KeyNotFoundException) { hidden = true; }
                AssertTrue(hidden, "An invisible source returns not found.");
                AssertEqual(1, (await service.EnumerateAsync(admin).ConfigureAwait(false)).Count);
            }));

            cases.Add(CaseAsync("duplicate_replacements_and_validation", "Copied credentials can be replaced or cleared and invalid copies create no record", TestTags.Positive, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(db.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_copy", "usr_owner", false, true, "Test");
                ModelEndpoint source = NewInference("source", "http://localhost:1"); source.ApiKey = "old-key";
                source.Headers = new List<ModelEndpointHeader> { new ModelEndpointHeader("X-Org", "old-value") };
                source = await service.CreateAsync(auth, source).ConfigureAwait(false);
                ModelEndpoint replacement = NewInference("replacement", "http://localhost:2"); replacement.ApiKeyInput = "new-key";
                replacement.Headers = new List<ModelEndpointHeader>();
                ModelEndpoint copy = await service.DuplicateAsync(auth, source.Id, replacement).ConfigureAwait(false);
                AssertEqual("new-key", copy.ApiKey); AssertEqual(0, copy.Headers.Count);
                ModelEndpoint cleared = NewInference("cleared", "http://localhost:3"); cleared.ApiKeyInput = null;
                AssertNull((await service.DuplicateAsync(auth, source.Id, cleared).ConfigureAwait(false)).ApiKey);
                bool rejected = false;
                ModelEndpoint invalid = NewInference("invalid", "http://localhost:4"); invalid.InferenceOptions = new InferenceOptions { MaxIterations = 0 };
                try { await service.DuplicateAsync(auth, source.Id, invalid).ConfigureAwait(false); } catch (ArgumentException) { rejected = true; }
                AssertTrue(rejected, "Copied endpoint is validated normally.");
                AssertEqual(3, (await service.EnumerateAsync(auth).ConfigureAwait(false)).Count);
            }));

            cases.Add(CaseAsync("inference_policy_roundtrip_preserve_and_reset", "Inference policy persists, omitted updates preserve it and explicit null resets defaults", TestTags.Positive, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(db.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_policy", "usr_policy", false, true, "Test");
                ModelEndpoint endpoint = NewInference("policy", "http://localhost:1");
                endpoint.InferenceOptions = new InferenceOptions { ToolsEnabled = false, ReadOnlyTools = true, McpToolsEnabled = false, AllowedTools = new List<string> { "read_file" }, MaxIterations = 3, MaxTokens = 321, Temperature = 0.2, TopP = 0.8, RequireHarbor = true };
                ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);
                ModelEndpoint read = (await db.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false))!;
                AssertEqual(3, read.InferenceOptions.MaxIterations);
                AssertEqual(321, read.InferenceOptions.MaxTokens);
                AssertEqual(false, read.InferenceOptions.ToolsEnabled);
                AssertEqual(true, read.InferenceOptions.RequireHarbor);
                AssertEqual("read_file", read.InferenceOptions.AllowedTools![0]);
                ModelEndpoint update = NewInference("renamed", "http://localhost:1"); update.Id = created.Id;
                ModelEndpoint preserved = await service.UpdateAsync(auth, update).ConfigureAwait(false);
                AssertEqual(3, preserved.InferenceOptions.MaxIterations);
                AssertEqual(false, preserved.InferenceOptions.McpToolsEnabled);
                update.InferenceOptions = null!;
                await service.UpdateAsync(auth, update).ConfigureAwait(false);
                ModelEndpoint reset = (await db.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false))!;
                AssertEqual(100, reset.InferenceOptions.MaxIterations);
                AssertEqual(true, reset.InferenceOptions.ToolsEnabled);
                AssertNull(reset.InferenceOptions.AllowedTools);
            }));

            cases.Add(CaseAsync("inference_policy_rejects_invalid_values", "Reject invalid inference limits and ambiguous tool names", TestTags.Negative, async () =>
            {
                using TestDatabase db = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(db.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_policy", "usr_policy", false, true, "Test");
                foreach (InferenceOptions options in new[] {
                    new InferenceOptions { MaxIterations = 0 }, new InferenceOptions { MaxIterations = 1001 }, new InferenceOptions { MaxTokens = 0 },
                    new InferenceOptions { Temperature = Double.NaN }, new InferenceOptions { Temperature = 2.1 }, new InferenceOptions { TopP = -0.1 },
                    new InferenceOptions { AllowedTools = new List<string> { "read_file", "read_file" } }, new InferenceOptions { AllowedTools = new List<string> { " read_file" } } })
                {
                    ModelEndpoint endpoint = NewInference("invalid", "http://localhost:1"); endpoint.InferenceOptions = options;
                    bool rejected = false;
                    try { await service.CreateAsync(auth, endpoint).ConfigureAwait(false); } catch (ArgumentException) { rejected = true; }
                    AssertTrue(rejected, "Invalid inference options must be rejected before persistence.");
                }
                new InferenceOptions { MaxIterations = 1000, MaxTokens = 10000000, Temperature = 0, TopP = 1, AllowedTools = new List<string>() }.Validate();
            }));

            cases.Add(CaseAsync("inference_harbor_transport_snapshot", "Harbor transport snapshots tool policy and generation settings alongside credentials", TestTags.Positive, () =>
            {
                ModelEndpoint endpoint = NewInference("transport", "http://localhost:1"); endpoint.ApiKey = "secret"; endpoint.Enabled = false;
                endpoint.InferenceOptions = new InferenceOptions { AllowedTools = new List<string> { "read_file" }, MaxTokens = 42, RequireHarbor = true };
                Armada.Core.Harbor.HarborInferenceEndpoint transport = Armada.Core.Harbor.HarborInferenceEndpoint.FromEndpoint(endpoint);
                endpoint.InferenceOptions.AllowedTools.Clear();
                ModelEndpoint restored = transport.ToEndpoint();
                AssertEqual("read_file", restored.InferenceOptions.AllowedTools![0]);
                AssertEqual(true, restored.InferenceOptions.RequireHarbor);
                AssertEqual(42, restored.InferenceOptions.MaxTokens);
                AssertEqual(false, restored.Enabled);
                AssertEqual("secret", restored.ApiKey);
                AssertTrue(!System.Text.Json.JsonSerializer.Serialize(restored).Contains("secret"), "REST endpoint must not expose provider credentials.");
                restored.InferenceOptions.AllowedTools.Clear();
                AssertEqual(1, transport.InferenceOptions.AllowedTools!.Count);
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("create_creates_embedding_endpoint", "CreateAsync creates an embedding endpoint", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep", "usr_mep", false, true, "UnitTest");

                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "OpenAI Embeddings",
                    Kind = ModelEndpointKindEnum.Embedding,
                    Provider = ModelProviderEnum.OpenAI,
                    BaseUrl = "https://api.openai.com",
                    Model = "text-embedding-3-small"
                };
                endpoint.ApiKey = "sk-secret-value";

                ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);

                AssertStartsWith("mep_", created.Id);
                AssertEqual(ModelEndpointKindEnum.Embedding, created.Kind);
                AssertEqual(ModelProviderEnum.OpenAI, created.Provider);
                AssertEqual("ten_mep", created.TenantId);
                AssertTrue(created.HasApiKey, "Expected HasApiKey to be true after creating with a key.");
                AssertEqual(EndpointHealthStatusEnum.Unknown, created.HealthStatus);
            }));

            cases.Add(CaseAsync("scope_visibility_and_create_scope", "Regular user sees tenant-wide + own; create is forced user-specific", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext admin = AuthContext.Authenticated("ten_s", "usr_admin", false, true, "Test");
                AuthContext userA = AuthContext.Authenticated("ten_s", "usr_a", false, false, "Test");
                AuthContext userB = AuthContext.Authenticated("ten_s", "usr_b", false, false, "Test");

                await service.CreateAsync(admin, NewInference("Shared", "http://localhost:1")).ConfigureAwait(false);
                ModelEndpoint aOwn = await service.CreateAsync(userA, NewInference("A-Private", "http://localhost:2")).ConfigureAwait(false);
                await service.CreateAsync(userB, NewInference("B-Private", "http://localhost:3")).ConfigureAwait(false);

                // Regular-user create is forced user-specific and survives the DB round-trip.
                ModelEndpoint? aReread = await service.ReadAsync(userA, aOwn.Id).ConfigureAwait(false);
                AssertTrue(aReread != null, "User should read their own endpoint.");
                AssertEqual(ScopeEnum.UserSpecific, aReread!.Scope);

                List<ModelEndpoint> visibleToA = await service.EnumerateAsync(userA).ConfigureAwait(false);
                AssertEqual(2, visibleToA.Count);
                AssertTrue(visibleToA.Exists(e => e.Name == "Shared"), "User A should see the tenant-wide endpoint.");
                AssertTrue(visibleToA.Exists(e => e.Name == "A-Private"), "User A should see their own endpoint.");
                AssertTrue(!visibleToA.Exists(e => e.Name == "B-Private"), "User A should NOT see user B's endpoint.");

                // Tenant admin sees all three.
                List<ModelEndpoint> visibleToAdmin = await service.EnumerateAsync(admin).ConfigureAwait(false);
                AssertEqual(3, visibleToAdmin.Count);
            }));

            cases.Add(CaseAsync("delete_blocked_when_referenced_by_captain", "DeleteAsync is blocked while a captain references the endpoint", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_del", "usr_del", false, true, "UnitTest");

                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Inference One",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.OpenAICompatible,
                    BaseUrl = "http://localhost:11434",
                    Model = "llama3"
                };
                ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);

                Armada.Core.Models.Captain captain = new Armada.Core.Models.Captain("api-captain", Armada.Core.Enums.AgentRuntimeEnum.ApiEndpoint);
                captain.ModelEndpointId = created.Id;
                Armada.Core.Models.Captain createdCaptain = await testDb.Driver.Captains.CreateAsync(captain).ConfigureAwait(false);

                // The endpoint id must survive the DB round-trip (proves the migration + row mapping).
                Armada.Core.Models.Captain? reread = await testDb.Driver.Captains.ReadAsync(createdCaptain.Id).ConfigureAwait(false);
                AssertTrue(reread != null, "Expected the captain to be readable.");
                AssertEqual(created.Id, reread!.ModelEndpointId ?? String.Empty);

                List<Armada.Core.Models.Captain> referencing = await service.GetReferencingCaptainsAsync(auth, created.Id).ConfigureAwait(false);
                AssertEqual(1, referencing.Count);

                await AssertThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(auth, created.Id));
            }));

            cases.Add(CaseAsync("enumerate_returns_tenant_scoped_endpoints", "EnumerateAsync returns tenant-scoped endpoints", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_enum", "usr_mep_enum", false, true, "UnitTest");

                await service.CreateAsync(auth, NewInference("Gpt", "https://api.openai.com")).ConfigureAwait(false);
                await service.CreateAsync(auth, NewInference("Local", "http://localhost:11434")).ConfigureAwait(false);

                List<ModelEndpoint> endpoints = await service.EnumerateAsync(auth).ConfigureAwait(false);
                AssertEqual(2, endpoints.Count);
            }));

            cases.Add(CaseAsync("update_preserves_api_key_when_not_supplied", "UpdateAsync preserves the API key when none supplied", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_key", "usr_mep_key", false, true, "UnitTest");

                ModelEndpoint endpoint = NewInference("Keyed", "https://api.openai.com");
                endpoint.ApiKey = "sk-original";
                ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);

                // Build an update object that does NOT supply an API key (ApiKeySpecified stays false).
                ModelEndpoint edit = new ModelEndpoint
                {
                    Id = created.Id,
                    Name = "Renamed",
                    Kind = created.Kind,
                    Provider = created.Provider,
                    BaseUrl = created.BaseUrl,
                    Model = created.Model
                };
                await service.UpdateAsync(auth, edit).ConfigureAwait(false);

                ModelEndpoint? reloaded = await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected endpoint to reload.");
                AssertEqual("Renamed", reloaded!.Name);
                AssertEqual("sk-original", reloaded.ApiKey);
            }));

            cases.Add(CaseAsync("update_replaces_api_key_when_supplied", "UpdateAsync replaces the API key when supplied", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_key2", "usr_mep_key2", false, true, "UnitTest");

                ModelEndpoint endpoint = NewInference("Keyed", "https://api.openai.com");
                endpoint.ApiKey = "sk-original";
                ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);

                ModelEndpoint edit = new ModelEndpoint
                {
                    Id = created.Id,
                    Name = created.Name,
                    Kind = created.Kind,
                    Provider = created.Provider,
                    BaseUrl = created.BaseUrl
                };
                edit.ApiKeyInput = "sk-rotated";
                await service.UpdateAsync(auth, edit).ConfigureAwait(false);

                ModelEndpoint? reloaded = await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected endpoint to reload.");
                AssertEqual("sk-rotated", reloaded!.ApiKey);
            }));

            cases.Add(CaseAsync("delete_removes_endpoint", "DeleteAsync removes the endpoint", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_del", "usr_mep_del", false, true, "UnitTest");

                ModelEndpoint created = await service.CreateAsync(auth, NewInference("Doomed", "https://api.openai.com")).ConfigureAwait(false);
                await service.DeleteAsync(auth, created.Id).ConfigureAwait(false);

                ModelEndpoint? reloaded = await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNull(reloaded, "Expected endpoint to be deleted.");
            }));

            cases.Add(CaseAsync("normalize_base_url_dedups_equivalent_urls", "NormalizeBaseUrl treats equivalent URLs as one", TestTags.Positive, () =>
            {
                string a = ModelEndpointService.NormalizeBaseUrl("https://Host.Example.com:443/");
                string b = ModelEndpointService.NormalizeBaseUrl("https://host.example.com");
                AssertEqual(a, b);

                string c = ModelEndpointService.NormalizeBaseUrl("http://localhost:11434");
                AssertNotEqual(a, c);
                return Task.CompletedTask;
            }));

            // Negative: Anthropic cannot serve embeddings.

            cases.Add(CaseAsync("create_rejects_anthropic_embedding", "CreateAsync rejects Anthropic embedding", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_neg", "usr_mep_neg", false, true, "UnitTest");

                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Bad",
                    Kind = ModelEndpointKindEnum.Embedding,
                    Provider = ModelProviderEnum.Anthropic,
                    BaseUrl = "https://api.anthropic.com"
                };
                await AssertThrowsAsync<ArgumentException>(() => service.CreateAsync(auth, endpoint));
            }));

            // Negative: Voyage AI cannot serve inference.

            cases.Add(CaseAsync("create_rejects_voyageai_inference", "CreateAsync rejects Voyage AI inference", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_neg2", "usr_mep_neg2", false, true, "UnitTest");

                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Bad",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.VoyageAI,
                    BaseUrl = "https://api.voyageai.com"
                };
                await AssertThrowsAsync<ArgumentException>(() => service.CreateAsync(auth, endpoint));
            }));

            // Negative: base URL is required.

            cases.Add(CaseAsync("create_rejects_missing_base_url", "CreateAsync rejects a missing base URL", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_neg3", "usr_mep_neg3", false, true, "UnitTest");

                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "No URL",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.OpenAI,
                    BaseUrl = ""
                };
                await AssertThrowsAsync<ArgumentException>(() => service.CreateAsync(auth, endpoint));
            }));

            // Negative: reading with a null id throws.

            cases.Add(CaseAsync("read_null_id_throws", "ReadAsync NullId Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_null", "usr_mep_null", false, true, "UnitTest");
                await AssertThrowsAsync<ArgumentNullException>(() => service.ReadAsync(auth, null!));
            }));

            // Negative: updating an unknown endpoint throws not-found.

            cases.Add(CaseAsync("update_unknown_id_throws", "UpdateAsync UnknownId Throws", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_missing", "usr_mep_missing", false, true, "UnitTest");

                ModelEndpoint edit = NewInference("Ghost", "https://api.openai.com");
                edit.Id = "mep_does_not_exist";
                await AssertThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(auth, edit));
            }));

            // Negative: validating an unreachable endpoint yields an Unhealthy, persisted result (no throw).

            cases.Add(CaseAsync("validate_unreachable_endpoint_marks_unhealthy", "ValidateAsync marks an unreachable endpoint unhealthy", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_val", "usr_mep_val", false, true, "UnitTest");

                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Unreachable",
                    Kind = ModelEndpointKindEnum.Embedding,
                    Provider = ModelProviderEnum.OpenAI,
                    BaseUrl = "http://127.0.0.1:1",
                    Model = "text-embedding-3-small",
                    TimeoutMs = 2000
                };
                ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);

                ModelEndpointProbeResult result = await service.ValidateAsync(auth, created.Id).ConfigureAwait(false);
                AssertFalse(result.Success, "Expected validation against an unreachable endpoint to fail.");

                ModelEndpoint? reloaded = await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected endpoint to reload.");
                AssertEqual(EndpointHealthStatusEnum.Unhealthy, reloaded!.HealthStatus);
            }));

            // Validation appends to the persisted rolling health history and updates derived aggregates.

            cases.Add(CaseAsync("validate_appends_health_history", "ValidateAsync appends to the persisted health history", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_mep_hist", "usr_mep_hist", false, true, "UnitTest");

                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "History",
                    Kind = ModelEndpointKindEnum.Embedding,
                    Provider = ModelProviderEnum.OpenAI,
                    BaseUrl = "http://127.0.0.1:1",
                    Model = "text-embedding-3-small",
                    TimeoutMs = 2000
                };
                ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);
                AssertEqual(0, created.HealthHistory.Count);

                await service.ValidateAsync(auth, created.Id).ConfigureAwait(false);
                await service.ValidateAsync(auth, created.Id).ConfigureAwait(false);

                ModelEndpoint? reloaded = await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false);
                AssertNotNull(reloaded, "Expected endpoint to reload.");
                AssertEqual(2, reloaded!.HealthHistory.Count);
                AssertEqual(2, reloaded.ConsecutiveFailures);
                AssertEqual(0, reloaded.ConsecutiveSuccesses);
                AssertNotNull(reloaded.FirstHealthCheckUtc, "Expected FirstHealthCheckUtc to be derived from history.");
            }));

            cases.Add(CaseAsync("create_roundtrips_cloud_provider_fields", "CreateAsync persists region/project/api_version/access_key_id", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_cloud", "usr_cloud", false, true, "UnitTest");

                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Bedrock Claude",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.Bedrock,
                    Model = "anthropic.claude-3-5-sonnet-20240620-v1:0",
                    Region = "us-east-1",
                    AccessKeyId = "AKIAEXAMPLE",
                    ApiVersion = "2024-10-21",
                    Project = "demo-project"
                };
                endpoint.ApiKey = "aws-secret-access-key";

                ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);
                ModelEndpoint? reloaded = await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false);

                AssertNotNull(reloaded, "Expected the endpoint to reload after the migration added the columns.");
                AssertEqual(ModelProviderEnum.Bedrock, reloaded!.Provider);
                AssertEqual("us-east-1", reloaded.Region);
                AssertEqual("demo-project", reloaded.Project);
                AssertEqual("2024-10-21", reloaded.ApiVersion);
                AssertEqual("AKIAEXAMPLE", reloaded.AccessKeyId);
                AssertTrue(reloaded.HasApiKey, "Expected the AWS secret access key to be stored.");
            }));

            cases.Add(CaseAsync("factory_builds_azure_client_from_deployment", "ModelEndpointClientFactory builds an Azure client using the model as deployment", TestTags.Positive, () =>
            {
                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Azure GPT",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.AzureOpenAI,
                    BaseUrl = "https://my-resource.openai.azure.com",
                    Model = "gpt-4o-deployment"
                };
                endpoint.ApiKey = "azure-key";

                using (CompletionClientBase client = ModelEndpointClientFactory.CreateCompletion(endpoint, CreateLogging()))
                {
                    AssertEqual("gpt-4o-deployment", client.Model);
                }
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("factory_rejects_missing_vertex_project", "ModelEndpointClientFactory rejects a Vertex endpoint with no project", TestTags.Negative, async () =>
            {
                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Vertex",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.VertexAI,
                    Region = "us-central1"
                };
                endpoint.ApiKey = "{}";

                await AssertThrowsAsync<InvalidOperationException>(() =>
                {
                    ModelEndpointClientFactory.Create(endpoint, CreateLogging());
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("factory_rejects_missing_bedrock_region", "ModelEndpointClientFactory rejects a Bedrock endpoint with no region", TestTags.Negative, async () =>
            {
                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Bedrock",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.Bedrock,
                    AccessKeyId = "AKIAEXAMPLE"
                };
                endpoint.ApiKey = "secret";

                await AssertThrowsAsync<InvalidOperationException>(() =>
                {
                    ModelEndpointClientFactory.Create(endpoint, CreateLogging());
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("factory_create_dispatches_on_kind", "ModelEndpointClientFactory.Create returns an embedding client for Embedding endpoints and a completion client for Inference endpoints", TestTags.Positive, () =>
            {
                ModelEndpoint embedding = new ModelEndpoint
                {
                    Name = "OpenAI embeddings",
                    Kind = ModelEndpointKindEnum.Embedding,
                    Provider = ModelProviderEnum.OpenAI,
                    BaseUrl = "https://api.openai.com",
                    Model = "text-embedding-3-small",
                    TimeoutMs = 45000
                };
                embedding.ApiKey = "sk-test";

                using (ClientBase client = ModelEndpointClientFactory.Create(embedding, CreateLogging()))
                {
                    AssertTrue(client is OpenAiEmbeddingClient, "Expected an OpenAI embedding client for an Embedding endpoint.");
                    AssertEqual(45000, client.TimeoutMs);
                    AssertEqual("text-embedding-3-small", ((EmbeddingClientBase)client).Model);
                }

                ModelEndpoint inference = new ModelEndpoint
                {
                    Name = "Ollama chat",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.Ollama,
                    BaseUrl = "http://localhost:11434",
                    Model = "llama3"
                };

                using (ClientBase client = ModelEndpointClientFactory.Create(inference, CreateLogging()))
                {
                    AssertTrue(client is OllamaCompletionClient, "Expected an Ollama completion client for an Inference endpoint.");
                    AssertEqual("llama3", ((CompletionClientBase)client).Model);
                }
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("factory_builds_voyage_embedding_client", "ModelEndpointClientFactory builds a Voyage AI embedding client", TestTags.Positive, () =>
            {
                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Voyage",
                    Kind = ModelEndpointKindEnum.Embedding,
                    Provider = ModelProviderEnum.VoyageAI,
                    BaseUrl = "https://api.voyageai.com",
                    Model = "voyage-3"
                };
                endpoint.ApiKey = "voyage-key";

                using (EmbeddingClientBase client = ModelEndpointClientFactory.CreateEmbedding(endpoint, CreateLogging()))
                {
                    AssertTrue(client is VoyageAiEmbeddingClient, "Expected a Voyage AI embedding client.");
                    AssertEqual("voyage-3", client.Model);
                }
                return Task.CompletedTask;
            }));

            cases.Add(CaseAsync("factory_rejects_anthropic_embedding_client", "ModelEndpointClientFactory.CreateEmbedding rejects Anthropic, which has no embeddings API", TestTags.Negative, async () =>
            {
                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Anthropic",
                    Kind = ModelEndpointKindEnum.Inference,
                    Provider = ModelProviderEnum.Anthropic,
                    BaseUrl = "https://api.anthropic.com"
                };
                endpoint.ApiKey = "sk-ant";

                await AssertThrowsAsync<InvalidOperationException>(() =>
                {
                    ModelEndpointClientFactory.CreateEmbedding(endpoint, CreateLogging());
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("factory_rejects_voyage_completion_client", "ModelEndpointClientFactory.CreateCompletion rejects Voyage AI, which has no inference API", TestTags.Negative, async () =>
            {
                ModelEndpoint endpoint = new ModelEndpoint
                {
                    Name = "Voyage",
                    Kind = ModelEndpointKindEnum.Embedding,
                    Provider = ModelProviderEnum.VoyageAI,
                    BaseUrl = "https://api.voyageai.com"
                };
                endpoint.ApiKey = "voyage-key";

                await AssertThrowsAsync<InvalidOperationException>(() =>
                {
                    ModelEndpointClientFactory.CreateCompletion(endpoint, CreateLogging());
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            }));

            foreach (ModelProviderEnum provider in new[] { ModelProviderEnum.OllamaCloud, ModelProviderEnum.XAI })
            {
                foreach (ModelEndpointKindEnum kind in new[] { ModelEndpointKindEnum.Inference, ModelEndpointKindEnum.Embedding })
                {
                    cases.Add(CaseAsync("cloud_" + provider + "_" + kind, "Cloud provider round trip and capability client: " + provider + " " + kind, TestTags.Positive, async () =>
                    {
                        using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                        ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                        AuthContext auth = AuthContext.Authenticated("ten_cloud", "usr_cloud", false, true, "UnitTest");
                        ModelEndpoint endpoint = new ModelEndpoint { Name = "Cloud", Provider = provider, Kind = kind, BaseUrl = "", Model = "catalog-model", TimeoutMs = 34567 };
                        endpoint.ApiKey = "test-token";
                        ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);
                        AssertEqual(provider == ModelProviderEnum.OllamaCloud ? "https://ollama.com:443" : "https://api.x.ai", created.BaseUrl);
                        ModelEndpoint stored = (await service.ReadAsync(auth, created.Id).ConfigureAwait(false))!;
                        AssertEqual(provider, stored.Provider);
                        using (ClientBase client = ModelEndpointClientFactory.Create(stored, CreateLogging()))
                        {
                            string expected = provider == ModelProviderEnum.OllamaCloud ? "OllamaCloud" : "XAi";
                            AssertEqual(expected + (kind == ModelEndpointKindEnum.Inference ? "CompletionClient" : "EmbeddingClient"), client.GetType().Name);
                            AssertEqual(34567, client.TimeoutMs);
                            if (client is CompletionClientBase completion) AssertEqual("catalog-model", completion.Model);
                            if (client is EmbeddingClientBase embedding) AssertEqual("catalog-model", embedding.Model);
                        }
                        stored.ApiKey = null;
                        ModelEndpoint updated = await service.UpdateAsync(auth, stored).ConfigureAwait(false);
                        AssertTrue(updated.HasApiKey, "An unchanged edit must preserve the bearer token.");
                    }));
                }

                cases.Add(CaseAsync("cloud_auth_" + provider, "Cloud provider requires bearer authentication: " + provider, TestTags.Negative, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                    AuthContext auth = AuthContext.Authenticated("ten_cloud", "usr_cloud", false, true, "UnitTest");
                    ModelEndpoint endpoint = new ModelEndpoint { Name = "Cloud", Provider = provider, Kind = ModelEndpointKindEnum.Inference, BaseUrl = "" };
                    await AssertThrowsAsync<ArgumentException>(() => service.CreateAsync(auth, endpoint)).ConfigureAwait(false);
                    endpoint.ApiKey = "test-token";
                    endpoint.AuthType = ModelEndpointAuthTypeEnum.None;
                    await AssertThrowsAsync<ArgumentException>(() => service.CreateAsync(auth, endpoint)).ConfigureAwait(false);
                    endpoint.AuthType = ModelEndpointAuthTypeEnum.Bearer;
                    ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);
                    using ClientBase client = ModelEndpointClientFactory.Create(created, CreateLogging());
                }));
            }

            cases.Add(CaseAsync("cloud_fixed_url", "Ollama Cloud rejects an endpoint override", TestTags.Negative, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                ModelEndpoint endpoint = new ModelEndpoint { Name = "Cloud", Provider = ModelProviderEnum.OllamaCloud, BaseUrl = "https://example.com" };
                endpoint.ApiKey = "test-token";
                await AssertThrowsAsync<ArgumentException>(() => service.CreateAsync(AuthContext.Authenticated("ten_cloud", "usr_cloud", false, true, "UnitTest"), endpoint)).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("probe_keeps_configuration_saved_during_health_check", "A configuration save during a health probe keeps the model, URL, and key", TestTags.Positive, async () =>
            {
                using (HeldHealthProbe probe = new HeldHealthProbe(1))
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                    AuthContext auth = AuthContext.Authenticated("ten_health", "usr_health", false, true, "Test");
                    ModelEndpoint created = await service.CreateAsync(auth, NewOllama("held", probe.BaseUrl, "original-model", "original-key")).ConfigureAwait(false);
                    Task<int> sweep = service.CheckHealthAllAsync();
                    bool arrived = await probe.WaitForProbesAsync().ConfigureAwait(false);
                    if (arrived)
                    {
                        ModelEndpoint edit = (await service.ReadAsync(auth, created.Id).ConfigureAwait(false))!;
                        edit.Model = "saved-model";
                        edit.BaseUrl = "http://127.0.0.1:9";
                        edit.ApiKeyInput = "saved-key";
                        await service.UpdateAsync(auth, edit).ConfigureAwait(false);
                    }

                    probe.Release();
                    await sweep.ConfigureAwait(false);
                    AssertTrue(arrived, "The health probe did not reach the endpoint.");
                    ModelEndpoint stored = (await service.ReadAsync(auth, created.Id).ConfigureAwait(false))!;
                    AssertEqual("saved-model", stored.Model, "saved model");
                    AssertEqual("http://127.0.0.1:9", stored.BaseUrl, "saved url");
                    AssertEqual("saved-key", stored.ApiKey, "saved key");
                    AssertEqual(EndpointHealthStatusEnum.Unknown, stored.HealthStatus, "stale health");
                    AssertEqual(0, stored.HealthHistory.Count, "stale history");
                }
            }));

            cases.Add(CaseAsync("probe_records_health_when_configuration_is_unchanged", "A health probe with no configuration edit records health and keeps the model and key", TestTags.Positive, async () =>
            {
                using (HeldHealthProbe probe = new HeldHealthProbe(1))
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                    AuthContext auth = AuthContext.Authenticated("ten_health", "usr_health", false, true, "Test");
                    ModelEndpoint created = await service.CreateAsync(auth, NewOllama("held", probe.BaseUrl, "original-model", "original-key")).ConfigureAwait(false);
                    Task<int> sweep = service.CheckHealthAllAsync();
                    bool arrived = await probe.WaitForProbesAsync().ConfigureAwait(false);
                    probe.Release();
                    int probed = await sweep.ConfigureAwait(false);
                    AssertTrue(arrived, "The health probe did not reach the endpoint.");
                    AssertEqual(1, probed, "probed groups");
                    ModelEndpoint stored = (await service.ReadAsync(auth, created.Id).ConfigureAwait(false))!;
                    AssertEqual("original-model", stored.Model, "model");
                    AssertEqual("original-key", stored.ApiKey, "key");
                    AssertEqual(probe.BaseUrl, stored.BaseUrl, "url");
                    AssertEqual(EndpointHealthStatusEnum.Healthy, stored.HealthStatus, "health");
                    AssertEqual(1, stored.HealthHistory.Count, "history");
                    AssertTrue(stored.HealthHistory[0].Success, "history success");
                }
            }));

            cases.Add(CaseAsync("concurrent_health_checks_keep_both_results", "Two overlapping health probes both remain in history", TestTags.Positive, async () =>
            {
                using (HeldHealthProbe probe = new HeldHealthProbe(2))
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                    AuthContext auth = AuthContext.Authenticated("ten_health", "usr_health", false, true, "Test");
                    ModelEndpoint created = await service.CreateAsync(auth, NewOllama("held", probe.BaseUrl, "original-model", "original-key")).ConfigureAwait(false);
                    Task<int> first = service.CheckHealthAllAsync();
                    Task<int> second = service.CheckHealthAllAsync();
                    bool arrived = await probe.WaitForProbesAsync().ConfigureAwait(false);
                    probe.Release();
                    await first.ConfigureAwait(false);
                    await second.ConfigureAwait(false);
                    AssertTrue(arrived, "Both health probes did not reach the endpoint.");
                    ModelEndpoint stored = (await service.ReadAsync(auth, created.Id).ConfigureAwait(false))!;
                    AssertEqual("original-model", stored.Model, "model");
                    AssertEqual("original-key", stored.ApiKey, "key");
                    AssertEqual(2, stored.HealthHistory.Count, "both health results");
                    AssertTrue(stored.HealthHistory[0].Success && stored.HealthHistory[1].Success, "both results succeeded");
                }
            }));

            cases.Add(CaseAsync("grouped_probe_keeps_each_member_configuration", "One grouped probe keeps a member configuration saved during the probe", TestTags.Positive, async () =>
            {
                using (HeldHealthProbe probe = new HeldHealthProbe(1))
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                    AuthContext auth = AuthContext.Authenticated("ten_health", "usr_health", false, true, "Test");
                    ModelEndpoint first = await service.CreateAsync(auth, NewOllama("member-a", probe.BaseUrl, "model-a", "group-key")).ConfigureAwait(false);
                    ModelEndpoint second = await service.CreateAsync(auth, NewOllama("member-b", probe.BaseUrl, "model-b", "group-key")).ConfigureAwait(false);
                    Task<int> sweep = service.CheckHealthAllAsync();
                    bool arrived = await probe.WaitForProbesAsync().ConfigureAwait(false);
                    if (arrived)
                    {
                        ModelEndpoint edit = (await service.ReadAsync(auth, first.Id).ConfigureAwait(false))!;
                        edit.Model = "saved-model";
                        edit.BaseUrl = "http://127.0.0.1:9";
                        edit.ApiKeyInput = "saved-key";
                        await service.UpdateAsync(auth, edit).ConfigureAwait(false);
                    }

                    probe.Release();
                    int probed = await sweep.ConfigureAwait(false);
                    AssertTrue(arrived, "The grouped health probe did not reach the endpoint.");
                    AssertEqual(1, probed, "one probe for the group");
                    ModelEndpoint changed = (await service.ReadAsync(auth, first.Id).ConfigureAwait(false))!;
                    ModelEndpoint unchanged = (await service.ReadAsync(auth, second.Id).ConfigureAwait(false))!;
                    AssertEqual("saved-model", changed.Model, "member model");
                    AssertEqual("http://127.0.0.1:9", changed.BaseUrl, "member url");
                    AssertEqual("saved-key", changed.ApiKey, "member key");
                    AssertEqual(EndpointHealthStatusEnum.Unknown, changed.HealthStatus, "member health");
                    AssertEqual("model-b", unchanged.Model, "other member model");
                    AssertEqual(EndpointHealthStatusEnum.Healthy, unchanged.HealthStatus, "other member health");
                    AssertEqual(1, unchanged.HealthHistory.Count, "other member history");
                }
            }));

            cases.Add(CaseAsync("probe_discards_health_when_only_region_changes", "A health probe discards its result when only the region changed", TestTags.Positive, async () =>
            {
                using (HeldHealthProbe probe = new HeldHealthProbe(1))
                using (TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false))
                {
                    ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                    AuthContext auth = AuthContext.Authenticated("ten_health", "usr_health", false, true, "Test");
                    ModelEndpoint created = await service.CreateAsync(auth, NewOllama("held", probe.BaseUrl, "original-model", "original-key")).ConfigureAwait(false);
                    Task<int> sweep = service.CheckHealthAllAsync();
                    bool arrived = await probe.WaitForProbesAsync().ConfigureAwait(false);
                    if (arrived)
                    {
                        ModelEndpoint edit = (await service.ReadAsync(auth, created.Id).ConfigureAwait(false))!;
                        edit.Region = "eu-central-1";
                        await service.UpdateAsync(auth, edit).ConfigureAwait(false);
                    }

                    probe.Release();
                    await sweep.ConfigureAwait(false);
                    AssertTrue(arrived, "The health probe did not reach the endpoint.");
                    ModelEndpoint stored = (await service.ReadAsync(auth, created.Id).ConfigureAwait(false))!;
                    AssertEqual("eu-central-1", stored.Region, "saved region");
                    AssertEqual("original-model", stored.Model, "model");
                    AssertEqual("original-key", stored.ApiKey, "key");
                    AssertEqual(EndpointHealthStatusEnum.Unknown, stored.HealthStatus, "stale health");
                }
            }));

            string? simulation = Environment.GetEnvironmentVariable("ARMADA_MODEL_PROVIDER_SIMULATION");
            if (!String.IsNullOrWhiteSpace(simulation))
            {
                cases.Add(CaseAsync("xai_http_validation", "xAI inference and embeddings validate through the native HTTP contract", TestTags.Positive, async () =>
                {
                    using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                    ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                    AuthContext auth = AuthContext.Authenticated("ten_http", "usr_http", false, true, "UnitTest");
                    foreach (ModelEndpointKindEnum kind in new[] { ModelEndpointKindEnum.Inference, ModelEndpointKindEnum.Embedding })
                    {
                        ModelEndpoint endpoint = new ModelEndpoint { Name = "HTTP " + kind, Provider = ModelProviderEnum.XAI, Kind = kind, BaseUrl = simulation, Model = "test-model" };
                        endpoint.ApiKey = "test-token";
                        ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);
                        ModelEndpointProbeResult result = await service.ValidateAsync(auth, created.Id).ConfigureAwait(false);
                        AssertTrue(result.Success, "Native xAI validation failed: " + result.Error);
                        if (kind == ModelEndpointKindEnum.Inference) AssertEqual("pong", result.SampleText);
                        else AssertEqual(3, result.EmbeddingDimensions ?? 0);
                    }
                }));
            }

            return new TestSuiteDescriptor(
                suiteId: "Services.ModelEndpointService",
                displayName: "Model Endpoint Service",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static ModelEndpoint NewOllama(string name, string baseUrl, string model, string apiKey)
        {
            ModelEndpoint endpoint = new ModelEndpoint
            {
                Name = name,
                Kind = ModelEndpointKindEnum.Inference,
                Provider = ModelProviderEnum.Ollama,
                BaseUrl = baseUrl,
                Model = model,
                Region = "us-east-1",
                TimeoutMs = 30000
            };
            endpoint.ApiKey = apiKey;
            return endpoint;
        }

        private static ModelEndpoint NewInference(string name, string baseUrl)
        {
            return new ModelEndpoint
            {
                Name = name,
                Kind = ModelEndpointKindEnum.Inference,
                Provider = ModelProviderEnum.OpenAI,
                BaseUrl = baseUrl,
                Model = "gpt-4o-mini"
            };
        }

        private static TestCaseDescriptor CaseAsync(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ModelEndpointService",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion

        #region Private-Classes

        /// <summary>
        /// Accepts health-probe HTTP calls and holds the response until the test releases it.
        /// </summary>
        private sealed class HeldHealthProbe : IDisposable
        {
            private readonly HttpListener _Listener;
            private readonly List<Task> _Responses = new List<Task>();
            private readonly object _Gate = new object();
            private readonly TaskCompletionSource<bool> _AllArrived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _Release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly int _Expected;
            private int _Arrived;

            /// <summary>
            /// Base URL of the held endpoint.
            /// </summary>
            public string BaseUrl { get; }

            /// <summary>
            /// Start a listener that holds each probe response.
            /// </summary>
            /// <param name="expectedProbes">Number of probe requests to wait for.</param>
            public HeldHealthProbe(int expectedProbes)
            {
                _Expected = expectedProbes;
                int boundPort;
                _Listener = TestPorts.StartHttpListener(port => new[] { "http://127.0.0.1:" + port + "/" }, out boundPort);
                BaseUrl = "http://127.0.0.1:" + boundPort;
                AcceptLoopAsync().ContinueWith(task =>
                {
                    if (task.Exception != null) task.Exception.Handle(ex => true);
                }, TaskScheduler.Default);
            }

            /// <summary>
            /// Wait until the expected probe requests have arrived.
            /// </summary>
            /// <returns>True when they arrived within 20 seconds.</returns>
            public async Task<bool> WaitForProbesAsync()
            {
                Task delay = Task.Delay(TimeSpan.FromSeconds(20));
                Task finished = await Task.WhenAny(_AllArrived.Task, delay).ConfigureAwait(false);
                return finished == _AllArrived.Task;
            }

            /// <summary>
            /// Send the held probe responses.
            /// </summary>
            public void Release()
            {
                _Release.TrySetResult(true);
            }

            /// <summary>
            /// Release held calls and stop the listener.
            /// </summary>
            public void Dispose()
            {
                Release();
                try { _Listener.Stop(); } catch { }
                try { _Listener.Close(); } catch { }
            }

            private async Task AcceptLoopAsync()
            {
                while (true)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _Listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        return;
                    }

                    int arrived = Interlocked.Increment(ref _Arrived);
                    if (arrived >= _Expected) _AllArrived.TrySetResult(true);
                    Task response = RespondAsync(context);
                    lock (_Gate) { _Responses.Add(response); }
                }
            }

            private async Task RespondAsync(HttpListenerContext context)
            {
                try
                {
                    await _Release.Task.ConfigureAwait(false);
                    byte[] body = Encoding.UTF8.GetBytes("{\"models\":[]}");
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = body.Length;
                    using (Stream stream = context.Response.OutputStream)
                    {
                        await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                    }

                    context.Response.Close();
                }
                catch
                {
                    try { context.Response.Abort(); } catch { }
                }
            }
        }

        #endregion
    }
}
