namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Delete several black box entries.
    /// </summary>
    public class BlackBoxDeleteManyRequest
    {
        /// <summary>
        /// Entry ids (at most 1000).
        /// </summary>
        [CollectionBound(BlackBoxLimits.MaxDeleteIds)]
        public List<string> Ids
        {
            get => _Ids;
            set => _Ids = value ?? new List<string>();
        }

        private List<string> _Ids = new List<string>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxDeleteManyRequest()
        {
        }
    }
}
