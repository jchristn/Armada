namespace Armada.Core.Models
{
    using System;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A prompt template used to generate agent instructions.
    /// Templates support placeholder parameters such as {MissionId}, {VesselName}, etc.
    /// </summary>
    public class PromptTemplate
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Tenant identifier.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Template name, used as a unique lookup key (e.g. "mission.rules", "persona.worker").
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Human-readable description of what this template is used for.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Template category for grouping (e.g. "mission", "persona", "commit", "landing").
        /// </summary>
        public string Category
        {
            get => _Category;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Category));
                _Category = value;
            }
        }

        /// <summary>
        /// Template content with {Placeholder} parameters for substitution.
        /// </summary>
        public string Content
        {
            get => _Content;
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(Content));
                _Content = value;
            }
        }

        /// <summary>
        /// Whether this is a built-in system template. Built-in templates cannot be deleted, only overridden.
        /// </summary>
        public bool IsBuiltIn { get; set; } = false;

        /// <summary>
        /// Whether the template is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable("ptpl_", 24);
        private string _Name = "unnamed";
        private string _Category = "mission";
        private string _Content = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PromptTemplate()
        {
        }

        /// <summary>
        /// Instantiate with name and content.
        /// </summary>
        /// <param name="name">Template name.</param>
        /// <param name="content">Template content.</param>
        public PromptTemplate(string name, string content)
        {
            Name = name;
            Content = content;
        }

        #endregion
    }
}
