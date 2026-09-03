namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;

    /// <summary>
    /// Typed result of the in-dock Definition-of-Done gate.
    /// </summary>
    public sealed class DefinitionOfDoneResult
    {
        #region Public-Members

        /// <summary>
        /// Classified outcome of the gate.
        /// </summary>
        public DefinitionOfDoneOutcomeEnum Outcome { get; }

        /// <summary>
        /// Human-readable detail (which phase, exit code, or a redaction-safe output tail).
        /// </summary>
        public string Detail { get; }

        /// <summary>
        /// Whether the gate was skipped (disabled, or no build/test commands configured). A skipped gate
        /// reports <see cref="DefinitionOfDoneOutcomeEnum.Pass"/> so acceptance proceeds unchanged.
        /// </summary>
        public bool Skipped { get; }

        /// <summary>
        /// True when the change satisfied the gate (or the gate did not run).
        /// </summary>
        public bool Passed => Outcome == DefinitionOfDoneOutcomeEnum.Pass;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="outcome">Classified outcome.</param>
        /// <param name="detail">Human-readable detail.</param>
        /// <param name="skipped">Whether the gate was skipped.</param>
        public DefinitionOfDoneResult(DefinitionOfDoneOutcomeEnum outcome, string detail, bool skipped = false)
        {
            Outcome = outcome;
            Detail = detail ?? String.Empty;
            Skipped = skipped;
        }

        /// <summary>
        /// A passing result for a gate that did not run.
        /// </summary>
        /// <param name="reason">Why the gate was skipped.</param>
        /// <returns>A skipped, passing result.</returns>
        public static DefinitionOfDoneResult SkippedResult(string reason)
        {
            return new DefinitionOfDoneResult(DefinitionOfDoneOutcomeEnum.Pass, reason, true);
        }

        #endregion
    }
}
