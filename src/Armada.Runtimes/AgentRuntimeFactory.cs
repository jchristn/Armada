namespace Armada.Runtimes
{
    using SyslogLogging;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Runtimes.Interfaces;

    /// <summary>
    /// Factory for creating agent runtime instances.
    /// </summary>
    public class AgentRuntimeFactory
    {
        #region Public-Members

        #endregion

        #region Private-Members

        private string _Header = "[AgentRuntimeFactory] ";
        private LoggingModule _Logging;
        private Func<string, ModelEndpoint?>? _EndpointResolver;
        private Dictionary<string, Func<IAgentRuntime>> _CustomRuntimes = new Dictionary<string, Func<IAgentRuntime>>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <param name="endpointResolver">Optional resolver mapping a captain's model-endpoint id to a
        /// configured <see cref="ModelEndpoint"/>, enabling API-endpoint captains. Null disables them.</param>
        public AgentRuntimeFactory(LoggingModule logging, Func<string, ModelEndpoint?>? endpointResolver = null)
        {
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _EndpointResolver = endpointResolver;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create an agent runtime by type.
        /// </summary>
        /// <param name="runtimeType">Runtime type.</param>
        /// <returns>Agent runtime instance.</returns>
        public IAgentRuntime Create(AgentRuntimeEnum runtimeType)
        {
            switch (runtimeType)
            {
                case AgentRuntimeEnum.ClaudeCode:
                    return new ClaudeCodeRuntime(_Logging);
                case AgentRuntimeEnum.Codex:
                    return new CodexRuntime(_Logging);
                case AgentRuntimeEnum.Gemini:
                    return new GeminiRuntime(_Logging);
                case AgentRuntimeEnum.Cursor:
                    return new CursorRuntime(_Logging);
                case AgentRuntimeEnum.Mux:
                    return new MuxRuntime(_Logging);
                case AgentRuntimeEnum.OpenCode:
                    return new OpenCodeRuntime(_Logging);
                case AgentRuntimeEnum.ApiEndpoint:
                    if (_EndpointResolver == null)
                        throw new InvalidOperationException("API-endpoint captains are not enabled: no model-endpoint resolver was configured.");
                    return new ApiAgentRuntime(_EndpointResolver, _Logging);
                case AgentRuntimeEnum.Custom:
                    throw new InvalidOperationException("Use Create(string name) for custom runtimes");
                default:
                    throw new ArgumentOutOfRangeException(nameof(runtimeType), "Unknown runtime type: " + runtimeType);
            }
        }

        /// <summary>
        /// Create a custom agent runtime by name.
        /// </summary>
        /// <param name="name">Custom runtime name.</param>
        /// <returns>Agent runtime instance.</returns>
        public IAgentRuntime Create(string name)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            if (_CustomRuntimes.TryGetValue(name, out Func<IAgentRuntime>? factory))
            {
                return factory();
            }

            throw new InvalidOperationException("No custom runtime registered with name: " + name);
        }

        /// <summary>
        /// Register a custom runtime factory.
        /// </summary>
        /// <param name="name">Custom runtime name.</param>
        /// <param name="factory">Factory function to create the runtime.</param>
        public void Register(string name, Func<IAgentRuntime> factory)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            _CustomRuntimes[name] = factory;
            _Logging.Debug(_Header + "registered custom runtime: " + name);
        }

        #endregion
    }
}
