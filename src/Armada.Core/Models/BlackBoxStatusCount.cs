namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A status and a count.
    /// </summary>
    public class BlackBoxStatusCount
    {
        /// <summary>
        /// Status name.
        /// </summary>
        public string Status { get; set; } = "";

        /// <summary>
        /// Count.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxStatusCount()
        {
        }
    }
}
