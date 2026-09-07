namespace Armada.Server.Mcp.Tools
{
    using System;
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
                    if (endpoint == null) return (object)new { Error = "Model endpoint not found" };
                    return (object)endpoint;
                });

            register(
                "create_model_endpoint",
                "Create a managed model endpoint. Kind must be Embedding or Inference; Provider is one of Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, VoyageAI. Anthropic cannot be used for Embedding and VoyageAI cannot be used for Inference.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Human-facing endpoint name" },
                        kind = new { type = "string", description = "Embedding or Inference" },
                        provider = new { type = "string", description = "Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, or VoyageAI" },
                        baseUrl = new { type = "string", description = "Base URL of the endpoint" },
                        model = new { type = "string", description = "Model identifier to request (optional)" },
                        apiKey = new { type = "string", description = "API key/credential (optional; stored but never returned)" },
                        dimensionality = new { type = "integer", description = "Optional embedding dimensionality hint" },
                        timeoutMs = new { type = "integer", description = "Per-request timeout in milliseconds (default 120000)" },
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
                    if (request.Enabled.HasValue) endpoint.Enabled = request.Enabled.Value;
                    if (request.ApiKey != null) endpoint.ApiKey = request.ApiKey;

                    try
                    {
                        return (object)await endpoints.CreateAsync(auth, endpoint).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                    {
                        return (object)new { Error = ex.Message };
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
                        kind = new { type = "string", description = "Embedding or Inference" },
                        provider = new { type = "string", description = "Ollama, OpenAI, OpenAICompatible, Anthropic, Gemini, or VoyageAI" },
                        baseUrl = new { type = "string", description = "Base URL of the endpoint" },
                        model = new { type = "string", description = "Model identifier to request" },
                        apiKey = new { type = "string", description = "API key/credential (omit to keep the stored key)" },
                        dimensionality = new { type = "integer", description = "Embedding dimensionality hint" },
                        timeoutMs = new { type = "integer", description = "Per-request timeout in milliseconds" },
                        enabled = new { type = "boolean", description = "Whether the endpoint is enabled" }
                    },
                    required = new[] { "endpointId" }
                },
                async (args) =>
                {
                    ModelEndpointUpsertArgs request = JsonSerializer.Deserialize<ModelEndpointUpsertArgs>(args!.Value, _JsonOptions)
                        ?? throw new InvalidOperationException("Could not deserialize ModelEndpointUpsertArgs.");
                    AuthContext auth = McpToolHelpers.ResolveCallerContext();

                    if (String.IsNullOrWhiteSpace(request.EndpointId)) return (object)new { Error = "endpointId is required." };

                    ModelEndpoint? existing = await endpoints.ReadAsync(auth, request.EndpointId).ConfigureAwait(false);
                    if (existing == null) return (object)new { Error = "Model endpoint not found" };

                    if (!String.IsNullOrWhiteSpace(request.Name)) existing.Name = request.Name;
                    if (request.Kind.HasValue) existing.Kind = request.Kind.Value;
                    if (request.Provider.HasValue) existing.Provider = request.Provider.Value;
                    if (request.BaseUrl != null) existing.BaseUrl = request.BaseUrl;
                    if (request.Model != null) existing.Model = request.Model;
                    if (request.Dimensionality.HasValue) existing.Dimensionality = request.Dimensionality.Value;
                    if (request.TimeoutMs.HasValue) existing.TimeoutMs = request.TimeoutMs.Value;
                    if (request.Enabled.HasValue) existing.Enabled = request.Enabled.Value;
                    if (request.ApiKey != null) existing.ApiKey = request.ApiKey;

                    try
                    {
                        return (object)await endpoints.UpdateAsync(auth, existing).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                    {
                        return (object)new { Error = ex.Message };
                    }
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
                        return (object)new { Error = ex.Message };
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
                        return (object)new { Error = ex.Message };
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
                    int probed = await endpoints.CheckHealthAllAsync().ConfigureAwait(false);
                    return (object)new ModelEndpointHealthSweepResponse { DistinctBaseUrlsProbed = probed };
                });
        }
    }
}
