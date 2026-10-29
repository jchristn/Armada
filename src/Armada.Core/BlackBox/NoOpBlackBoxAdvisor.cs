namespace Armada.Core.BlackBox
{
    using Armada.Core.Models;

    /// <summary>
    /// Default advisor until the rule catalog lands: no findings, rules version 0. Thread-safe.
    /// </summary>
    public class NoOpBlackBoxAdvisor : IBlackBoxAdvisor
    {
        /// <inheritdoc />
        public int RulesVersion => 0;

        /// <inheritdoc />
        public BlackBoxAnalysis? Analyze(BlackBoxRecord record)
        {
            return null;
        }
    }
}
