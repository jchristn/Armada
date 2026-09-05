namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ModelEndpointService"/>: CRUD over a live SQLite store, provider/kind
    /// capability guards, API-key preservation on update, and base-URL health deduplication. Positive
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
    }
}
