namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.BlackBox;

    /// <summary>
    /// Path success rates fixed by a test.
    /// </summary>
    public sealed class FixedBlackBoxPathStats : IBlackBoxPathStats
    {
        /// <summary>Rates by path id.</summary>
        public Dictionary<string, double> Rates { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

        /// <inheritdoc />
        public bool TryGetRate(string pathId, out double rate, out int settledRuns)
        {
            settledRuns = 0;
            if (!Rates.TryGetValue(pathId, out rate)) return false;
            settledRuns = 5;
            return true;
        }
    }
}
