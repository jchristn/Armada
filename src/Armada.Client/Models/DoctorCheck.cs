namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// One diagnostics check result.
    /// </summary>
    public class DoctorCheck
    {
        #region Public-Members

        /// <summary>
        /// Check name.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>
        /// Status (Pass, Warn, Fail).
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// Detail message.
        /// </summary>
        public string Message { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DoctorCheck()
        {
        }

        #endregion
    }
}
