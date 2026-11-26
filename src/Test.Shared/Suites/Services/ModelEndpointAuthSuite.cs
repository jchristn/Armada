namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Harbor;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Model endpoint authentication types and extra headers: what reaches the wire for each type, validation, the
    /// update merge that keeps write-only header values, that values never appear in API JSON, the database round trip,
    /// and the transport form shipped to a Harbor.
    /// </summary>
    public sealed class ModelEndpointAuthSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ModelEndpointAuth";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(CaseAsync("types_reach_the_wire", "Bearer, Basic, Header, and QueryParameter put the secret where they say, with extra headers on every type", TestTags.Positive, async () =>
            {
                HttpRequestMessage bearer = await SendAsync(Endpoint(ModelEndpointAuthTypeEnum.Bearer, "tok")).ConfigureAwait(false);
                AssertEqual("Bearer tok", bearer.Headers.Authorization!.ToString());
                AssertEqual("cf-header-value-7731", bearer.Headers.GetValues("CF-Access-Client-Id").Single(), "extra header sent");

                ModelEndpoint basicEndpoint = Endpoint(ModelEndpointAuthTypeEnum.Basic, "pw");
                basicEndpoint.AuthUsername = "joel";
                HttpRequestMessage basic = await SendAsync(basicEndpoint).ConfigureAwait(false);
                AssertEqual("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("joel:pw")), basic.Headers.Authorization!.ToString());

                ModelEndpoint headerEndpoint = Endpoint(ModelEndpointAuthTypeEnum.Header, "k1");
                headerEndpoint.AuthHeaderName = "X-API-Key";
                HttpRequestMessage header = await SendAsync(headerEndpoint).ConfigureAwait(false);
                AssertEqual("k1", header.Headers.GetValues("X-API-Key").Single());
                AssertNull(header.Headers.Authorization, "no Authorization header");

                ModelEndpoint queryEndpoint = Endpoint(ModelEndpointAuthTypeEnum.QueryParameter, "a b");
                queryEndpoint.AuthQueryParameter = "key";
                HttpRequestMessage query = await SendAsync(queryEndpoint, "http://ollama:11434/api/chat?stream=false").ConfigureAwait(false);
                AssertEqual("?stream=false&key=a%20b", query.RequestUri!.Query);

                HttpRequestMessage none = await SendAsync(Endpoint(ModelEndpointAuthTypeEnum.None, "ignored")).ConfigureAwait(false);
                AssertNull(none.Headers.Authorization, "None sends no credential");
                AssertTrue(none.Headers.Contains("CF-Access-Client-Id"), "None still sends extra headers");
            }));

            cases.Add(Case("provider_default_keeps_provider_scheme", "ProviderDefault hands the secret to the provider and adds no client; other types hand it none", TestTags.Positive, () =>
            {
                ModelEndpoint endpoint = Endpoint(ModelEndpointAuthTypeEnum.ProviderDefault, "sk");
                endpoint.Headers = new List<ModelEndpointHeader>();
                AssertEqual("sk", ModelEndpointAuth.ProviderSecret(endpoint));
                AssertNull(ModelEndpointAuth.CreateHttpClient(endpoint), "no custom client without headers");
                endpoint.AuthType = ModelEndpointAuthTypeEnum.Bearer;
                AssertEqual("", ModelEndpointAuth.ProviderSecret(endpoint), "the provider adds no header of its own");
                using (HttpClient? client = ModelEndpointAuth.CreateHttpClient(endpoint))
                    AssertNotNull(client, "custom client for Bearer");
            }));

            cases.Add(Case("validation", "Validation rejects missing names, signing providers with other types, bad and duplicate headers, and conflicts", TestTags.Negative, () =>
            {
                ModelEndpoint basic = Endpoint(ModelEndpointAuthTypeEnum.Basic, "pw");
                AssertThrows<ArgumentException>(() => ModelEndpointAuth.Validate(basic));
                ModelEndpoint header = Endpoint(ModelEndpointAuthTypeEnum.Header, "v");
                AssertThrows<ArgumentException>(() => ModelEndpointAuth.Validate(header));
                ModelEndpoint query = Endpoint(ModelEndpointAuthTypeEnum.QueryParameter, "v");
                AssertThrows<ArgumentException>(() => ModelEndpointAuth.Validate(query));
                ModelEndpoint bedrock = Endpoint(ModelEndpointAuthTypeEnum.Bearer, "v");
                bedrock.Provider = ModelProviderEnum.Bedrock;
                AssertThrows<ArgumentException>(() => ModelEndpointAuth.Validate(bedrock));
                ModelEndpoint spaced = Endpoint(ModelEndpointAuthTypeEnum.None, null);
                spaced.Headers = new List<ModelEndpointHeader> { new ModelEndpointHeader("Bad Name", "v") };
                AssertThrows<ArgumentException>(() => ModelEndpointAuth.Validate(spaced));
                ModelEndpoint duplicate = Endpoint(ModelEndpointAuthTypeEnum.None, null);
                duplicate.Headers = new List<ModelEndpointHeader> { new ModelEndpointHeader("X-A", "1"), new ModelEndpointHeader("x-a", "2") };
                AssertThrows<ArgumentException>(() => ModelEndpointAuth.Validate(duplicate));
                ModelEndpoint conflict = Endpoint(ModelEndpointAuthTypeEnum.Bearer, "t");
                conflict.Headers = new List<ModelEndpointHeader> { new ModelEndpointHeader("Authorization", "x") };
                AssertThrows<ArgumentException>(() => ModelEndpointAuth.Validate(conflict));
                ModelEndpoint valid = Endpoint(ModelEndpointAuthTypeEnum.Bearer, "t");
                ModelEndpointAuth.Validate(valid);
            }));

            cases.Add(CaseAsync("update_merges_header_values", "An update keeps a header's stored value when it is sent without one, drops omitted headers, and keeps all when the list is omitted", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpointService service = new ModelEndpointService(testDb.Driver, CreateLogging());
                AuthContext auth = AuthContext.Authenticated("ten_auth", "usr_auth", false, true, "UnitTest");

                ModelEndpoint endpoint = Endpoint(ModelEndpointAuthTypeEnum.Bearer, "tok");
                endpoint.Headers = new List<ModelEndpointHeader> { new ModelEndpointHeader("CF-Access-Client-Id", "id1"), new ModelEndpointHeader("CF-Access-Client-Secret", "s1") };
                ModelEndpoint created = await service.CreateAsync(auth, endpoint).ConfigureAwait(false);

                ModelEndpoint edit = Deserialize("{\"id\":\"" + created.Id + "\",\"name\":\"Ollama\",\"provider\":\"Ollama\",\"kind\":\"Inference\",\"baseUrl\":\"http://ollama:11434\","
                    + "\"authType\":\"Bearer\",\"headers\":[{\"name\":\"cf-access-client-id\",\"value\":null}]}");
                await service.UpdateAsync(auth, edit).ConfigureAwait(false);
                ModelEndpoint? afterEdit = await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false);
                AssertEqual(1, afterEdit!.Headers.Count, "omitted header removed");
                AssertEqual("id1", afterEdit.Headers[0].Value, "stored value kept");
                AssertEqual("tok", afterEdit.ApiKey, "secret kept when apiKey is omitted");

                ModelEndpoint untouched = Deserialize("{\"id\":\"" + created.Id + "\",\"name\":\"Ollama\",\"provider\":\"Ollama\",\"kind\":\"Inference\",\"baseUrl\":\"http://ollama:11434\",\"authType\":\"Bearer\"}");
                await service.UpdateAsync(auth, untouched).ConfigureAwait(false);
                ModelEndpoint? afterUntouched = await testDb.Driver.ModelEndpoints.ReadAsync(created.Id).ConfigureAwait(false);
                AssertEqual(1, afterUntouched!.Headers.Count, "omitted list keeps the stored headers");

                ModelEndpoint unknown = Deserialize("{\"id\":\"" + created.Id + "\",\"name\":\"Ollama\",\"provider\":\"Ollama\",\"kind\":\"Inference\",\"baseUrl\":\"http://ollama:11434\","
                    + "\"authType\":\"Bearer\",\"headers\":[{\"name\":\"X-New\"}]}");
                await AssertThrowsAsync<ArgumentException>(() => service.UpdateAsync(auth, unknown)).ConfigureAwait(false);
            }));

            cases.Add(CaseAsync("database_round_trip", "Auth type, names, and header values survive the database; API JSON hides the values", TestTags.Positive, async () =>
            {
                using TestDatabase testDb = await TestDatabaseHelper.CreateDatabaseAsync().ConfigureAwait(false);
                ModelEndpoint endpoint = Endpoint(ModelEndpointAuthTypeEnum.Header, "endpoint-secret-4410");
                endpoint.AuthHeaderName = "X-API-Key";
                endpoint.TenantId = "ten_auth";
                await testDb.Driver.ModelEndpoints.CreateAsync(endpoint).ConfigureAwait(false);

                ModelEndpoint? read = await testDb.Driver.ModelEndpoints.ReadAsync(endpoint.Id).ConfigureAwait(false);
                AssertEqual(ModelEndpointAuthTypeEnum.Header, read!.AuthType);
                AssertEqual("X-API-Key", read.AuthHeaderName);
                AssertEqual("cf-header-value-7731", read.Headers.Single().Value, "header value stored");

                string json = JsonSerializer.Serialize(read, ArmadaJson.Options);
                AssertFalse(json.Contains("cf-header-value-7731", StringComparison.Ordinal), "header value not serialized");
                AssertFalse(json.Contains("endpoint-secret-4410", StringComparison.Ordinal), "secret not serialized");
                ModelEndpoint client = Deserialize(json);
                AssertTrue(client.Headers.Single().HasValue, "hasValue serialized");
                AssertNull(client.Headers.Single().Value, "value not serialized");
                AssertEqual(ModelEndpointAuthTypeEnum.Header, client.AuthType, "auth type serialized");
                AssertEqual("X-API-Key", client.AuthHeaderName, "header name serialized");
            }));

            cases.Add(Case("harbor_transport_round_trip", "The endpoint shipped to a Harbor carries the auth type, secret, and header values", TestTags.Positive, () =>
            {
                ModelEndpoint endpoint = Endpoint(ModelEndpointAuthTypeEnum.Basic, "pw");
                endpoint.AuthUsername = "joel";
                endpoint.Region = "us-east-1";
                string json = JsonSerializer.Serialize(HarborInferenceEndpoint.FromEndpoint(endpoint));
                ModelEndpoint shipped = JsonSerializer.Deserialize<HarborInferenceEndpoint>(json)!.ToEndpoint();
                AssertEqual(ModelEndpointAuthTypeEnum.Basic, shipped.AuthType);
                AssertEqual("joel", shipped.AuthUsername);
                AssertEqual("pw", shipped.ApiKey);
                AssertEqual("us-east-1", shipped.Region);
                AssertEqual("cf-header-value-7731", shipped.Headers.Single().Value);
            }));

            cases.Add(CaseAsync("required_key_placeholder_never_reaches_the_wire", "A provider client that requires a key gets a placeholder under Armada authentication, and the handler removes it before sending", TestTags.Positive, async () =>
            {
                ModelEndpoint anthropic = Endpoint(ModelEndpointAuthTypeEnum.Bearer, "tok");
                anthropic.Provider = ModelProviderEnum.Anthropic;
                anthropic.BaseUrl = "https://gateway.example.com";
                using (PolyPrompt.Clients.ProviderClientBase client = ModelEndpointClientFactory.CreateCompletion(anthropic, CreateLogging()))
                    AssertTrue(client is PolyPrompt.Clients.AnthropicClient, "Bearer authentication still builds the Anthropic client");

                CapturingHandler inner = new CapturingHandler();
                using (HttpMessageInvoker invoker = new HttpMessageInvoker(new ModelEndpointAuthHandler(anthropic, inner)))
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://gateway.example.com/v1/messages"))
                {
                    request.Headers.TryAddWithoutValidation("x-api-key", ModelEndpointAuthHandler.ExternalAuthPlaceholder);
                    request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                    await invoker.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
                }
                AssertFalse(inner.Last!.Headers.Contains("x-api-key"), "the placeholder key header is removed");
                AssertTrue(inner.Last!.Headers.Contains("anthropic-version"), "other provider headers stay");
                AssertEqual("Bearer tok", inner.Last!.Headers.Authorization?.ToString());

                ModelEndpoint missing = Endpoint(ModelEndpointAuthTypeEnum.ProviderDefault, null);
                missing.Provider = ModelProviderEnum.Anthropic;
                missing.Headers = new List<ModelEndpointHeader>();
                AssertThrows<InvalidOperationException>(() => ModelEndpointClientFactory.CreateCompletion(missing, CreateLogging()));
            }));

            cases.Add(Case("azure_uses_v1_chat_completions", "Azure OpenAI is built as a Chat Completions client on the v1 route with the api-key header", TestTags.Positive, () =>
            {
                ModelEndpoint azure = new ModelEndpoint { Name = "Azure", Provider = ModelProviderEnum.AzureOpenAI, Kind = ModelEndpointKindEnum.Inference,
                    BaseUrl = "https://my-resource.openai.azure.com/", Model = "my-deployment" };
                azure.ApiKey = "azure-key";
                using (PolyPrompt.Clients.ProviderClientBase client = ModelEndpointClientFactory.CreateCompletion(azure, CreateLogging()))
                {
                    PolyPrompt.Clients.OpenAiCompatibleClient? compatible = client as PolyPrompt.Clients.OpenAiCompatibleClient;
                    AssertNotNull(compatible, "Azure uses the Chat Completions client");
                    AssertEqual("https://my-resource.openai.azure.com/openai/v1", compatible!.Endpoint);
                    AssertEqual("api-key", compatible.AuthPlacement.Name);
                    AssertEqual("my-deployment", client.Defaults.Completion?.Model);
                }
            }));

            return new TestSuiteDescriptor(suiteId: SuiteId, displayName: "Model Endpoint Authentication", cases: cases);
        }

        #endregion

        #region Private-Methods

        private static ModelEndpoint Endpoint(ModelEndpointAuthTypeEnum authType, string? secret)
        {
            ModelEndpoint endpoint = new ModelEndpoint
            {
                Name = "Ollama",
                Provider = ModelProviderEnum.Ollama,
                Kind = ModelEndpointKindEnum.Inference,
                BaseUrl = "http://ollama:11434",
                AuthType = authType,
                Headers = new List<ModelEndpointHeader> { new ModelEndpointHeader("CF-Access-Client-Id", "cf-header-value-7731") }
            };
            endpoint.ApiKey = secret;
            return endpoint;
        }

        private static ModelEndpoint Deserialize(string json)
        {
            return JsonSerializer.Deserialize<ModelEndpoint>(json, ArmadaJson.Options)!;
        }

        private static async Task<HttpRequestMessage> SendAsync(ModelEndpoint endpoint, string url = "http://ollama:11434/api/chat")
        {
            CapturingHandler inner = new CapturingHandler();
            using (HttpMessageInvoker invoker = new HttpMessageInvoker(new ModelEndpointAuthHandler(endpoint, inner)))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                await invoker.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
            }

            return inner.Last!;
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return logging;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return CaseAsync(caseId, displayName, tag, () =>
            {
                body();
                return Task.CompletedTask;
            });
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

        #region Private-Classes

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public HttpRequestMessage? Last { get; private set; } = null;

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Last = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }

        #endregion
    }
}
