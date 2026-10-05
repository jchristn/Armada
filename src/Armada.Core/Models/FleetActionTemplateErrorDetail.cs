namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Machine-readable detail carried in the Data field of a fleet action error response when a command or prompt
    /// template references a variable the renderer does not support.
    /// </summary>
    public class FleetActionTemplateErrorDetail
    {
        #region Public-Members

        /// <summary>
        /// Stable error code for an unknown template variable.
        /// </summary>
        public const string UnknownTemplateVariableCode = "UnknownTemplateVariable";

        /// <summary>
        /// Stable error code, always <see cref="UnknownTemplateVariableCode"/>.
        /// </summary>
        public string Code { get; set; } = UnknownTemplateVariableCode;

        /// <summary>
        /// The unknown variable name exactly as it appeared between the braces (trimmed), never null.
        /// </summary>
        public string VariableName
        {
            get => _VariableName;
            set => _VariableName = value ?? String.Empty;
        }

        #endregion

        #region Private-Members

        private string _VariableName = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public FleetActionTemplateErrorDetail()
        {
        }

        /// <summary>
        /// Instantiate for an unknown variable.
        /// </summary>
        /// <param name="variableName">The unknown variable name.</param>
        public FleetActionTemplateErrorDetail(string variableName)
        {
            VariableName = variableName;
        }

        #endregion
    }
}
