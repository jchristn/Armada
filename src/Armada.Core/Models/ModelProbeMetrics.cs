namespace Armada.Core.Models
{
    /// <summary>
    /// Values one probe measured. Every field is nullable: a probe fills only its own fields, and an unrun probe leaves
    /// them all null. Stored as JSON on the result row.
    /// </summary>
    public class ModelProbeMetrics
    {
        #region Public-Members

        /// <summary>
        /// Served context: the configured context length, or null.
        /// </summary>
        public int? ConfiguredContextTokens { get; set; } = null;

        /// <summary>
        /// Served context: the context length the endpoint reported serving, or null.
        /// </summary>
        public int? ServedContextTokens { get; set; } = null;

        /// <summary>
        /// Served context: whether served and configured differ (a configuration error).
        /// </summary>
        public bool? ContextMismatch { get; set; } = null;

        /// <summary>
        /// Output contract, tool calling: how many exchanges or checks met the contract exactly.
        /// </summary>
        public int? Compliant { get; set; } = null;

        /// <summary>
        /// Output contract, tool calling: how many exchanges or checks were scored.
        /// </summary>
        public int? Checked { get; set; } = null;

        /// <summary>
        /// Output contract: how many answers the existing repair path would have to repair (Judge and Architect), or that miss the contract with no repair path yet (Worker).
        /// </summary>
        public int? RepairNeeded { get; set; } = null;

        /// <summary>
        /// Output contract: the Worker answer ended with a standalone [ARMADA:RESULT] COMPLETE line.
        /// </summary>
        public bool? WorkerResultCompliant { get; set; } = null;

        /// <summary>
        /// Output contract: the Judge answer carried exactly one standalone [ARMADA:VERDICT] line.
        /// </summary>
        public bool? JudgeVerdictCompliant { get; set; } = null;

        /// <summary>
        /// Output contract: the Judge answer carried every required review lens section.
        /// </summary>
        public bool? JudgeSectionsCompliant { get; set; } = null;

        /// <summary>
        /// Output contract: the Architect answer carried a valid armada-plan JSON block.
        /// </summary>
        public bool? ArchitectPlanCompliant { get; set; } = null;

        /// <summary>
        /// ASCII and format rules: total violations (non-ASCII characters plus ANSI escape sequences).
        /// </summary>
        public int? Violations { get; set; } = null;

        /// <summary>
        /// ASCII and format rules: em and en dashes.
        /// </summary>
        public int? EmDashes { get; set; } = null;

        /// <summary>
        /// ASCII and format rules: curly single and double quotes.
        /// </summary>
        public int? SmartQuotes { get; set; } = null;

        /// <summary>
        /// ASCII and format rules: ANSI escape sequences.
        /// </summary>
        public int? AnsiCodes { get; set; } = null;

        /// <summary>
        /// ASCII and format rules: other non-ASCII characters.
        /// </summary>
        public int? OtherNonAscii { get; set; } = null;

        /// <summary>
        /// Tool calling: answers or fields that could not be read (no armada-probe JSON block, wrong types).
        /// </summary>
        public int? MalformedCalls { get; set; } = null;

        /// <summary>
        /// Tool calling: the model reported the failing tool's error and still finished the remaining steps.
        /// </summary>
        public bool? ErrorRecovered { get; set; } = null;

        /// <summary>
        /// Tool calling: the run timed out or repeated the same output line many times (a tool loop).
        /// </summary>
        public bool? Looping { get; set; } = null;

        /// <summary>
        /// Speed: milliseconds to the first output at about 4K input tokens.
        /// </summary>
        public long? FirstOutputMs4k { get; set; } = null;

        /// <summary>
        /// Speed: estimated output tokens per second at about 4K input tokens.
        /// </summary>
        public double? OutputTokensPerSecond4k { get; set; } = null;

        /// <summary>
        /// Speed: milliseconds to the first output at about 32K input tokens.
        /// </summary>
        public long? FirstOutputMs32k { get; set; } = null;

        /// <summary>
        /// Speed: estimated output tokens per second at about 32K input tokens.
        /// </summary>
        public double? OutputTokensPerSecond32k { get; set; } = null;

        /// <summary>
        /// Speed: advised inference request timeout in seconds (never a stage-check timeout).
        /// </summary>
        public int? SuggestedRequestTimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Speed: advised inference inactivity timeout in seconds.
        /// </summary>
        public int? SuggestedInactivityTimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Speed: advised stall timeout in seconds.
        /// </summary>
        public int? SuggestedStallTimeoutSeconds { get; set; } = null;

        /// <summary>
        /// Determinism: distinct complete outputs after case and whitespace normalization, including explanations.
        /// </summary>
        public int? DistinctOutputs { get; set; } = null;

        /// <summary>
        /// Determinism: every repeat contained the same valid five-integer answer, ignoring explanatory wording.
        /// </summary>
        public bool? AnswerAgreement { get; set; } = null;

        /// <summary>
        /// Determinism: lowest word-set similarity (0 to 1) between any two repeats.
        /// </summary>
        public double? MinSimilarity { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelProbeMetrics()
        {
        }

        #endregion
    }
}
