namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Names of the submit tools and which persona submits what (RELIABILITY_SIMPLICITY_PLAN.md RS-6.2). The same names are
    /// used by the MCP tools and by the native tools of the API endpoint runtime.
    /// </summary>
    public static class StageSubmissionTools
    {
        /// <summary>
        /// Tool an Architect submits its plan with.
        /// </summary>
        public const string SubmitPlan = "armada_submit_plan";

        /// <summary>
        /// Tool a Judge submits its verdict with.
        /// </summary>
        public const string SubmitVerdict = "armada_submit_verdict";

        /// <summary>
        /// Tool every other contract persona submits its result with.
        /// </summary>
        public const string SubmitResult = "armada_submit_result";

        /// <summary>
        /// Environment variable the Admiral sets on a mission launch to name the submit tool the stage must call, so a runtime
        /// knows which native tool to offer without reading the prompt.
        /// </summary>
        public const string ToolEnvironmentVariable = "ARMADA_SUBMIT_TOOL";

        /// <summary>
        /// Environment variable that carries the stage's persona next to <see cref="ToolEnvironmentVariable"/>, so a native
        /// submit tool checks the same persona contract the Admiral does.
        /// </summary>
        public const string PersonaEnvironmentVariable = "ARMADA_SUBMIT_PERSONA";

        /// <summary>
        /// What a persona submits, or null for a persona without a contract (the Recorder and unknown personas).
        /// </summary>
        /// <param name="persona">Persona name.</param>
        /// <returns>The kind, or null.</returns>
        public static StageSubmissionKindEnum? KindFor(string? persona)
        {
            switch (String.IsNullOrWhiteSpace(persona) ? PersonaCatalog.Worker : PersonaCatalog.NormalizeName(persona))
            {
                case PersonaCatalog.Architect:
                    return StageSubmissionKindEnum.Plan;
                case PersonaCatalog.Judge:
                    return StageSubmissionKindEnum.Verdict;
                case PersonaCatalog.Worker:
                case PersonaCatalog.ProductManager:
                case PersonaCatalog.TestEngineer:
                case PersonaCatalog.Linter:
                case PersonaCatalog.UsabilityEngineer:
                    return StageSubmissionKindEnum.Result;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The tool name for a submission kind.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>Tool name.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined kind.</exception>
        public static string ToolFor(StageSubmissionKindEnum kind)
        {
            switch (kind)
            {
                case StageSubmissionKindEnum.Plan: return SubmitPlan;
                case StageSubmissionKindEnum.Verdict: return SubmitVerdict;
                case StageSubmissionKindEnum.Result: return SubmitResult;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        /// <summary>
        /// The submit tool a persona calls, or null for a persona without one.
        /// </summary>
        /// <param name="persona">Persona name.</param>
        /// <returns>Tool name, or null.</returns>
        public static string? ToolForPersona(string? persona)
        {
            StageSubmissionKindEnum? kind = KindFor(persona);
            return kind.HasValue ? ToolFor(kind.Value) : null;
        }

        /// <summary>
        /// The kind a submit tool name submits, or null for any other name.
        /// </summary>
        /// <param name="toolName">Tool name.</param>
        /// <returns>The kind, or null.</returns>
        public static StageSubmissionKindEnum? KindForTool(string? toolName)
        {
            if (String.Equals(toolName, SubmitPlan, StringComparison.Ordinal)) return StageSubmissionKindEnum.Plan;
            if (String.Equals(toolName, SubmitVerdict, StringComparison.Ordinal)) return StageSubmissionKindEnum.Verdict;
            if (String.Equals(toolName, SubmitResult, StringComparison.Ordinal)) return StageSubmissionKindEnum.Result;
            return null;
        }

        /// <summary>
        /// The tool description shown to the model.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>Description.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown for an undefined kind.</exception>
        public static string DescriptionFor(StageSubmissionKindEnum kind)
        {
            const string tail = " The submission is checked at once; on errors nothing is stored and the errors are returned so you can fix them and call again. The last accepted submission is your result and replaces any marker lines in your reply.";
            switch (kind)
            {
                case StageSubmissionKindEnum.Plan:
                    return "Submit your Architect plan (contract version " + StageOutputContract.PlanContractVersion + ") for this mission. Only the Architect of the mission may call it." + tail;
                case StageSubmissionKindEnum.Verdict:
                    return "Submit your Judge verdict, the three review lenses, structured findings and the evidence you checked for this mission. Only the Judge of the mission may call it." + tail;
                case StageSubmissionKindEnum.Result:
                    return "Submit your stage result (complete, or blocked for a verifier), the report sections your contract lists, structured findings and the evidence you checked. Only the captain of the mission may call it." + tail;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
    }
}
