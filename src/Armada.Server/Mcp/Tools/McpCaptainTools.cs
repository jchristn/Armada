namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using Armada.Server;

    /// <summary>
    /// Registers MCP tools for captain operations (get, create, update, stop, delete, log).
    /// </summary>
    public static class McpCaptainTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers captain MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for captain data access.</param>
        /// <param name="admiral">Admiral service for captain orchestration.</param>
        /// <param name="settings">Armada settings, or null if unavailable.</param>
        /// <param name="onStopCaptain">Optional callback invoked when a captain is stopped.</param>
        /// <param name="agentLifecycle">Optional lifecycle handler used for model validation.</param>
        /// <param name="captainToolService">Optional captain tool availability service.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, IAdmiralService admiral, ArmadaSettings? settings, Func<string, Task>? onStopCaptain = null, AgentLifecycleHandler? agentLifecycle = null, CaptainToolService? captainToolService = null)
        {
            register(
                "get_captain",
                "Get details of a specific captain (AI agent)",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" }
                    },
                    required = new[] { "captainId" }
                },
                async (args) =>
                {
                    CaptainIdArgs request = JsonSerializer.Deserialize<CaptainIdArgs>(args!.Value, _JsonOptions)!;
                    string captainId = request.CaptainId;
                    Captain? captain = await database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                    if (captain == null) return (object)new { Error = "Captain not found" };
                    return (object)captain;
                });

            register(
                "release_captain",
                "Lift a captain's quarantine, returning it to the Idle pool so tier selection can hand it work again.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" }
                    },
                    required = new[] { "captainId" }
                },
                async (args) =>
                {
                    CaptainIdArgs request = JsonSerializer.Deserialize<CaptainIdArgs>(args!.Value, _JsonOptions)!;
                    Captain? captain = await database.Captains.ReadAsync(request.CaptainId).ConfigureAwait(false);
                    if (captain == null) return (object)new { Error = "Captain not found" };
                    if (captain.State != CaptainStateEnum.Quarantined)
                        return (object)new { Status = "not_quarantined", CaptainId = captain.Id };

                    captain.State = CaptainStateEnum.Idle;
                    captain.QuarantineUntilUtc = null;
                    captain.QuarantineReason = null;
                    captain.LastUpdateUtc = DateTime.UtcNow;
                    captain = await database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                    return (object)captain;
                });

            if (captainToolService != null)
            {
                register(
                    "get_captain_tools",
                    "Describe the runtime-visible tool sources and named tools available to a specific captain.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" }
                        },
                        required = new[] { "captainId" }
                    },
                    async (args) =>
                    {
                        CaptainIdArgs request = JsonSerializer.Deserialize<CaptainIdArgs>(args!.Value, _JsonOptions)!;
                        string captainId = request.CaptainId;
                        Captain? captain = await database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                        if (captain == null) return (object)new { Error = "Captain not found" };
                        return await captainToolService.DescribeAsync(captain).ConfigureAwait(false);
                    });
            }

            register(
                "create_captain",
                "Register a new captain (AI agent)",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Captain display name" },
                        runtime = new { type = "string", description = "Agent runtime: ClaudeCode, Codex, Gemini, Cursor, Mux, or Custom" },
                        systemInstructions = new { type = "string", description = "System instructions for this captain -- injected into every mission prompt to specialize behavior" },
                        model = new { type = "string", description = "AI model identifier; null means runtime default" },
                        reasoningEffort = new { type = "string", description = "Reasoning effort: Off, Minimal, Low, Medium, or High. Translated per runtime (Claude thinking budget, Codex reasoning effort, Mux --effort). Ignored by runtimes without a control." },
                        tier = new { type = "string", description = "Capability tier for dispatch routing: Economy, Standard, or Premium. Empty auto-classifies from the model name." },
                        allowedPersonas = new { type = "string", description = "JSON array of persona names this captain can fill, e.g. [\"Worker\",\"Judge\"]. Null means any persona." },
                        preferredPersona = new { type = "string", description = "Preferred persona for dispatch routing priority" },
                        muxConfigDirectory = new { type = "string", description = "Optional Mux config directory override" },
                        muxEndpoint = new { type = "string", description = "Named Mux endpoint for this captain" },
                        muxBaseUrl = new { type = "string", description = "Optional Mux base URL override" },
                        muxAdapterType = new { type = "string", description = "Optional Mux adapter type override" },
                        muxTemperature = new { type = "number", description = "Optional Mux temperature override" },
                        muxMaxTokens = new { type = "integer", description = "Optional Mux max tokens override" },
                        muxSystemPromptPath = new { type = "string", description = "Optional Mux system prompt file path" },
                        muxApprovalPolicy = new { type = "string", description = "Optional Mux approval policy override" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    CaptainCreateArgs request = JsonSerializer.Deserialize<CaptainCreateArgs>(args!.Value, _JsonOptions)!;
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    Captain captain = new Captain();
                    captain.TenantId = String.IsNullOrEmpty(caller.TenantId) ? ArmadaConstants.DefaultTenantId : caller.TenantId;
                    captain.UserId = caller.UserId;
                    captain.Name = request.Name;
                    if (!String.IsNullOrEmpty(request.Runtime) && Enum.TryParse<AgentRuntimeEnum>(request.Runtime, true, out AgentRuntimeEnum rt))
                        captain.Runtime = rt;
                    captain.SystemInstructions = request.SystemInstructions;
                    captain.Model = String.IsNullOrWhiteSpace(request.Model) ? null : request.Model;
                    captain.AllowedPersonas = request.AllowedPersonas;
                    captain.PreferredPersona = request.PreferredPersona;
                    captain.ReasoningEffort = ParseReasoningEffort(request.ReasoningEffort);
                    captain.Tier = ParseTier(request.Tier);
                    ApplyMuxOptions(captain, request);

                    if (agentLifecycle != null)
                    {
                        string? validationError = await agentLifecycle.ValidateCaptainModelAsync(captain).ConfigureAwait(false);
                        if (validationError != null) return CreateToolErrorResponse(validationError);
                    }

                    captain = await database.Captains.CreateAsync(captain).ConfigureAwait(false);
                    return (object)captain;
                });

            register(
                "update_captain",
                "Update a captain's name or runtime. Operational fields (state, process, mission) are preserved.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" },
                        name = new { type = "string", description = "New display name" },
                        runtime = new { type = "string", description = "New agent runtime: ClaudeCode, Codex, Gemini, Cursor, Mux, or Custom" },
                        systemInstructions = new { type = "string", description = "New system instructions for this captain" },
                        model = new { type = "string", description = "New AI model identifier; null means runtime default" },
                        reasoningEffort = new { type = "string", description = "Reasoning effort: Off, Minimal, Low, Medium, or High. Empty string clears it. Translated per runtime (Claude thinking budget, Codex reasoning effort, Mux --effort)." },
                        tier = new { type = "string", description = "Capability tier: Economy, Standard, or Premium. Empty string clears it (auto-classify from model)." },
                        allowedPersonas = new { type = "string", description = "JSON array of persona names this captain can fill, e.g. [\"Worker\",\"Judge\"]. Null means any persona." },
                        preferredPersona = new { type = "string", description = "Preferred persona for dispatch routing priority" },
                        muxConfigDirectory = new { type = "string", description = "Optional Mux config directory override; empty string clears it" },
                        muxEndpoint = new { type = "string", description = "Named Mux endpoint; empty string clears it" },
                        muxBaseUrl = new { type = "string", description = "Optional Mux base URL override; empty string clears it" },
                        muxAdapterType = new { type = "string", description = "Optional Mux adapter type override; empty string clears it" },
                        muxTemperature = new { type = "number", description = "Optional Mux temperature override" },
                        muxMaxTokens = new { type = "integer", description = "Optional Mux max tokens override" },
                        muxSystemPromptPath = new { type = "string", description = "Optional Mux system prompt file path; empty string clears it" },
                        muxApprovalPolicy = new { type = "string", description = "Optional Mux approval policy override; empty string clears it" }
                    },
                    required = new[] { "captainId" }
                },
                async (args) =>
                {
                    CaptainUpdateArgs request = JsonSerializer.Deserialize<CaptainUpdateArgs>(args!.Value, _JsonOptions)!;
                    string captainId = request.CaptainId;
                    Captain? captain = await database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                    if (captain == null) return (object)new { Error = "Captain not found" };
                    if (request.Name != null)
                        captain.Name = request.Name;
                    if (!String.IsNullOrEmpty(request.Runtime) && Enum.TryParse<AgentRuntimeEnum>(request.Runtime, true, out AgentRuntimeEnum rt))
                        captain.Runtime = rt;
                    if (request.SystemInstructions != null)
                        captain.SystemInstructions = request.SystemInstructions;
                    if (request.Model != null)
                        captain.Model = String.IsNullOrWhiteSpace(request.Model) ? null : request.Model;
                    if (request.AllowedPersonas != null)
                        captain.AllowedPersonas = request.AllowedPersonas;
                    if (request.PreferredPersona != null)
                        captain.PreferredPersona = request.PreferredPersona;
                    if (request.ReasoningEffort != null)
                        captain.ReasoningEffort = ParseReasoningEffort(request.ReasoningEffort);
                    if (request.Tier != null)
                        captain.Tier = ParseTier(request.Tier);
                    try
                    {
                        ApplyMuxOptions(captain, request, captain.Runtime == AgentRuntimeEnum.Mux ? captain : null);
                    }
                    catch (Exception ex)
                    {
                        return CreateToolErrorResponse(ex.Message);
                    }

                    if (agentLifecycle != null)
                    {
                        string? validationError = await agentLifecycle.ValidateCaptainModelAsync(captain).ConfigureAwait(false);
                        if (validationError != null) return CreateToolErrorResponse(validationError);
                    }

                    captain.LastUpdateUtc = DateTime.UtcNow;
                    captain = await database.Captains.UpdateAsync(captain).ConfigureAwait(false);
                    return (object)captain;
                });

            register(
                "stop_captain",
                "Stop a specific captain agent, killing its process and recalling it to idle state",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" }
                    },
                    required = new[] { "captainId" }
                },
                async (args) =>
                {
                    CaptainIdArgs request = JsonSerializer.Deserialize<CaptainIdArgs>(args!.Value, _JsonOptions)!;
                    string captainId = request.CaptainId;
                    if (onStopCaptain != null)
                        await onStopCaptain(captainId).ConfigureAwait(false);
                    await admiral.RecallCaptainAsync(captainId).ConfigureAwait(false);
                    return (object)new { Status = "stopped", CaptainId = captainId };
                });

            register(
                "stop_all",
                "Emergency stop all running captains",
                new { type = "object", properties = new { } },
                async (args) =>
                {
                    await admiral.RecallAllAsync().ConfigureAwait(false);
                    return (object)new { Status = "all_stopped" };
                });

            register(
                "delete_captain",
                "Delete a captain. If working, the captain is recalled first.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" }
                    },
                    required = new[] { "captainId" }
                },
                async (args) =>
                {
                    CaptainIdArgs request = JsonSerializer.Deserialize<CaptainIdArgs>(args!.Value, _JsonOptions)!;
                    string captainId = request.CaptainId;
                    Captain? captain = await database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                    if (captain == null) return (object)new { Error = "Captain not found" };

                    // Block deletion of working captains
                    if (captain.State == CaptainStateEnum.Working)
                        return (object)new { Error = "Cannot delete captain while state is Working. Stop the captain first." };

                    // Block deletion if captain has active missions
                    List<Mission> captainMissions = await database.Missions.EnumerateByCaptainAsync(captainId).ConfigureAwait(false);
                    int activeMissionCount = captainMissions.Count(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress);
                    if (activeMissionCount > 0)
                        return (object)new { Error = "Cannot delete captain with " + activeMissionCount + " active mission(s) in Assigned or InProgress status. Cancel or complete them first." };

                    await database.Captains.DeleteAsync(captainId).ConfigureAwait(false);
                    return (object)new { Status = "deleted", CaptainId = captainId };
                });

            register(
                "delete_captains",
                "Permanently delete multiple captains from the database by ID. Captains that are Working or have active missions are skipped. Returns a summary of deleted and skipped entries. This cannot be undone.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        ids = new { type = "array", items = new { type = "string" }, description = "List of captain IDs to delete (cpt_ prefix)" }
                    },
                    required = new[] { "ids" }
                },
                async (args) =>
                {
                    DeleteMultipleArgs request = JsonSerializer.Deserialize<DeleteMultipleArgs>(args!.Value, _JsonOptions)!;
                    if (request.Ids == null || request.Ids.Count == 0)
                        return (object)new { Error = "ids is required and must not be empty" };

                    DeleteMultipleResult result = new DeleteMultipleResult();
                    foreach (string id in request.Ids)
                    {
                        if (String.IsNullOrEmpty(id))
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id ?? "", "Empty ID"));
                            continue;
                        }
                        Captain? captain = await database.Captains.ReadAsync(id).ConfigureAwait(false);
                        if (captain == null)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Not found"));
                            continue;
                        }
                        if (captain.State == CaptainStateEnum.Working)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete captain while state is Working. Stop the captain first."));
                            continue;
                        }
                        List<Mission> captainMissions = await database.Missions.EnumerateByCaptainAsync(id).ConfigureAwait(false);
                        int activeMissionCount = captainMissions.Count(m => m.Status == MissionStatusEnum.Assigned || m.Status == MissionStatusEnum.InProgress);
                        if (activeMissionCount > 0)
                        {
                            result.Skipped.Add(new DeleteMultipleSkipped(id, "Cannot delete captain with " + activeMissionCount + " active mission(s). Cancel or complete them first."));
                            continue;
                        }
                        await database.Captains.DeleteAsync(id).ConfigureAwait(false);
                        result.Deleted++;
                    }
                    result.ResolveStatus();
                    return (object)result;
                });

            // Captain log requires settings
            if (settings != null)
            {
                register(
                    "get_captain_log",
                    "Get the current session log for a captain. Supports pagination.",
                    new
                    {
                        type = "object",
                        properties = new
                        {
                            captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" },
                            lines = new { type = "integer", description = "Number of lines to return (default 100)" },
                            offset = new { type = "integer", description = "Line offset to start from (default 0)" }
                        },
                        required = new[] { "captainId" }
                    },
                    async (args) =>
                    {
                        CaptainLogArgs request = JsonSerializer.Deserialize<CaptainLogArgs>(args!.Value, _JsonOptions)!;
                        string captainId = request.CaptainId;
                        Captain? captain = await database.Captains.ReadAsync(captainId).ConfigureAwait(false);
                        if (captain == null) return (object)new { Error = "Captain not found" };

                        string pointerPath = Path.Combine(settings.LogDirectory, "captains", captainId + ".current");
                        string? logPath = null;

                        if (File.Exists(pointerPath))
                        {
                            string target = (await McpToolHelpers.ReadTextFileSafeAsync(pointerPath).ConfigureAwait(false)).Trim();
                            if (File.Exists(target))
                                logPath = target;
                        }

                        if (logPath == null)
                            return (object)new { CaptainId = captainId, Log = "", Lines = 0, TotalLines = 0 };

                        string[] allLines = await McpToolHelpers.ReadLogFileSafeAsync(logPath).ConfigureAwait(false);
                        int totalLines = allLines.Length;

                        int offset = Math.Max(0, request.Offset ?? 0);
                        int lineCount = Math.Max(1, request.Lines ?? 100);

                        string[] slice = allLines.Skip(offset).Take(lineCount).ToArray();
                        string log = String.Join("\n", slice);
                        return (object)new { CaptainId = captainId, Log = log, Lines = slice.Length, TotalLines = totalLines };
                    });
            }
        }

        private static object CreateToolErrorResponse(string error)
        {
            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = error
                    }
                },
                isError = true
            };
        }

        private static ReasoningEffortEnum? ParseReasoningEffort(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return Enum.TryParse<ReasoningEffortEnum>(value.Trim(), true, out ReasoningEffortEnum parsed)
                ? parsed
                : (ReasoningEffortEnum?)null;
        }

        private static CaptainTierEnum? ParseTier(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            return Enum.TryParse<CaptainTierEnum>(value.Trim(), true, out CaptainTierEnum parsed)
                ? parsed
                : (CaptainTierEnum?)null;
        }

        private static void ApplyMuxOptions(Captain captain, CaptainCreateArgs request)
        {
            if (captain.Runtime != AgentRuntimeEnum.Mux)
            {
                captain.RuntimeOptionsJson = null;
                return;
            }

            MuxCaptainOptions options = new MuxCaptainOptions
            {
                ConfigDirectory = request.MuxConfigDirectory,
                Endpoint = request.MuxEndpoint,
                BaseUrl = request.MuxBaseUrl,
                AdapterType = request.MuxAdapterType,
                Temperature = request.MuxTemperature,
                MaxTokens = request.MuxMaxTokens,
                SystemPromptPath = request.MuxSystemPromptPath,
                ApprovalPolicy = request.MuxApprovalPolicy
            };

            captain.RuntimeOptionsJson = CaptainRuntimeOptions.Serialize(options);
        }

        private static void ApplyMuxOptions(Captain captain, CaptainUpdateArgs request, Captain? existingCaptain)
        {
            if (captain.Runtime != AgentRuntimeEnum.Mux)
            {
                captain.RuntimeOptionsJson = null;
                return;
            }

            MuxCaptainOptions options = GetExistingMuxOptions(existingCaptain);

            if (request.MuxConfigDirectory != null) options.ConfigDirectory = request.MuxConfigDirectory;
            if (request.MuxEndpoint != null) options.Endpoint = request.MuxEndpoint;
            if (request.MuxBaseUrl != null) options.BaseUrl = request.MuxBaseUrl;
            if (request.MuxAdapterType != null) options.AdapterType = request.MuxAdapterType;
            if (request.MuxTemperature.HasValue) options.Temperature = request.MuxTemperature;
            if (request.MuxMaxTokens.HasValue) options.MaxTokens = request.MuxMaxTokens;
            if (request.MuxSystemPromptPath != null) options.SystemPromptPath = request.MuxSystemPromptPath;
            if (request.MuxApprovalPolicy != null) options.ApprovalPolicy = request.MuxApprovalPolicy;

            captain.RuntimeOptionsJson = CaptainRuntimeOptions.Serialize(options);
        }

        private static MuxCaptainOptions GetExistingMuxOptions(Captain? captain)
        {
            if (captain == null || captain.Runtime != AgentRuntimeEnum.Mux || String.IsNullOrWhiteSpace(captain.RuntimeOptionsJson))
            {
                return new MuxCaptainOptions();
            }

            return CaptainRuntimeOptions.GetMuxOptions(captain)
                ?? new MuxCaptainOptions();
        }
    }
}
