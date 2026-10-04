namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when a fleet action command or prompt template references a variable outside the fixed set the
    /// renderer supports. Derives from <see cref="ArgumentException"/> so API layers map it to HTTP 400.
    /// </summary>
    public class FleetActionTemplateException : ArgumentException
    {
        #region Public-Members

        /// <summary>
        /// The unknown variable name exactly as it appeared between the braces (trimmed), never null.
        /// </summary>
        public string VariableName { get; } = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for an unknown variable.
        /// </summary>
        /// <param name="variableName">The unknown variable name.</param>
        public FleetActionTemplateException(string variableName)
            : base("Unknown template variable '{{" + (variableName ?? String.Empty) + "}}'. Supported variables: "
                + String.Join(", ", FleetActionTemplateRenderer.SupportedVariables) + ".")
        {
            VariableName = variableName ?? String.Empty;
        }

        #endregion
    }
}
