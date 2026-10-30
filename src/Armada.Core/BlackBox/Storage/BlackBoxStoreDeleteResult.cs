namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Result of a bulk store delete.
    /// </summary>
    public class BlackBoxStoreDeleteResult
    {
        /// <summary>
        /// References requested.
        /// </summary>
        public int Requested { get; set; } = 0;

        /// <summary>
        /// References deleted (or already absent).
        /// </summary>
        public int Deleted { get; set; } = 0;

        /// <summary>
        /// References that failed.
        /// </summary>
        public int Failed { get; set; } = 0;

        /// <summary>
        /// Names of the objects that failed (bounded by the request).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems, Reason = "Bounded by the request")]
        public List<string> FailedNames
        {
            get => _FailedNames;
            set => _FailedNames = value ?? new List<string>();
        }

        /// <summary>
        /// First error message, or null.
        /// </summary>
        public string? FirstError { get; set; } = null;

        private List<string> _FailedNames = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStoreDeleteResult()
        {
        }
    }
}
