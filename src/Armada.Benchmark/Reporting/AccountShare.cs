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

        /// <summary>Reservations the account can hold above the reserve: floor((100 - used - reserve) / perAttempt).</summary>
        [JsonPropertyName("capacity")]
        public int Capacity { get; set; }

        /// <summary>
        /// Most reservations the account held at one admission, counting an attempt as settled only once a capture of the
        /// account followed its completion. Lenient toward the product (the real reconciler settles later), so a
        /// value above the capacity is a real over-admission.
        /// </summary>
        [JsonPropertyName("maxOutstanding")]
        public int MaxOutstanding { get; set; }

        /// <summary>True when an Enforce arm held more reservations than the capacity.</summary>
        [JsonPropertyName("violation")]
        public bool Violation { get; set; }
    }
}
