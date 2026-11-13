namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// Observed success rates of recovery paths, read synchronously by the advisor to break ties between equally
    /// weighted paths. Implementations hold a snapshot; lookups never do I/O.
    /// </summary>
    public interface IBlackBoxPathStats
    {
        /// <summary>
        /// The success rate of a path when enough runs have settled.
        /// </summary>
        /// <param name="pathId">Recovery path id.</param>
        /// <param name="rate">Succeeded over settled runs (0 to 1).</param>
        /// <param name="settledRuns">Settled runs behind the rate.</param>
        /// <returns>True when the path has a rate.</returns>
        bool TryGetRate(string pathId, out double rate, out int settledRuns);
    }
}
