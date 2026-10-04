namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// Request to transition a mission to a new status.
    /// </summary>
    public class TransitionRequest
    {
        #region Public-Members

        /// <summary>
        /// Target status name.
        /// </summary>
        public string Status { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public TransitionRequest()
        {
        }

        #endregion
    }
}
