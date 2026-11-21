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

        /// <summary>
        /// Optional Harbor (hbr_ prefix) visible to the caller that runs every turn of the conversation, so the
        /// captain works on that Harbor's machine. Null or empty runs turns where Armada picks (usually the Admiral
        /// host).
        /// </summary>
        public string? HarborId { get; set; } = null;

        /// <summary>
        /// Optional folder the captain runs in, on the machine that runs the turn (the Harbor's host, or the Admiral
        /// host when HarborId is not set). A leading ~ expands to that machine's home directory. Trimmed; maximum
        /// 1024 characters. Null or empty runs in a temporary scratch folder.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

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
