namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Resolve (or reopen) a black box entry.
    /// </summary>
    public class BlackBoxResolveRequest
    {
        /// <summary>
        /// Resolution; Open reopens.
        /// </summary>
        public BlackBoxResolutionEnum Resolution { get; set; } = BlackBoxResolutionEnum.Resolved;

        /// <summary>
        /// What fixed it (at most 2000 characters).
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// Link to what fixed it (at most 1000 characters).
        /// </summary>
        public string? Link { get; set; } = null;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxResolveRequest()
        {
        }
    }
}
