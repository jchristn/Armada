namespace Armada.Runtimes
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes.Mcp;
    using PolyToolDefinition = PolyPrompt.Models.ToolDefinition;

    /// <summary>
    /// The submit tool of one API endpoint run (RELIABILITY_SIMPLICITY_PLAN.md RS-6.3): the same name and schema as the MCP
    /// submit tool, checked in the runtime with the same validator. A valid submission is forwarded to the Admiral's MCP
    /// tool when the run has MCP access, so it is stored at once; otherwise it is written into the run's output as a
    /// submission block, which the Admiral stores at completion. Not thread-safe; one instance per run.
    /// </summary>
    public class NativeSubmitTool
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// What the tool submits.
        /// </summary>
        public StageSubmissionKindEnum Kind { get; }

        /// <summary>
        /// Persona whose contract the submission is checked against.
        /// </summary>
        public string Persona { get; }

        /// <summary>
        /// True once a submission was accepted.
        /// </summary>
        public bool Submitted { get; private set; } = false;

        /// <summary>
        /// Rejected calls so far.
        /// </summary>
        public int RejectedCalls { get; private set; } = 0;

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions();
        private readonly int _ProcessId;
        private int _Accepted = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="kind">What the tool submits.</param>
        /// <param name="persona">Persona of the stage.</param>
        /// <param name="processId">Synthetic process id of the run, used in block ids.</param>
        public NativeSubmitTool(StageSubmissionKindEnum kind, string? persona, int processId)
        {
            Kind = kind;
            Name = StageSubmissionTools.ToolFor(kind);
            Persona = String.IsNullOrWhiteSpace(persona) ? PersonaCatalog.Worker : PersonaCatalog.NormalizeName(persona);
            _ProcessId = processId;
        }

        /// <summary>
        /// The submit tool for a launch environment, or null when the Admiral named none or the names disagree.
        /// </summary>
        /// <param name="toolName">Value of <see cref="StageSubmissionTools.ToolEnvironmentVariable"/>.</param>
        /// <param name="persona">Value of <see cref="StageSubmissionTools.PersonaEnvironmentVariable"/>.</param>
        /// <param name="processId">Synthetic process id.</param>
        /// <returns>The tool, or null.</returns>
        public static NativeSubmitTool? FromEnvironment(string? toolName, string? persona, int processId)
        {
            StageSubmissionKindEnum? kind = StageSubmissionTools.KindForTool(toolName);
            if (kind == null) return null;
            if (StageSubmissionTools.KindFor(persona) != kind) return null;
            return new NativeSubmitTool(kind.Value, persona, processId);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The tool definition sent to the model.
        /// </summary>
        /// <returns>Definition.</returns>
        public PolyToolDefinition Definition()
        {
            return PolyToolDefinition.Function(Name, StageSubmissionTools.DescriptionFor(Kind), StageSubmissionSchema.For(Kind));
        }

        /// <summary>
        /// Handle one call: check it, then forward or record it. Returns the JSON result for the model.
        /// </summary>
        /// <param name="argumentsJson">Call arguments.</param>
        /// <param name="mcpClient">The run's MCP client when it offers the same tool, or null.</param>
        /// <param name="emit">Writes text into the run's output (for a native block).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result JSON.</returns>
        /// <exception cref="ArgumentNullException">Thrown when emit is null.</exception>
        public async Task<string> HandleAsync(string? argumentsJson, McpToolClient? mcpClient, Action<string> emit, CancellationToken token)
        {
            if (emit == null) throw new ArgumentNullException(nameof(emit));
            StageSubmissionCheck check = StageSubmissionValidator.Check(Kind, Persona, argumentsJson, Armada.Core.Constants.DefaultMaxHierarchyDepth);
            if (!check.Accepted)
            {
                RejectedCalls++;
                return JsonSerializer.Serialize(new StageSubmitResult
                {
                    Accepted = false,
                    Kind = Kind,
                    Errors = check.Errors,
                    Message = "Not stored. Fix every error and call " + Name + " again with the complete submission."
                }, _JsonOptions);
            }

            if (mcpClient != null)
            {
                try
                {
                    McpToolCallResult forwarded = await mcpClient.CallToolResultAsync(Name, argumentsJson ?? "{}", token).ConfigureAwait(false);
                    string text = McpToolClient.RenderResultText(forwarded);
                    StageSubmitResult? stored = null;
                    if (!forwarded.IsError)
                    {
                        try { stored = JsonSerializer.Deserialize<StageSubmitResult>(text, _JsonOptions); }
                        catch (JsonException) { stored = null; }
                    }
                    if (stored != null && stored.Accepted)
                    {
                        Submitted = true;
                        return text;
                    }
                    if (stored != null && !stored.Accepted)
                    {
                        RejectedCalls++;
                        return text;
                    }
                }
                catch (Exception ex) when (!(ex is OperationCanceledException && token.IsCancellationRequested))
                {
                    // The Admiral could not be reached; the submission still reaches it through the output below.
                }
            }

            _Accepted++;
            string id = "native-" + _ProcessId + "-" + _Accepted;
            emit(StageSubmissionText.Block(check.Submission!, id).Trim('\n'));
            Submitted = true;
            return JsonSerializer.Serialize(new StageSubmitResult
            {
                Accepted = true,
                Kind = Kind,
                SubmissionId = id,
                Message = "Stored. This submission is your result; finish with a brief plain-text reply. Submitting again replaces it."
            }, _JsonOptions);
        }

        /// <summary>
        /// Whether a final reply already meets the text contract (the fallback), so the run needs no forced submission.
        /// </summary>
        /// <param name="finalText">The terminal reply.</param>
        /// <returns>True when the reply carries a valid plan, verdict line, completion line or verifier block.</returns>
        public bool TextMeetsContract(string? finalText)
        {
            if (String.IsNullOrWhiteSpace(finalText)) return false;
            switch (Kind)
            {
                case StageSubmissionKindEnum.Plan:
                    return ArchitectPlanParser.Parse(finalText, Armada.Core.Constants.DefaultMaxHierarchyDepth).Status == ArchitectPlanParseStatusEnum.Parsed;
                case StageSubmissionKindEnum.Verdict:
                    return JudgeVerdictParser.Parse(finalText) != JudgeVerdictEnum.None;
                default:
                    return CompletionLine.HasCompletionLine(finalText) || VerifierContract.IsBlocked(finalText);
            }
        }

        /// <summary>
        /// The instruction sent with a forced submit turn.
        /// </summary>
        /// <returns>Instruction text.</returns>
        public string ForcePrompt()
        {
            return "You stopped without submitting your result. Call " + Name + " now with your complete "
                + Kind.ToString().ToLowerInvariant() + "; it is how this stage finishes. Do not call other tools.";
        }

        #endregion
    }
}
