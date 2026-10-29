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
    /// A bounded set of object references for a bulk delete.
    /// </summary>
    public class BlackBoxObjectRefSet
    {
        /// <summary>
        /// References (at most 1000).
        /// </summary>
        [CollectionBound(CollectionLimits.MaxRequestItems)]
        public List<BlackBoxObjectRef> References
        {
            get => _References;
            set => _References = value ?? new List<BlackBoxObjectRef>();
        }

        private List<BlackBoxObjectRef> _References = new List<BlackBoxObjectRef>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxObjectRefSet()
        {
        }
    }
}
