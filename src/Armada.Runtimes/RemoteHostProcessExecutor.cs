namespace Armada.Runtimes
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Services;
    using Armada.Runtimes.Interfaces;

    /// <summary>
    /// Split-mode implementation of the process-executor seam: produces a <see cref="RemoteAgentRuntime"/>
    /// that launches captains on a specific Harbor over its link. The agent lifecycle handler works against
    /// the returned runtime identically to a local one; the process simply runs on the Harbor host.
    /// </summary>
    public class RemoteHostProcessExecutor : IHostProcessExecutor
    {
        #region Private-Members

        private readonly HarborConnectionManager _Manager;
        private readonly string _HarborId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for a specific target Harbor.
        /// </summary>
        /// <param name="manager">Harbor connection manager.</param>
        /// <param name="harborId">Target Harbor identifier.</param>
        public RemoteHostProcessExecutor(HarborConnectionManager manager, string harborId)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            _HarborId = harborId;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IAgentRuntime CreateRuntime(AgentRuntimeEnum runtimeType)
        {
            return new RemoteAgentRuntime(_Manager, _HarborId, runtimeType);
        }

        /// <inheritdoc />
        public IAgentRuntime CreateRuntime(string name)
        {
            if (Enum.TryParse<AgentRuntimeEnum>(name, true, out AgentRuntimeEnum parsed))
                return new RemoteAgentRuntime(_Manager, _HarborId, parsed);
            throw new NotSupportedException("Harbor delegation does not support the custom runtime name: " + name);
        }

        #endregion
    }
}
