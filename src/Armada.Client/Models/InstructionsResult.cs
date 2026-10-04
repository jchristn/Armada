namespace Armada.Client.Models
{
    using System;

    /// <summary>
    /// The instructions file written for a mission.
    /// </summary>
    public class InstructionsResult
    {
        #region Public-Members

        /// <summary>
        /// File name.
        /// </summary>
        public string FileName { get; set; } = "";

        /// <summary>
        /// File content.
        /// </summary>
        public string Content { get; set; } = "";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public InstructionsResult()
        {
        }

        #endregion
    }
}
