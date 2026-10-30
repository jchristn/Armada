namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// A redaction rule and how often it fired.
    /// </summary>
    public class BlackBoxRedactionRuleCount
    {
        /// <summary>
        /// Rule name.
        /// </summary>
        public string Rule { get; set; } = "";

        /// <summary>
        /// Count.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxRedactionRuleCount()
        {
        }
    }
}
