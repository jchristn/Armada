namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Configuration section: effective values that shaped the failure, each with its source.
    /// </summary>
    public class BlackBoxConfiguration
    {
        /// <summary>
        /// Effective values.
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxConfigValues, Reason = "Capped by BlackBoxRecordBounder")]
        public List<BlackBoxConfigValue> Values
        {
            get => _Values;
            set => _Values = value ?? new List<BlackBoxConfigValue>();
        }

        private List<BlackBoxConfigValue> _Values = new List<BlackBoxConfigValue>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxConfiguration()
        {
        }
    }
}
