namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>Paged mission rows and coverage metadata.</summary>
    public class UsageMissionResult : EnumerationResult<UsageMissionRow>
    {
        /// <summary>Coverage of the bounded drilldown.</summary>
        public UsageCoverage Coverage { get; set; } = new UsageCoverage();

    }
}
