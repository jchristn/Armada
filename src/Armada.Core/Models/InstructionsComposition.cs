namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>
    /// A composed mission instructions file: the text, its estimated size, the budget it was held to, and the sections
    /// trimmed to meet it.
    /// </summary>
    public class InstructionsComposition
    {
        #region Public-Members

        /// <summary>
        /// The composed instructions text.
        /// </summary>
        public string Text { get; set; } = "";

        /// <summary>
        /// Estimated tokens of <see cref="Text"/> plus the tool schemas the launch sends.
        /// </summary>
        public long EstimatedTokens { get; set; } = 0;

        /// <summary>
        /// The budget in tokens, or null when the reader's context is unknown (nothing is trimmed).
        /// </summary>
        public long? BudgetTokens { get; set; } = null;

        /// <summary>
        /// Names of the sections trimmed to fit, in the order they were trimmed.
        /// </summary>
        [CollectionBound(64, Reason = "At most one per instructions section")]
        public List<string> TrimmedSections { get; set; } = new List<string>();

        /// <summary>
        /// Whether the file is still over budget after every trimmable section was trimmed.
        /// </summary>
        public bool OverBudget { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public InstructionsComposition()
        {
        }

        #endregion
    }
}
