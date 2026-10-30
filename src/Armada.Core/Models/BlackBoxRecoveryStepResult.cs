namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Result of one recovery step.
    /// </summary>
    public class BlackBoxRecoveryStepResult
    {
        /// <summary>
        /// Step order.
        /// </summary>
        public int Order { get; set; } = 0;

        /// <summary>
        /// Tool run, or null for a manual step.
        /// </summary>
        public string? Tool { get; set; } = null;

        /// <summary>
        /// Whether it succeeded.
        /// </summary>
        public bool Succeeded { get; set; } = false;

        /// <summary>
        /// Result or error message.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRecoveryStepResult()
        {
        }
    }
}
