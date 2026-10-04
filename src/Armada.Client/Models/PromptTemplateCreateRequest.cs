namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Request to create a prompt template.
    /// </summary>
    public class PromptTemplateCreateRequest
    {
        #region Public-Members

        /// <summary>
        /// Template name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Category.
        /// </summary>
        public string Category { get; set; } = "";

        /// <summary>
        /// Template content.
        /// </summary>
        public string Content { get; set; } = "";

        /// <summary>
        /// Description, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Active flag, or null.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Scope, or null.
        /// </summary>
        public Armada.Core.Enums.ScopeEnum? Scope { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromptTemplateCreateRequest()
        {
        }

        #endregion
    }
}
