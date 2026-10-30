namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.BlackBox;
    using Armada.Core.BlackBox.Storage;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// MCP resources and prompts for the black box (MISSION_BLACKBOX_PLAN.md section 8.2), on Voltaic's resource
    /// template and prompt support. Resources: <c>armada://blackbox/{entryId}/brief.md</c>,
    /// <c>armada://blackbox/{entryId}/record.json</c>, <c>armada://blackbox/{entryId}/objects/{name}</c> and
    /// <c>armada://missions/{missionId}/blackbox</c> (the mission's current entry digest). Prompts:
    /// <c>review_blackbox(missionId, goal)</c> and <c>explain_blackbox(missionId)</c>, which expand to the section 9
    /// instructions. Every read runs as the MCP caller through <see cref="IBlackBoxService"/>, so an entry the caller
    /// cannot see is not found.
    /// </summary>
    public static class McpBlackBoxResources
    {
        #region Public-Members

        /// <summary>
        /// Resource URI templates, in registration order.
        /// </summary>
        public static readonly IReadOnlyList<string> UriTemplates = new List<string>
        {
            "armada://blackbox/{entryId}/brief.md",
            "armada://blackbox/{entryId}/record.json",
            "armada://blackbox/{entryId}/objects/{name}",
            "armada://missions/{missionId}/blackbox"
        };

        /// <summary>
        /// Prompt names, in registration order.
        /// </summary>
        public static readonly IReadOnlyList<string> PromptNames = new List<string> { "review_blackbox", "explain_blackbox" };

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static readonly JsonSerializerOptions _ReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the resource templates and prompts on an MCP server.
        /// </summary>
        /// <param name="server">MCP HTTP server.</param>
        /// <param name="service">Black box service.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static void Register(McpHttpServer server, IBlackBoxService service)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            if (service == null) throw new ArgumentNullException(nameof(service));

            server.RegisterResourceTemplate(
                new McpResourceTemplate
                {
                    UriTemplate = UriTemplates[0],
                    Name = "blackbox_brief",
                    Title = "Black box brief",
                    Description = "The FIX THIS brief of a black box entry, as Markdown.",
                    MimeType = "text/markdown"
                },
                (uri, variables, token) => GuardAsync(uri, async () =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    string brief = await service.GetBriefAsync(caller, Variable(variables, "entryId"), false, token).ConfigureAwait(false);
                    return Text(uri, "text/markdown", brief);
                }));

            server.RegisterResourceTemplate(
                new McpResourceTemplate
                {
                    UriTemplate = UriTemplates[1],
                    Name = "blackbox_record",
                    Title = "Black box record",
                    Description = "The versioned record (record.json) of a black box entry.",
                    MimeType = "application/json"
                },
                (uri, variables, token) => GuardAsync(uri, async () =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxRecord record = await service.GetRecordAsync(caller, Variable(variables, "entryId"), token).ConfigureAwait(false);
                    return Text(uri, "application/json", JsonSerializer.Serialize(record, BlackBoxJson.Options));
                }));

            server.RegisterResourceTemplate(
                new McpResourceTemplate
                {
                    UriTemplate = UriTemplates[2],
                    Name = "blackbox_object",
                    Title = "Black box object",
                    Description = "The first 64 KB of one object of a black box entry (URL-encode a name with a folder, for example evidence%2Fgit-status.txt). Page a larger object with the read_blackbox_object tool.",
                    MimeType = "text/plain"
                },
                (uri, variables, token) => GuardAsync(uri, async () =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BlackBoxObjectChunk chunk = await service.ReadObjectChunkAsync(caller, Variable(variables, "entryId"), Variable(variables, "name"), 0, null, token).ConfigureAwait(false);
                    if (chunk.Encoding == "base64")
                    {
                        McpReadResourceResult blob = new McpReadResourceResult();
                        blob.Contents.Add(new McpBlobResourceContents { Uri = uri, MimeType = chunk.ContentType, Blob = chunk.Content });
                        return blob;
                    }

                    string text = chunk.NextOffset.HasValue
                        ? chunk.Content + "\n\n[first " + chunk.Length + " of " + chunk.TotalBytes + " bytes; read the rest with read_blackbox_object offset " + chunk.NextOffset.Value + "]\n"
                        : chunk.Content;
                    return Text(uri, chunk.ContentType, text);
                }));

            server.RegisterResourceTemplate(
                new McpResourceTemplate
                {
                    UriTemplate = UriTemplates[3],
                    Name = "mission_blackbox",
                    Title = "Mission black box",
                    Description = "The summary (digest) of a mission's current black box entry: where, how and why it failed, triage and the top recovery paths.",
                    MimeType = "application/json"
                },
                (uri, variables, token) => GuardAsync(uri, async () =>
                {
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    string entryId = await service.ResolveEntryIdAsync(caller, Variable(variables, "missionId"), null, null, token).ConfigureAwait(false);
                    BlackBoxDigest digest = await service.GetDigestAsync(caller, entryId, token).ConfigureAwait(false);
                    return Text(uri, "application/json", JsonSerializer.Serialize(digest, _JsonOptions));
                }));

            server.RegisterPrompt(
                new McpPrompt
                {
                    Name = PromptNames[0],
                    Title = "Review a black box",
                    Description = "Review the black box of a failed mission and take action toward a goal (explain, fix, prevent, fix Armada, compare or clean up), using only the recorded recovery paths and recommendations.",
                    Arguments = new List<McpPromptArgument>
                    {
                        new McpPromptArgument { Name = "missionId", Description = "Mission ID (msn_ prefix)", Required = true },
                        new McpPromptArgument { Name = "goal", Description = "What to do: explain, fix, prevent, fix Armada, compare or clean up (default explain)", Required = false }
                    }
                },
                (parameters, token) => Task.FromResult(Prompt("Review the black box of mission " + Argument(parameters, "missionId"), ReviewText(Argument(parameters, "missionId"), Argument(parameters, "goal")))));

            server.RegisterPrompt(
                new McpPrompt
                {
                    Name = PromptNames[1],
                    Title = "Explain a black box",
                    Description = "Explain why a mission failed from its black box, with evidence and opinion clearly separated.",
                    Arguments = new List<McpPromptArgument>
                    {
                        new McpPromptArgument { Name = "missionId", Description = "Mission ID (msn_ prefix)", Required = true }
                    }
                },
                (parameters, token) => Task.FromResult(Prompt("Explain the black box of mission " + Argument(parameters, "missionId"), ExplainText(Argument(parameters, "missionId")))));
        }

        /// <summary>
        /// The JSON-RPC error code MCP defines for a resource that does not exist (<c>-32002</c>).
        /// </summary>
        public const int ResourceNotFoundCode = -32002;

        /// <summary>
        /// Map a black box error raised while reading a resource to a typed JSON-RPC error, or null when the exception is
        /// not a black box error. A missing or invisible entry or object is <see cref="ResourceNotFoundCode"/> (the MCP
        /// resource-not-found convention); any other black box error is invalid params (<c>-32602</c>). The data carries
        /// the uri and the same typed error a tool returns (ErrorCode and Code), so a client handles both alike.
        /// </summary>
        /// <param name="uri">Resource uri.</param>
        /// <param name="ex">Exception.</param>
        /// <returns>Protocol error, or null.</returns>
        public static McpProtocolException? ToResourceError(string? uri, Exception? ex)
        {
            McpToolError? error = McpBlackBoxTools.ToToolError(ex);
            if (error == null) return null;
            BlackBoxResourceErrorData data = new BlackBoxResourceErrorData
            {
                Uri = uri ?? String.Empty,
                Error = error.Error,
                ErrorCode = error.ErrorCode,
                Code = error.Code
            };
            int code = error.ErrorCode == McpToolErrorCodeEnum.NotFound ? ResourceNotFoundCode : -32602;
            return new McpProtocolException(code, error.Error, data);
        }

        /// <summary>
        /// The text the review_blackbox prompt expands to.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <param name="goal">Goal, or null for explain.</param>
        /// <returns>Prompt text.</returns>
        public static string ReviewText(string? missionId, string? goal)
        {
            string mission = String.IsNullOrWhiteSpace(missionId) ? "the mission" : missionId!.Trim();
            string target = String.IsNullOrWhiteSpace(goal) ? "explain it" : goal!.Trim();
            StringBuilder sb = new StringBuilder();
            sb.Append("Review the black box for mission ").Append(mission).Append(" and ").Append(target).Append(".\n\n");
            sb.Append("1. Call get_blackbox {missionId: \"").Append(mission).Append("\", format: \"summary\"} first.\n");
            sb.Append("2. Read only the sections the goal needs with read_blackbox_section (Conflict, Workspace, Analysis), and page evidence with read_blackbox_object instead of loading it whole.\n");
            sb.Append("3. Map the goal to tools: explain (get_blackbox summary or brief, sections, objects); fix or recover (run_blackbox_recovery with a ranked path: dry run, then confirm); prevent (apply_blackbox_recommendation: dry run, then confirm); fix Armada (get_blackbox {format: brief} or get_blackbox_bundle, then a mission on the Armada vessel with that brief); compare (list_blackbox_entries {fingerprint}, search_blackbox); clean up (resolve_blackbox, pin_blackbox, delete_blackbox_entries).\n\n");
            sb.Append(McpBlackBoxTools.SafetyGuidance).Append('\n');
            return sb.ToString();
        }

        /// <summary>
        /// The text the explain_blackbox prompt expands to.
        /// </summary>
        /// <param name="missionId">Mission id.</param>
        /// <returns>Prompt text.</returns>
        public static string ExplainText(string? missionId)
        {
            string mission = String.IsNullOrWhiteSpace(missionId) ? "the mission" : missionId!.Trim();
            StringBuilder sb = new StringBuilder();
            sb.Append("Explain why mission ").Append(mission).Append(" failed, from its black box.\n\n");
            sb.Append("Call get_blackbox {missionId: \"").Append(mission).Append("\", format: \"summary\"} first, then read only the sections you need. Answer in this shape: what happened (where, how, status before and after), the evidence (quoted from the entry, with section or object names), the triage verdict and whether you agree, your opinion of why it failed and what could have been done differently (clearly marked as opinion), and the recorded recovery paths and recommendations. Do not run anything.\n\n");
            sb.Append(McpBlackBoxTools.SafetyGuidance).Append('\n');
            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static async Task<McpReadResourceResult> GuardAsync(string uri, Func<Task<McpReadResourceResult>> read)
        {
            try
            {
                return await read().ConfigureAwait(false);
            }
            catch (BlackBoxException ex)
            {
                McpProtocolException? mapped = ToResourceError(uri, ex);
                if (mapped == null) throw;
                throw mapped;
            }
        }

        private static string Variable(IReadOnlyDictionary<string, string> variables, string name)
        {
            if (variables == null || !variables.TryGetValue(name, out string? value) || String.IsNullOrWhiteSpace(value))
                throw new BlackBoxNotFoundException("The resource URI has no " + name + ".");
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (UriFormatException)
            {
                return value;
            }
        }

        private static string? Argument(RpcParameters? parameters, string name)
        {
            if (parameters == null || !parameters.HasValue || String.IsNullOrEmpty(parameters.RawJson)) return null;
            BlackBoxPromptArgs? args;
            try
            {
                args = JsonSerializer.Deserialize<BlackBoxPromptArgs>(parameters.RawJson, _ReadOptions);
            }
            catch (JsonException)
            {
                return null;
            }

            if (args == null) return null;
            BlackBoxPromptArgs source = args.Arguments ?? args;
            return name == "goal" ? source.Goal : source.MissionId;
        }

        private static McpReadResourceResult Text(string uri, string mimeType, string text)
        {
            McpReadResourceResult result = new McpReadResourceResult();
            result.Contents.Add(new McpTextResourceContents { Uri = uri, MimeType = mimeType, Text = text });
            return result;
        }

        private static McpGetPromptResult Prompt(string description, string text)
        {
            McpGetPromptResult result = new McpGetPromptResult { Description = description };
            result.Messages.Add(new McpPromptMessage { Role = "user", Content = new McpTextContent { Text = text } });
            return result;
        }

        #endregion
    }
}
