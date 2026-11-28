namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers the fixture battery MCP tools: list_battery_packs, run_battery, get_battery_run, list_battery_runs, cancel_battery_run and list_battery_results. Every tool enforces
    /// the same rules as REST through <see cref="BatteryAccessService"/>; its typed exceptions become typed tool errors.
    /// </summary>
    public static class McpBatteryTools
    {
        #region Private-Members

        private const string UnavailableMessage = "The fixture battery is not available on this server.";

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the tools. When <paramref name="service"/> is null every tool is still registered and answers a typed
        /// Unavailable error (the standalone stdio server), so the tool list matches the HTTP server's.
        /// </summary>
        /// <param name="register">Registration delegate.</param>
        /// <param name="service">Battery access service, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="register"/> is null.</exception>
        public static void Register(RegisterToolDelegate register, BatteryAccessService? service)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            register(
                "list_battery_packs",
                "List the fixture battery packs (each a fixture repository plus its tasks, defined as data), with each task's persona, grader (command, byteComparison, seededSet, mutation, rubric) and toolchain. Malformed packs are listed with valid false and their validation errors. Returns an enumeration result (objects, totalRecords, totalPages).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        pageNumber = new { type = "integer", description = "Page number (default 1)" },
                        pageSize = new { type = "integer", description = "Page size (1-1000, default 100)" }
                    }
                },
                (args) =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BatteryPackListArgs request = Read<BatteryPackListArgs>(args);
                    return Task.FromResult((object)service.ListPacks(caller, request.PageNumber, request.PageSize));
                });

            register("get_battery_pack", "Get battery pack. Writes require a global admin; paths cannot traverse or follow symlinks; text is limited to 1 MiB.",
                new { type = "object", properties = new { packId = new { type = "string" } }, required = new[] { "packId" } },
                args =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    string? id = PackArgument(args, "packId");
                    string? path = PackArgument(args, "path");
                    string? content = PackArgument(args, "content");
                    string? manifest = PackArgument(args, "manifestJson");
                    return Task.FromResult((object)service.Packs.GetPack(caller, id));
                });

            register("read_battery_pack_file", "Read battery pack text file. Writes require a global admin; paths cannot traverse or follow symlinks; text is limited to 1 MiB.",
                new { type = "object", properties = new { packId = new { type = "string" }, path = new { type = "string" } }, required = new[] { "packId", "path" } },
                args =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    string? id = PackArgument(args, "packId");
                    string? path = PackArgument(args, "path");
                    string? content = PackArgument(args, "content");
                    string? manifest = PackArgument(args, "manifestJson");
                    return Task.FromResult((object)service.Packs.ReadPackFile(caller, id, path));
                });

            register("write_battery_pack_file", "Write battery pack text file. Writes require a global admin; paths cannot traverse or follow symlinks; text is limited to 1 MiB.",
                new { type = "object", properties = new { packId = new { type = "string" }, path = new { type = "string" }, content = new { type = "string" } }, required = new[] { "packId", "path", "content" } },
                args =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    string? id = PackArgument(args, "packId");
                    string? path = PackArgument(args, "path");
                    string? content = PackArgument(args, "content");
                    string? manifest = PackArgument(args, "manifestJson");
                    return Task.FromResult((object)service.Packs.WritePackFile(caller, id, path, content));
                });

            register("delete_battery_pack_file", "Delete battery pack text file. Writes require a global admin; paths cannot traverse or follow symlinks; text is limited to 1 MiB.",
                new { type = "object", properties = new { packId = new { type = "string" }, path = new { type = "string" } }, required = new[] { "packId", "path" } },
                args =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    string? id = PackArgument(args, "packId");
                    string? path = PackArgument(args, "path");
                    string? content = PackArgument(args, "content");
                    string? manifest = PackArgument(args, "manifestJson");
                    return Task.FromResult((object)service.Packs.DeletePackFile(caller, id, path));
                });

            register("create_battery_pack", "Create battery pack. Writes require a global admin; paths cannot traverse or follow symlinks; text is limited to 1 MiB.",
                new { type = "object", properties = new { packId = new { type = "string" }, manifestJson = new { type = "string" } }, required = new[] { "packId", "manifestJson" } },
                args =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    string? id = PackArgument(args, "packId");
                    string? path = PackArgument(args, "path");
                    string? content = PackArgument(args, "content");
                    string? manifest = PackArgument(args, "manifestJson");
                    return Task.FromResult((object)service.Packs.CreatePack(caller, new BatteryPackEditRequest { PackId = id ?? String.Empty, ManifestJson = manifest ?? String.Empty }));
                });

            register("update_battery_pack_manifest", "Update battery pack manifest. Writes require a global admin; paths cannot traverse or follow symlinks; text is limited to 1 MiB.",
                new { type = "object", properties = new { packId = new { type = "string" }, manifestJson = new { type = "string" } }, required = new[] { "packId", "manifestJson" } },
                args =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    string? id = PackArgument(args, "packId");
                    string? path = PackArgument(args, "path");
                    string? content = PackArgument(args, "content");
                    string? manifest = PackArgument(args, "manifestJson");
                    return Task.FromResult((object)service.Packs.UpdatePackManifest(caller, id, manifest));
                });

            register("delete_battery_pack", "Delete battery pack. Writes require a global admin; paths cannot traverse or follow symlinks; text is limited to 1 MiB.",
                new { type = "object", properties = new { packId = new { type = "string" } }, required = new[] { "packId" } },
                args =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    string? id = PackArgument(args, "packId");
                    string? path = PackArgument(args, "path");
                    string? content = PackArgument(args, "content");
                    string? manifest = PackArgument(args, "manifestJson");
                    return Task.FromResult((object)service.Packs.DeletePack(caller, id));
                });

            register("validate_battery_pack", "Validate battery pack without writing. Writes require a global admin; paths cannot traverse or follow symlinks; text is limited to 1 MiB.",
                new { type = "object", properties = new { packId = new { type = "string" } }, required = new[] { "packId" } },
                args =>
                {
                    if (service == null) return Task.FromResult((object)McpToolError.Unavailable(UnavailableMessage));
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    string? id = PackArgument(args, "packId");
                    string? path = PackArgument(args, "path");
                    string? content = PackArgument(args, "content");
                    string? manifest = PackArgument(args, "manifestJson");
                    return Task.FromResult((object)service.Packs.ValidatePack(caller, id));
                });

            register(
                "run_battery",
                "Run fixture battery packs against a captain. Before each task the fixture is reset; the task is dispatched as a mission on the pack's fixture vessel under the built-in ModelEvaluation pipeline (pinned to the captain, no landing) and graded per its manifest. Returns the queued run at once; poll get_battery_run. runCount defaults to 3. A task whose toolchain the Harbor lacks is CouldNotRun, never Failed; rubric-graded tasks are PendingEvaluation. Battery missions never count in production stage outcomes. Needs a global admin or a tenant admin of the captain's tenant. Set createFixtureVessel to create a pack's fixture vessel the first time.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        packs = new { type = "array", items = new { type = "string" }, description = "Pack identifiers, for example [\"csharp\"]" },
                        captainId = new { type = "string", description = "Captain to run the tasks (cpt_ prefix)" },
                        runCount = new { type = "integer", description = "Runs of each task (1-10, default 3)" },
                        seed = new { type = "integer", description = "Seed to record with the run (default: picked at random)" },
                        createFixtureVessel = new { type = "boolean", description = "Create a pack's fixture vessel when it does not exist yet (default false)" },
                        promptVariant = new { type = "string", @enum = new[] { "Current", "Lean", "Compact" }, description = "Persona prompt variant every task renders with (default: the captain's own setting, else Current). Runs that differ only in this compare the variants." },
                        pipeline = new { type = "string", description = "Pipeline name to run each Worker task under as a full voyage (default: the built-in ModelEvaluation pipeline with captainId). Measures a mixed pipeline of models." },
                        captainOverrides = new
                        {
                            type = "array",
                            description = "Required with pipeline: the captain for each persona, for example [{\"persona\":\"Worker\",\"captainId\":\"cpt_...\"},{\"persona\":\"Judge\",\"captainId\":\"cpt_...\"}]",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    persona = new { type = "string", description = "Persona name" },
                                    captainId = new { type = "string", description = "Captain ID (cpt_ prefix)" }
                                },
                                required = new[] { "persona", "captainId" }
                            }
                        }
                    },
                    required = new[] { "packs", "captainId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BatteryRunArgs request = Read<BatteryRunArgs>(args);
                    BatteryRunRequest body = new BatteryRunRequest
                    {
                        Packs = request.Packs ?? new List<string>(),
                        CaptainId = request.CaptainId,
                        RunCount = request.RunCount,
                        Seed = request.Seed,
                        CreateFixtureVessel = request.CreateFixtureVessel ?? false,
                        PromptVariant = PromptVariantResolver.Parse(request.PromptVariant),
                        Pipeline = request.Pipeline,
                        CaptainOverrides = request.CaptainOverrides
                    };
                    return (object)await service.RunAsync(caller, body).ConfigureAwait(false);
                });

            register(
                "get_battery_run",
                "Get one fixture battery run visible to the caller: its provenance (seed, sampling settings, battery version, configuration revision) and outcome counts. The per-task results are not embedded: page them with list_battery_results.",
                new
                {
                    type = "object",
                    properties = new { id = new { type = "string", description = "Run ID (bat_ prefix)" } },
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BatteryRunIdArgs request = Read<BatteryRunIdArgs>(args);
                    return (object)await service.GetRunAsync(caller, request.Id).ConfigureAwait(false);
                });

            register(
                "list_battery_runs",
                "List one page of fixture battery runs visible to the caller, newest first, without their per-task results. A global admin sees every tenant (tenantId filters); everyone else their own tenant. Returns an enumeration result (objects, totalRecords, totalPages).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        tenantId = new { type = "string", description = "Tenant filter (global admins only)" },
                        captainId = new { type = "string", description = "Captain filter (cpt_ prefix)" },
                        status = new { type = "string", @enum = Enum.GetNames<BatteryRunStatusEnum>(), description = "Status filter" },
                        pageNumber = new { type = "integer", description = "Page number (default 1)" },
                        pageSize = new { type = "integer", description = "Page size (1-1000, default 25)" }
                    }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BatteryRunListArgs request = Read<BatteryRunListArgs>(args);
                    BatteryRunQuery query = new BatteryRunQuery { TenantId = request.TenantId, CaptainId = request.CaptainId };
                    if (!String.IsNullOrWhiteSpace(request.Status))
                    {
                        if (!Enum.TryParse(request.Status.Trim(), true, out BatteryRunStatusEnum status) || !Enum.IsDefined(status))
                            return (object)McpToolError.InvalidArgument("status must be Queued, Running, Completed, Failed or Cancelled.");
                        query.Status = status;
                    }

                    if (request.PageNumber.HasValue) query.PageNumber = request.PageNumber.Value;
                    if (request.PageSize.HasValue) query.PageSize = request.PageSize.Value;
                    return (object)await service.EnumerateRunsAsync(caller, query).ConfigureAwait(false);
                });

            register(
                "cancel_battery_run",
                "Cancel a fixture battery run as a stopped Full Evaluate does: no further task is dispatched, the open mission is cancelled (releasing its dock and captain), and the run ends Cancelled with finished results kept. Waits up to 10 seconds, then returns the run (still Running while its open mission stops; poll get_battery_run). A finished run is returned unchanged. Needs a global admin or a tenant admin of the run's tenant.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        id = new { type = "string", description = "Run ID (bat_ prefix)" },
                        reason = new { type = "string", description = "Reason recorded on the run (at most 500 characters)" }
                    },
                    required = new[] { "id" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BatteryRunCancelArgs request = Read<BatteryRunCancelArgs>(args);
                    return (object)await service.CancelRunAsync(caller, request.Id, request.Reason).ConfigureAwait(false);
                });

            register(
                "list_battery_results",
                "List one page of a fixture battery run's per-task results (Passed, Failed, CouldNotRun or PendingEvaluation), each with the grader's evidence and the captain's raw output. Paged in the database; returns an enumeration result (objects, totalRecords, totalPages).",
                new
                {
                    type = "object",
                    properties = new
                    {
                        runId = new { type = "string", description = "Run ID (bat_ prefix)" },
                        pageNumber = new { type = "integer", description = "Page number (default 1)" },
                        pageSize = new { type = "integer", description = "Page size (1-1000, default 10)" },
                        status = new { type = "string", @enum = new[] { "Passed", "Failed", "CouldNotRun", "PendingEvaluation" }, description = "Outcome filter" },
                        order = new { type = "string", @enum = new[] { "CreatedDescending", "CreatedAscending" }, description = "Order by creation (default CreatedDescending)" }
                    },
                    required = new[] { "runId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    BatteryResultListArgs request = Read<BatteryResultListArgs>(args);
                    EnumerationQuery query = new EnumerationQuery { Status = request.Status };
                    if (request.PageNumber.HasValue) query.PageNumber = request.PageNumber.Value;
                    if (request.PageSize.HasValue) query.PageSize = request.PageSize.Value;
                    if (!String.IsNullOrWhiteSpace(request.Order))
                    {
                        if (!Enum.TryParse(request.Order.Trim(), true, out EnumerationOrderEnum order) || !Enum.IsDefined(order))
                            return (object)McpToolError.InvalidArgument("order must be CreatedDescending or CreatedAscending.");
                        query.Order = order;
                    }

                    return (object)await service.EnumerateResultsAsync(caller, request.RunId, query).ConfigureAwait(false);
                });
        }

        #endregion

        #region Private-Methods

        private static string? PackArgument(JsonElement? args, string name)
        {
            if (!args.HasValue || !args.Value.TryGetProperty(name, out JsonElement value)) return null;
            if (value.ValueKind != JsonValueKind.String) throw new ArgumentException(name + " must be a string.");
            return value.GetString();
        }

        private static T Read<T>(JsonElement? args) where T : class, new()
        {
            if (!args.HasValue || args.Value.ValueKind != JsonValueKind.Object) return new T();
            try
            {
                return JsonSerializer.Deserialize<T>(args.Value, _JsonOptions) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("Invalid arguments: " + ex.Message, ex);
            }
        }

        #endregion
    }
}
