namespace Armada.Core.Services
{
    using System;
    using System.Linq;

    /// <summary>
    /// Structured blocking outcome for verifier stages (Test Engineer, Linter, Usability Engineer), HF-25. A verifier that
    /// finds a product defect its role may not fix used to report COMPLETE with the defect in its prose, so the work went
    /// on through the rest of the chain until the Judge found the same defect. With this contract the stage ends with
    /// <see cref="BlockedMarker"/> and an <c>## Affected Case</c> section (the same format the Judge uses), and the work
    /// goes straight back to the Worker with the finding, as a Judge's NEEDS_REVISION does.
    /// </summary>
    public static class VerifierContract
    {
        #region Public-Members

        /// <summary>
        /// The standalone line a verifier ends with when it found a blocking product defect.
        /// </summary>
        public const string BlockedMarker = "[ARMADA:STAGE-OUTCOME] BLOCKED";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a persona is a verifier stage that can report a blocking outcome.
        /// </summary>
        /// <param name="persona">The persona name.</param>
        /// <returns>True for Test Engineer, Linter and Usability Engineer.</returns>
        public static bool Applies(string? persona)
        {
            return PersonaCatalog.Matches(persona, PersonaCatalog.TestEngineer)
                || PersonaCatalog.Matches(persona, PersonaCatalog.Linter)
                || PersonaCatalog.Matches(persona, PersonaCatalog.UsabilityEngineer);
        }

        /// <summary>
        /// Whether an output reports a blocking outcome: a standalone <see cref="BlockedMarker"/> line.
        /// </summary>
        /// <param name="output">The stage output.</param>
        /// <returns>True when the stage reported BLOCKED.</returns>
        public static bool IsBlocked(string? output)
        {
            if (String.IsNullOrEmpty(output)) return false;
            return output.Replace("\r", String.Empty).Split('\n').Any(line => String.Equals(line.Trim(), BlockedMarker, StringComparison.Ordinal));
        }

        /// <summary>
        /// The instruction added to a verifier's output contract.
        /// </summary>
        /// <returns>The instruction text.</returns>
        public static string BlockingInstruction()
        {
            return " If you find a defect in the product code that your role must not fix (it is outside what this stage may change), do not report COMPLETE and do not work around it: "
                + "add ## Affected Case with File: <path>[:line] or Scenario: <inputs and wrong result>, the violated requirement, expected and actual behavior, and the required correction, "
                + "then end with the standalone line " + BlockedMarker + " instead of " + StageOutputContract.ResultCompleteLine + ". The work goes back to the Worker with your finding, and the later stages wait for the fix.";
        }

        #endregion
    }
}
