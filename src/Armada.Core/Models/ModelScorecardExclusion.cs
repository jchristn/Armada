namespace Armada.Core.Models
{
    /// <summary>
    /// One attribution rule that removed stage outcomes (or Judge verdicts) from a scorecard, and how many it removed.
    /// Every rule is listed, including those that removed nothing.
    /// </summary>
    public class ModelScorecardExclusion
    {
        #region Public-Members

        /// <summary>
        /// Rule key, for example Evaluation, NotTheModel, Infra, OperatorAction, RuntimeEnvironment, NotSettled,
        /// OtherStage, JudgeVerdictOtherCaptain or JudgeVerdictExcluded.
        /// </summary>
        public string Rule { get; set; } = "";

        /// <summary>
        /// What the rule removes and why.
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// Number of items the rule removed within the day window.
        /// </summary>
        public int Removed { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelScorecardExclusion()
        {
        }

        #endregion
    }
}
