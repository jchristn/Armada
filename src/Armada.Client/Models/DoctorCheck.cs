namespace Armada.Client.Models
{
    using System;
    using Armada.Core.Enums;

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
        /// Check result.
        /// </summary>
        public DoctorCheckStatusEnum Status { get; set; } = DoctorCheckStatusEnum.Pass;

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
