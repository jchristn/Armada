namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// The default fleet categorization instructions (the import.fleet_categorization prompt template).
    /// </summary>
    public class FleetCategorizationDefaultPrompt
    {
        #region Public-Members

        /// <summary>
        /// Prompt template name. Never null.
        /// </summary>
        public string TemplateName
        {
            get => _TemplateName;
            set => _TemplateName = value ?? String.Empty;
        }

        /// <summary>
        /// Default instructions. Never null.
        /// </summary>
        public string Prompt
        {
            get => _Prompt;
            set => _Prompt = value ?? String.Empty;
        }

        /// <summary>
        /// Run time limit in minutes (Import.CategorizationTimeoutMinutes).
        /// </summary>
        public int TimeoutMinutes { get; set; } = 20;

        #endregion

        #region Private-Members

        private string _TemplateName = String.Empty;
        private string _Prompt = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetCategorizationDefaultPrompt()
        {
        }

        #endregion
    }
}
