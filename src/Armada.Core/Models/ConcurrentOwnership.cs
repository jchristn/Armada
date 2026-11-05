namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Paths owned by a logical mission that can run at the same time as another.</summary>
    public class ConcurrentOwnership
    {
        /// <summary>The logical mission's root.</summary>
        public string MissionId { get; set; } = String.Empty;

        /// <summary>Its title.</summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>Paths it owns.</summary>
        [Armada.Core.Services.CollectionBound(Armada.Core.Services.CollectionLimits.MaxConfigItems)]
        public List<string> Paths { get; set; } = new List<string>();
    }
}
