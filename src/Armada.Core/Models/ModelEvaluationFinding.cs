namespace Armada.Core.Models
{
    /// <summary>
    /// One strength or weakness the evaluator wrote, citing one mechanical count by key, for example "single-file
    /// Worker edits (3 of 3)" with key "task:csharp/add-member", passed 3 and total 3.
    /// </summary>
    public class ModelEvaluationFinding
    {
        #region Public-Members

        /// <summary>
        /// The sentence, at most 300 characters.
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Key of the mechanical count it cites.
        /// </summary>
        public string Key { get; set; } = "";

        /// <summary>
        /// Passed attempts it cites (must equal the count's Passed).
        /// </summary>
        public int Passed { get; set; } = 0;

        /// <summary>
        /// Graded attempts it cites (must equal the count's Graded).
        /// </summary>
        public int Total { get; set; } = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ModelEvaluationFinding()
        {
        }

        #endregion
    }
}
