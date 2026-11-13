namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Run black box garbage collection now (Admin).
    /// </summary>
    public class BlackBoxGcRunRequest
    {
        /// <summary>
        /// When true, report what would be deleted and delete nothing.
        /// </summary>
        public bool DryRun { get; set; } = false;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxGcRunRequest()
        {
        }
    }
}
