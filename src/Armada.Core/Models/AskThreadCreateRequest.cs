namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// Body of POST /api/v1/ask/threads.
    /// </summary>
    public class AskThreadCreateRequest
    {
        #region Public-Members

        /// <summary>
        /// Optional title; defaults to "New conversation" and is replaced by the first message when left default.
        /// </summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Optional captain (cpt_ prefix) visible to the caller.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Optional auto-approve flag. Default false.
        /// </summary>
        public bool? AutoApprove { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskThreadCreateRequest()
        {
        }

        #endregion
    }
}
