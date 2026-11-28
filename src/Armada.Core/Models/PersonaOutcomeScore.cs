namespace Armada.Core.Models
{
    /// <summary>
    /// One model profile's recorded results for one persona, weighted by age (RS-8.6). Each rate is the decayed share of
    /// the attempts it applies to; <see cref="Score"/> is the mean of the rates that have data, oriented so higher is
    /// better.
    /// </summary>
    public class PersonaOutcomeScore
    {
        #region Public-Members

        /// <summary>
        /// Model profile identity key the attempts matched.
        /// </summary>
        public string ProfileKey { get; set; } = "";

        /// <summary>
        /// Persona (normalized name).
        /// </summary>
        public string Persona { get; set; } = "";

        /// <summary>
        /// Attempts counted (not weighted).
        /// </summary>
        public int Samples { get; set; } = 0;

        /// <summary>
        /// Decayed share of attempts that completed without failing their stage checks.
        /// </summary>
        public double? SuccessRate { get; set; } = null;

        /// <summary>
        /// Decayed share of Judge verdicts on this work that were PASS, or null when no verdict was counted.
        /// </summary>
        public double? JudgePassRate { get; set; } = null;

        /// <summary>
        /// Decayed share of attempts that ended as a no-op.
        /// </summary>
        public double? NoOpRate { get; set; } = null;

        /// <summary>
        /// Decayed share of attempts whose suspected cause is not ToolUse.
        /// </summary>
        public double? ToolCallValidity { get; set; } = null;

        /// <summary>
        /// Decayed share of attempts that needed an output or contract repair.
        /// </summary>
        public double? RepairRate { get; set; } = null;

        /// <summary>
        /// Mean of success, Judge pass, one minus no-op, tool-call validity and one minus repair (those with data), 0 to 1.
        /// </summary>
        public double Score { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PersonaOutcomeScore()
        {
        }

        #endregion
    }
}
