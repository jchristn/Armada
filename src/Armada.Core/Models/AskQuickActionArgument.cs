namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// One field of a quick action form; Name is the MCP argument name.
    /// </summary>
    public class AskQuickActionArgument
    {
        #region Public-Members

        /// <summary>
        /// MCP argument name (camelCase, exactly as the tool expects).
        /// </summary>
        public string Name
        {
            get => _Name;
            set => _Name = value ?? String.Empty;
        }

        /// <summary>
        /// Display label.
        /// </summary>
        public string Label
        {
            get => _Label;
            set => _Label = value ?? String.Empty;
        }

        /// <summary>
        /// Field type: string, text, boolean, integer, vessel, fleet, captain, fleetAction, pipeline, missions (array of { title, description }), or paths (array of strings).
        /// </summary>
        public string Type
        {
            get => _Type;
            set => _Type = String.IsNullOrWhiteSpace(value) ? "string" : value;
        }

        /// <summary>
        /// Whether the field is required.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Help text, or null.
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Default value as text, or null.
        /// </summary>
        public string? DefaultValue { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Name = String.Empty;
        private string _Label = String.Empty;
        private string _Type = "string";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskQuickActionArgument()
        {
        }

        #endregion
    }
}
