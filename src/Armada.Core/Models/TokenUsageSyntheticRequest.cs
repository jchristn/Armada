namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using Armada.Core.Services;

    /// <summary>Bounded synthetic token import. Reusing a batch with identical ordered records is idempotent.</summary>
    public class TokenUsageSyntheticRequest
    {
        /// <summary>Caller-generated batch slug, 1-80 letters, digits, underscores or hyphens.</summary>
        public string BatchId { get; set; } = "";
        /// <summary>One to 500 simulated records; tenant/user/source attribution is assigned by the server.</summary>
        [CollectionBound(500)]
        public List<TokenUsageSyntheticRecord> Records { get; set; } = new List<TokenUsageSyntheticRecord>();
    }
}
