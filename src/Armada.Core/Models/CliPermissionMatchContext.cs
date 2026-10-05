namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// What a path rule is resolved against: the captain's working directory (the mission dock) and the home directory.
    /// Relative path rules never allow when the working directory is unknown (deny rules then match any path suffix).
    /// </summary>
    public class CliPermissionMatchContext
    {
        #region Public-Members

        /// <summary>
        /// Absolute working directory of the captain, or null when unknown.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Absolute home directory used for <c>~/</c> rules, or null when unknown.
        /// </summary>
        public string? HomeDirectory { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public CliPermissionMatchContext()
        {
        }

        #endregion
    }
}
