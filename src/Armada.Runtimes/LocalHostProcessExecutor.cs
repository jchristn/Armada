namespace Armada.Runtimes
{
    using System;
    using Armada.Core.Enums;
    using Armada.Runtimes.Interfaces;

    /// <summary>
    /// Local-mode (standalone) implementation of the process-executor seam: a pass-through to
    /// <see cref="AgentRuntimeFactory"/>, so captains launch in-process on the Admiral's own host exactly as
    /// before. Split mode substitutes a remote implementation that produces a runtime proxy over a Harbor
    /// link; nothing above the seam changes.
    /// </summary>
    public class LocalHostProcessExecutor : IHostProcessExecutor
    {
        #region Private-Members

        private readonly AgentRuntimeFactory _Factory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="factory">Agent runtime factory.</param>
        public LocalHostProcessExecutor(AgentRuntimeFactory factory)
        {
            _Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IAgentRuntime CreateRuntime(AgentRuntimeEnum runtimeType)
        {
            return _Factory.Create(runtimeType);
        }

        /// <inheritdoc />
        public IAgentRuntime CreateRuntime(string name)
        {
            return _Factory.Create(name);
        }

        #endregion
    }
}
