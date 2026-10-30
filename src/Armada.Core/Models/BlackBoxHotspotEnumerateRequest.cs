namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Services;

    /// <summary>
    /// Filters and paging for conflict hot spots (directories that conflict most, per vessel). Ordered by file count descending, then vessel and directory.
    /// </summary>
    public class BlackBoxHotspotEnumerateRequest : EnumerationQuery
    {
        /// <summary>
        /// File role to count (default Conflicted); null counts every role.
        /// </summary>
        public BlackBoxFileRoleEnum? Role { get; set; } = BlackBoxFileRoleEnum.Conflicted;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxHotspotEnumerateRequest()
        {
        }
    }
}
