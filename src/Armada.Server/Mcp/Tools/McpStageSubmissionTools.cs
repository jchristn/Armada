namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Registers the submit tools (RELIABILITY_SIMPLICITY_PLAN.md RS-6.2): armada_submit_plan, armada_submit_verdict and
    /// armada_submit_result, which a captain calls with its mission-scoped token to submit its stage output as typed JSON,
    /// and get_mission_submissions, which reads what a mission submitted. A submission is checked in the same call; the
    /// errors come back in the result (accepted false) so the model can fix them and call again.
    /// </summary>
    public static class McpStageSubmissionTools
    {
        #region Private-Members

        private const string UnavailableMessage = "Stage submissions are not available on this server.";

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register the tools. When <paramref name="service"/> is null every tool is still registered and answers a typed
        /// Unavailable error, so every tool list matches.
        /// </summary>
        /// <param name="register">Registration delegate.</param>
        /// <param name="service">Stage submission access service, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="register"/> is null.</exception>
        public static void Register(RegisterToolDelegate register, StageSubmissionAccessService? service)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            foreach (StageSubmissionKindEnum kind in new[] { StageSubmissionKindEnum.Plan, StageSubmissionKindEnum.Verdict, StageSubmissionKindEnum.Result })
            {
                StageSubmissionKindEnum submitted = kind;
                register(
                    StageSubmissionTools.ToolFor(submitted),
                    StageSubmissionTools.DescriptionFor(submitted),
                    StageSubmissionSchema.For(submitted),
                    async (args) =>
                    {
                        if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                        AuthContext caller = McpToolHelpers.ResolveCallerContext();
                        string? arguments = args.HasValue && args.Value.ValueKind == JsonValueKind.Object ? args.Value.GetRawText() : null;
                        return (object)await service.SubmitAsync(caller, submitted, arguments).ConfigureAwait(false);
                    });
            }

            register(
                "get_mission_submissions",
                "List what a mission submitted through armada_submit_plan, armada_submit_verdict or armada_submit_result, newest first, one entry per accepted submission with its attempt, source and typed content. The latest submission of an attempt decides that attempt's plan, verdict or result.",
                new
                {
                    type = "object",
                    properties = new { missionId = new { type = "string", description = "Mission ID (msn_ prefix)" } },
                    required = new[] { "missionId" }
                },
                async (args) =>
                {
                    if (service == null) return (object)McpToolError.Unavailable(UnavailableMessage);
                    AuthContext caller = McpToolHelpers.ResolveCallerContext();
                    MissionIdArgs request = Read<MissionIdArgs>(args);
                    return (object)await service.ListByMissionAsync(caller, request.MissionId).ConfigureAwait(false);
                });
        }

        #endregion

        #region Private-Methods

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
