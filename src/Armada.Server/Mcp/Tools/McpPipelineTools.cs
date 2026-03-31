namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using ArmadaConstants = Armada.Core.Constants;
    using Armada.Core.Database;
    using Armada.Core.Models;

    /// <summary>
    /// Registers MCP tools for pipeline CRUD operations.
    /// </summary>
    public static class McpPipelineTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers pipeline MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for pipeline data access.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database)
        {
            register(
                "armada_create_pipeline",
                "Create a new pipeline with an ordered sequence of persona stages",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Pipeline name (e.g. 'WorkerOnly', 'FullPipeline', 'Reviewed')" },
                        description = new { type = "string", description = "Human-readable description of the pipeline workflow" },
                        stages = new
                        {
                            type = "array",
                            description = "Ordered list of pipeline stages",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    personaName = new { type = "string", description = "Persona name for this stage" },
                                    isOptional = new { type = "boolean", description = "Whether this stage is optional (default false)" },
                                    description = new { type = "string", description = "Description of what this stage does" }
                                },
                                required = new[] { "personaName" }
                            }
                        }
                    },
                    required = new[] { "name", "stages" }
                },
                async (args) =>
                {
                    PipelineArgs request = JsonSerializer.Deserialize<PipelineArgs>(args!.Value, _JsonOptions)!;
                    if (String.IsNullOrEmpty(request.Name)) return (object)new { Error = "name is required" };
                    if (request.Stages == null || request.Stages.Count == 0) return (object)new { Error = "stages is required and must not be empty" };

                    Pipeline pipeline = new Pipeline(request.Name);
                    pipeline.TenantId = ArmadaConstants.DefaultTenantId;
                    if (request.Description != null)
                        pipeline.Description = request.Description;

                    List<PipelineStage> stages = new List<PipelineStage>();
                    for (int i = 0; i < request.Stages.Count; i++)
                    {
                        PipelineStageArgs stageArgs = request.Stages[i];
                        if (String.IsNullOrEmpty(stageArgs.PersonaName)) return (object)new { Error = "personaName is required for stage " + (i + 1) };

                        PipelineStage stage = new PipelineStage(i + 1, stageArgs.PersonaName);
                        if (stageArgs.IsOptional.HasValue)
                            stage.IsOptional = stageArgs.IsOptional.Value;
                        if (stageArgs.Description != null)
                            stage.Description = stageArgs.Description;
                        stages.Add(stage);
                    }
                    pipeline.Stages = stages;

                    pipeline = await database.Pipelines.CreateAsync(pipeline).ConfigureAwait(false);
                    return (object)pipeline;
                });

            register(
                "armada_get_pipeline",
                "Get a pipeline by name, including its stages",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Pipeline name" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    PipelineArgs request = JsonSerializer.Deserialize<PipelineArgs>(args!.Value, _JsonOptions)!;
                    string name = request.Name;
                    if (String.IsNullOrEmpty(name)) return (object)new { Error = "name is required" };
                    Pipeline? pipeline = await database.Pipelines.ReadByNameAsync(name).ConfigureAwait(false);
                    if (pipeline == null) return (object)new { Error = "Pipeline not found: " + name };
                    return (object)pipeline;
                });

            register(
                "armada_update_pipeline",
                "Update an existing pipeline's properties and stages",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Pipeline name (used to look up the pipeline)" },
                        description = new { type = "string", description = "New description" },
                        stages = new
                        {
                            type = "array",
                            description = "New ordered list of pipeline stages (replaces existing stages)",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    personaName = new { type = "string", description = "Persona name for this stage" },
                                    isOptional = new { type = "boolean", description = "Whether this stage is optional (default false)" },
                                    description = new { type = "string", description = "Description of what this stage does" }
                                },
                                required = new[] { "personaName" }
                            }
                        }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    PipelineArgs request = JsonSerializer.Deserialize<PipelineArgs>(args!.Value, _JsonOptions)!;
                    string name = request.Name;
                    if (String.IsNullOrEmpty(name)) return (object)new { Error = "name is required" };

                    Pipeline? pipeline = await database.Pipelines.ReadByNameAsync(name).ConfigureAwait(false);
                    if (pipeline == null) return (object)new { Error = "Pipeline not found: " + name };

                    if (request.Description != null)
                        pipeline.Description = request.Description;

                    if (request.Stages != null)
                    {
                        List<PipelineStage> stages = new List<PipelineStage>();
                        for (int i = 0; i < request.Stages.Count; i++)
                        {
                            PipelineStageArgs stageArgs = request.Stages[i];
                            if (String.IsNullOrEmpty(stageArgs.PersonaName)) return (object)new { Error = "personaName is required for stage " + (i + 1) };

                            PipelineStage stage = new PipelineStage(i + 1, stageArgs.PersonaName);
                            stage.PipelineId = pipeline.Id;
                            if (stageArgs.IsOptional.HasValue)
                                stage.IsOptional = stageArgs.IsOptional.Value;
                            if (stageArgs.Description != null)
                                stage.Description = stageArgs.Description;
                            stages.Add(stage);
                        }
                        pipeline.Stages = stages;
                    }

                    pipeline.LastUpdateUtc = DateTime.UtcNow;
                    pipeline = await database.Pipelines.UpdateAsync(pipeline).ConfigureAwait(false);
                    return (object)pipeline;
                });

            register(
                "armada_delete_pipeline",
                "Delete a pipeline by name. Built-in pipelines cannot be deleted.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "Pipeline name" }
                    },
                    required = new[] { "name" }
                },
                async (args) =>
                {
                    PipelineArgs request = JsonSerializer.Deserialize<PipelineArgs>(args!.Value, _JsonOptions)!;
                    string name = request.Name;
                    if (String.IsNullOrEmpty(name)) return (object)new { Error = "name is required" };

                    Pipeline? pipeline = await database.Pipelines.ReadByNameAsync(name).ConfigureAwait(false);
                    if (pipeline == null) return (object)new { Error = "Pipeline not found: " + name };
                    if (pipeline.IsBuiltIn) return (object)new { Error = "Cannot delete built-in pipeline: " + name };

                    await database.Pipelines.DeleteAsync(pipeline.Id).ConfigureAwait(false);
                    return (object)new { Status = "deleted", Name = name };
                });
        }
    }
}
