namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Response from DELETE /api/v1/captains/{id} or armada_delete_captain.
    /// </summary>
    public class DeleteCaptainResponse
    {
        /// <summary>
        /// Operation status (e.g. "deleted").
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// ID of the deleted captain.
        /// </summary>
        public string? CaptainId { get; set; }

        /// <summary>
        /// Error message if the operation failed.
        /// </summary>
        public string? Error { get; set; }

        /// <summary>
        /// Error detail message.
        /// </summary>
        public string? Message { get; set; }
    }
}
