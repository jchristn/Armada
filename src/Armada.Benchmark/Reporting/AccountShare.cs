namespace Armada.Benchmark.Reporting
{
    using System.Text.Json.Serialization;

    /// <summary>Admissions that landed on one synthetic account during an arm, and its reservation capacity.</summary>
    public sealed class AccountShare
    {
        /// <summary>Synthetic account name.</summary>
        [JsonPropertyName("account")]
        public string Account { get; set; } = "";

        /// <summary>Reported used percent.</summary>
        [JsonPropertyName("usedPercent")]
        public double UsedPercent { get; set; }

        /// <summary>Missions admitted on captains of this account.</summary>
        [JsonPropertyName("admissions")]
        public int Admissions { get; set; }

        /// <summary>Attempts the account can hold above the reserve: floor((100 - used - reserve) / perAttempt).</summary>
        [JsonPropertyName("capacity")]
        public int Capacity { get; set; }

        /// <summary>
        /// Highest Armada reservation estimate (active plus unsettled percent) the account published while the arm ran,
        /// sampled once a second from <c>GET /api/v1/usage/latest</c>.
        /// </summary>
        [JsonPropertyName("maxReservedPercent")]
        public double MaxReservedPercent { get; set; }

        /// <summary>Headroom above the protected reserve: 100 - used - reserve.</summary>
        [JsonPropertyName("headroomPercent")]
        public double HeadroomPercent { get; set; }

        /// <summary>True when, in Enforce, the published reservation estimate exceeded the headroom above the reserve.</summary>
        [JsonPropertyName("violation")]
        public bool Violation { get; set; }
    }
}
