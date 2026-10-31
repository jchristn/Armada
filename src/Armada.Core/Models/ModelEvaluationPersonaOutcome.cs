namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// Mechanical outcomes of one persona in a Full Evaluate: the persona's totals plus one count per battery task.
    /// </summary>
    public class ModelEvaluationPersonaOutcome
    {
        #region Public-Members

        /// <summary>
        /// Persona name (for example Worker).
        /// </summary>
        public string Persona { get; set; } = "";

        /// <summary>
        /// Totals over the persona's tasks (key "persona:Name").
        /// </summary>
        public ModelEvaluationCount Totals { get; set; } = new ModelEvaluationCount();

        /// <summary>
        /// One count per battery task the persona was scored on (key "task:pack/task").
        /// </summary>
        [CollectionBound(EvaluationLimits.MaxCounts, Reason = "One per battery task of the selected packs")]
        public List<ModelEvaluationCount> Tasks
        {
            get => _Tasks;
            set => _Tasks = value ?? new List<ModelEvaluationCount>();
        }

        #endregion

        #region Private-Members

        private List<ModelEvaluationCount> _Tasks = new List<ModelEvaluationCount>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEvaluationPersonaOutcome()
        {
        }

        #endregion
    }
}
