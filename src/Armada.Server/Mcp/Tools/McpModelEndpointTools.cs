namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers MCP tools for managing and monitoring model endpoints (embedding and inference).
    /// </summary>
    public static class McpModelEndpointTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Registers model endpoint MCP tools.
        /// </summary>
        /// <param name="register">Tool registration delegate.</param>
        /// <param name="endpoints">Model endpoint service.</param>
        public static void Register(RegisterToolDelegate register, ModelEndpointService endpoints)
        {
            register(
                "get_model_endpoint",
                "Inspect one managed model endpoint (embedding or inference) by ID. The API key is never returned.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        endpointId = new { type = "string", description = "Model endpoint ID (mep_ prefix)" }
                    },
                    required = new[] { "endpointId" }
                },
                async (args) =>
                {
                    ModelEndpointIdArgs request = JsonSerializer.Deserialize<ModelEndpointIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ModelEndpointIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    ModelEndpoint? endpoint = await endpoints.ReadAsync(auth, request.EndpointId).ConfigureAwait(false);
                    if (endpoint == null) return (object)McpToolError.NotFound("Model endpoint not found");
                    return (object)endpoint;
                });

            register(
                "create_model_endpoint",
                "Create a managed model endpoint. Kind must be one of " + McpToolHelpers.EnumNames<ModelEndpointKindEnum>() + "; Provider is one of " + McpToolHelpers.EnumNames<ModelProviderEnum>() + ". Anthropic cannot be used for Embedding and VoyageAI cannot be used for Inference.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Human-facing endpoint name" },
                        kind = new { type = "string", description = McpToolHelpers.EnumNames<ModelEndpointKindEnum>() },
                        provider = new { type = "string", description = McpToolHelpers.EnumNames<ModelProviderEnum>() },
                        baseUrl = new { type = "string", description = "Base URL of the endpoint" },
                        model = new { type = "string", description = "Model identifier to request (optional)" },
                        apiKey = new { type = "string", description = "API key/credential (optional; stored but never returned)" },
                        authType = new { type = "string", description = "How the endpoint authenticates; the secret is apiKey. " + McpToolHelpers.EnumNames<ModelEndpointAuthTypeEnum>() + ". ProviderDefault (default) uses the provider's scheme (Bearer for Ollama/OpenAI, x-api-key for Anthropic, ...); Bearer sends Authorization: Bearer <apiKey>; Basic sends authUsername and apiKey; Header sends <authHeaderName>: <apiKey>; QueryParameter appends ?<authQueryParameter>=<apiKey>; None sends no credential. Vertex AI and Bedrock accept only ProviderDefault." },
                        authUsername = new { type = "string", description = "User name for Basic authentication" },
                        authHeaderName = new { type = "string", description = "Header that carries apiKey for Header authentication, e.g. X-API-Key" },
                        authQueryParameter = new { type = "string", description = "Query parameter that carries apiKey for QueryParameter authentication, e.g. key" },
                        headers = new { type = "array", description = "Extra headers sent with every request, e.g. Cloudflare Access headers (at most 256). Values are stored but never returned.", items = new { type = "object", properties = new { name = new { type = "string" }, value = new { type = "string" } }, required = new[] { "name" } } },
                        dimensionality = new { type = "integer", description = "Optional embedding dimensionality hint" },
                        timeoutMs = new { type = "integer", description = "Per-request timeout in milliseconds (default 120000, clamped to 1000-600000)" },
                        inferenceOptions = new { type = "object", description = "Inference defaults and enforced tool policy; omit on update to preserve.", properties = new {
                            toolsEnabled = new { type = "boolean", @default = true },
                            readOnlyTools = new { type = "boolean", @default = false, description = "Only built-in read-only tools; excludes shell, file changes and MCP." },
                            mcpToolsEnabled = new { type = "boolean", @default = true },
                            allowedTools = new { type = new[] { "array", "null" }, items = new { type = "string", minLength = 1, maxLength = 256 }, maxItems = 256, uniqueItems = true, description = "Exact case-sensitive names; null permits all otherwise eligible tools, [] none." },
                            maxIterations = new { type = "integer", minimum = 1, maximum = 1000, @default = 100 },
                            maxTokens = new { type = new[] { "integer", "null" }, minimum = 1, maximum = 10000000 },
                            temperature = new { type = new[] { "number", "null" }, minimum = 0, maximum = 2 },
                            topP = new { type = new[] { "number", "null" }, minimum = 0, maximum = 1 },
                            requireHarbor = new { type = "boolean", @default = false }
                        } },
                        enabled = new { type = "boolean", description = "Whether the endpoint is enabled (default true)" }
                    },
                    required = new[] { "name", "baseUrl" }
                },
                async (args) =>
                {
                    ModelEndpointUpsertArgs request = JsonSerializer.Deserialize<ModelEndpointUpsertArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ModelEndpointUpsertArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();

                    ModelEndpoint endpoint = new ModelEndpoint();
                    if (!String.IsNullOrWhiteSpace(request.Name)) endpoint.Name = request.Name;
                    if (request.Kind.HasValue) endpoint.Kind = request.Kind.Value;
                    if (request.Provider.HasValue) endpoint.Provider = request.Provider.Value;
                    if (request.BaseUrl != null) endpoint.BaseUrl = request.BaseUrl;
                    endpoint.Model = request.Model;
                    if (request.Dimensionality.HasValue) endpoint.Dimensionality = request.Dimensionality.Value;
                    if (request.TimeoutMs.HasValue) endpoint.TimeoutMs = request.TimeoutMs.Value;
                    if (request.InferenceOptions != null) endpoint.InferenceOptions = request.InferenceOptions;
                    if (request.Enabled.HasValue) endpoint.Enabled = request.Enabled.Value;
                    if (request.ApiKey != null) endpoint.ApiKey = request.ApiKey;
                    if (request.AuthType.HasValue) endpoint.AuthType = request.AuthType.Value;
                    endpoint.AuthUsername = request.AuthUsername;
                    endpoint.AuthHeaderName = request.AuthHeaderName;
                    endpoint.AuthQueryParameter = request.AuthQueryParameter;
                    if (request.Headers != null) endpoint.Headers = request.Headers;

                    try
                    {
                        return (object)await endpoints.CreateAsync(auth, endpoint).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                    {
                        return (object)McpToolError.FromException(ex);
                    }
                });

            register(
                "update_model_endpoint",
                "Update a managed model endpoint. Only supplied fields change; omit apiKey to keep the stored key.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        endpointId = new { type = "string", description = "Model endpoint ID (mep_ prefix)" },
                        name = new { type = "string", description = "Human-facing endpoint name" },
                        kind = new { type = "string", description = McpToolHelpers.EnumNames<ModelEndpointKindEnum>() },
                        provider = new { type = "string", description = McpToolHelpers.EnumNames<ModelProviderEnum>() },
                        baseUrl = new { type = "string", description = "Base URL of the endpoint" },
                        model = new { type = "string", description = "Model identifier to request" },
                        apiKey = new { type = "string", description = "API key/credential (omit to keep the stored key)" },
                        authType = new { type = "string", description = "How the endpoint authenticates; the secret is apiKey. " + McpToolHelpers.EnumNames<ModelEndpointAuthTypeEnum>() + ". ProviderDefault (default) uses the provider's scheme (Bearer for Ollama/OpenAI, x-api-key for Anthropic, ...); Bearer sends Authorization: Bearer <apiKey>; Basic sends authUsername and apiKey; Header sends <authHeaderName>: <apiKey>; QueryParameter appends ?<authQueryParameter>=<apiKey>; None sends no credential. Vertex AI and Bedrock accept only ProviderDefault." },
                        authUsername = new { type = "string", description = "User name for Basic authentication" },
                        authHeaderName = new { type = "string", description = "Header that carries apiKey for Header authentication, e.g. X-API-Key" },
                        authQueryParameter = new { type = "string", description = "Query parameter that carries apiKey for QueryParameter authentication, e.g. key" },
                        headers = new { type = "array", description = "Extra headers, at most 256 (replaces the stored list; omit to keep it). A header without a value keeps the stored value of the header with the same name.", items = new { type = "object", properties = new { name = new { type = "string" }, value = new { type = "string" } }, required = new[] { "name" } } },
                        dimensionality = new { type = "integer", description = "Embedding dimensionality hint" },
                        timeoutMs = new { type = "integer", description = "Per-request timeout in milliseconds" },
                        inferenceOptions = new { type = "object", description = "Inference defaults and enforced tool policy; omit on update to preserve.", properties = new {
                            toolsEnabled = new { type = "boolean", @default = true },
                            readOnlyTools = new { type = "boolean", @default = false, description = "Only built-in read-only tools; excludes shell, file changes and MCP." },
                            mcpToolsEnabled = new { type = "boolean", @default = true },
                            allowedTools = new { type = new[] { "array", "null" }, items = new { type = "string", minLength = 1, maxLength = 256 }, maxItems = 256, uniqueItems = true, description = "Exact case-sensitive names; null permits all otherwise eligible tools, [] none." },
                            maxIterations = new { type = "integer", minimum = 1, maximum = 1000, @default = 100 },
                            maxTokens = new { type = new[] { "integer", "null" }, minimum = 1, maximum = 10000000 },
                            temperature = new { type = new[] { "number", "null" }, minimum = 0, maximum = 2 },
                            topP = new { type = new[] { "number", "null" }, minimum = 0, maximum = 1 },
                            requireHarbor = new { type = "boolean", @default = false }
                        } },
                        enabled = new { type = "boolean", description = "Whether the endpoint is enabled" }
                    },
                    required = new[] { "endpointId" }
                },
                async (args) =>
                {
                    ModelEndpointUpsertArgs request = JsonSerializer.Deserialize<ModelEndpointUpsertArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ModelEndpointUpsertArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();

                    if (String.IsNullOrWhiteSpace(request.EndpointId)) return (object)McpToolError.InvalidArgument("endpointId is required.");

                    ModelEndpoint? existing = await endpoints.ReadAsync(auth, request.EndpointId).ConfigureAwait(false);
                    if (existing == null) return (object)McpToolError.NotFound("Model endpoint not found");

                    if (!String.IsNullOrWhiteSpace(request.Name)) existing.Name = request.Name;
                    if (request.Kind.HasValue) existing.Kind = request.Kind.Value;
                    if (request.Provider.HasValue) existing.Provider = request.Provider.Value;
                    if (request.BaseUrl != null) existing.BaseUrl = request.BaseUrl;
                    if (request.Model != null) existing.Model = request.Model;
                    if (request.Dimensionality.HasValue) existing.Dimensionality = request.Dimensionality.Value;
                    if (request.TimeoutMs.HasValue) existing.TimeoutMs = request.TimeoutMs.Value;
                    if (request.InferenceOptions != null) existing.InferenceOptions = request.InferenceOptions;
                    if (request.Enabled.HasValue) existing.Enabled = request.Enabled.Value;
                    // Through the write-only input, so the service sees the key as supplied (a plain set was ignored).
                    if (request.ApiKey != null) existing.ApiKeyInput = request.ApiKey;
                    if (request.AuthType.HasValue) existing.AuthType = request.AuthType.Value;
                    if (request.AuthUsername != null) existing.AuthUsername = request.AuthUsername;
                    if (request.AuthHeaderName != null) existing.AuthHeaderName = request.AuthHeaderName;
                    if (request.AuthQueryParameter != null) existing.AuthQueryParameter = request.AuthQueryParameter;
                    if (request.Headers != null) existing.Headers = request.Headers;

                    try
                    {
                        return (object)await endpoints.UpdateAsync(auth, existing).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                    {
                        return (object)McpToolError.FromException(ex);
                    }
                });

            register("duplicate_model_endpoint", "Create an edited copy of an editable endpoint. Omitted apiKey and null extra-header values inherit stored secrets server-side; secrets are never returned. Does not call the provider.",
                new { type = "object", properties = new {
                    endpointId = new { type = "string", description = "Editable source endpoint ID" },
                    configuration = new { type = "object", description = "Complete edited configuration as accepted by create_model_endpoint: name, provider, kind, baseUrl, model, authentication, headers, inferenceOptions, scope and enabled. Omit apiKey to inherit; null header values inherit by name." }
                }, required = new[] { "endpointId", "configuration" } }, async args =>
                {
                    try
                    {
                        ModelEndpointDuplicateArgs request = JsonSerializer.Deserialize<ModelEndpointDuplicateArgs>(args!.Value, _JsonOptions)
                            ?? throw new ArgumentException("Source and configuration are required.");
                        return (object)await endpoints.DuplicateAsync(McpToolHelpers.ResolveCallerContext(), request.EndpointId, request.Configuration).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is KeyNotFoundException || ex is JsonException)
                    { return (object)McpToolError.FromException(ex); }
                });

            register(
                "delete_model_endpoint",
                "Delete a managed model endpoint by ID.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        endpointId = new { type = "string", description = "Model endpoint ID (mep_ prefix)" }
                    },
                    required = new[] { "endpointId" }
                },
                async (args) =>
                {
                    ModelEndpointIdArgs request = JsonSerializer.Deserialize<ModelEndpointIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ModelEndpointIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    try
                    {
                        await endpoints.DeleteAsync(auth, request.EndpointId).ConfigureAwait(false);
                        return (object)new { Deleted = true, EndpointId = request.EndpointId };
                    }
                    catch (Exception ex)
                    {
                        return (object)McpToolError.FromException(ex);
                    }
                });

            register(
                "validate_model_endpoint",
                "Validate a model endpoint by issuing a real request (an embedding for embedding endpoints, a short completion for inference endpoints) and update its health status.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        endpointId = new { type = "string", description = "Model endpoint ID (mep_ prefix)" }
                    },
                    required = new[] { "endpointId" }
                },
                async (args) =>
                {
                    ModelEndpointIdArgs request = JsonSerializer.Deserialize<ModelEndpointIdArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ModelEndpointIdArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();
                    try
                    {
                        return (object)await endpoints.ValidateAsync(auth, request.EndpointId).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        return (object)McpToolError.FromException(ex);
                    }
                });

            register(
                "health_check_model_endpoints",
                "Run a health sweep across all enabled model endpoints, deduplicated by base URL, and refresh their health status. Returns the number of distinct base URLs probed.",
                new
                {
                    type = "object",
                    properties = new { }
                },
                async (args) =>
                {
                    int probed = await endpoints.CheckHealthAllAsync(McpToolHelpers.ResolveCallerContext()).ConfigureAwait(false);
                    return (object)new ModelEndpointHealthSweepResponse { DistinctBaseUrlsProbed = probed };
                });
        }
    }
}
