namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Request to update a prompt template.
    /// </summary>
    public class PromptTemplateUpdateRequest
    {
        #region Public-Members

        /// <summary>
        /// Template content.
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Scope, or null.
        /// </summary>
        public Armada.Core.Enums.ScopeEnum? Scope { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromptTemplateUpdateRequest()
        {
        }

        #endregion
    }
}
