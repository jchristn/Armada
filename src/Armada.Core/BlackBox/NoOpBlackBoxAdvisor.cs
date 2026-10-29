namespace Armada.Core.BlackBox
{
    using Armada.Core.Models;

    /// <summary>
    /// Advisor with nothing to say (no findings, rules version 0): the pipeline default when no advisor is set, and a test stand-in. BlackBoxRuntime installs <see cref="BlackBoxAdvisor"/>. Thread-safe.
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
