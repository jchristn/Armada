namespace Armada.Core.Models
{
    /// <summary>
    /// Mechanical counts for one item of a Full Evaluate report: a probe, a battery task or a persona. Passed of Graded
    /// is what a narrative may cite; CouldNotRun and PendingEvaluation are never counted against the model.
    /// </summary>
    public class ModelEvaluationCount
    {
        #region Public-Members

        /// <summary>
        /// Stable key the evaluator cites, for example "probe:OutputContract", "task:csharp/coordinated-edit" or
        /// "persona:Worker".
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Human-readable label.
        /// </summary>
        public string Label { get; set; } = "";

        /// <summary>
        /// Persona the item tests, or null for probes that test no single persona.
        /// </summary>
        public string? Persona { get; set; } = null;

        /// <summary>
        /// Attempts that passed.
        /// </summary>
        public int Passed { get; set; } = 0;

        /// <summary>
        /// Attempts that failed.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Attempts that could not run (missing toolchain, budget, cancelled); not counted against the model.
        /// </summary>
        public int CouldNotRun { get; set; } = 0;

        /// <summary>
        /// Attempts whose grade needs the evaluator's rubric; not counted mechanically.
        /// </summary>
        public int PendingEvaluation { get; set; } = 0;

        /// <summary>
        /// Graded attempts (Passed plus Failed).
        /// </summary>
        public int Graded
        {
            get => Passed + Failed;
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEvaluationCount()
        {
        }

        #endregion
    }
}
