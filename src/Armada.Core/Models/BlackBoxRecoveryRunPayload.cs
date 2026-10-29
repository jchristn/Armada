namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Payload of the blackbox.recovery_run event.
    /// </summary>
    public class BlackBoxRecoveryRunPayload
    {
        /// <summary>
        /// Entry.
        /// </summary>
        public string EntryId { get; set; } = "";

        /// <summary>
        /// Recovery path.
        /// </summary>
        public string PathId { get; set; } = "";

        /// <summary>
        /// Whether this was a dry run.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Whether it succeeded.
        /// </summary>
        public bool Succeeded { get; set; } = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecoveryRunPayload()
        {
        }
    }
}
