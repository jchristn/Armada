namespace Armada.Publisher.Channels
{
    using System;
    using System.Collections.Generic;
    using Armada.Publisher.Manifest;
    using Armada.Publisher.Preflight;

    /// <summary>
    /// Placeholder for a channel whose recipe has not yet been authored. It surfaces the tools the channel
    /// will require (so Doctor stays honest) and fails loudly with guidance when asked to run, rather than
    /// silently reporting a package that was never built.
    /// </summary>
    public class StubChannel : IChannel
    {
        #region Private-Members

        private readonly ChannelKindEnum _Kind;
        private readonly List<ToolRequirement> _Requirements;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate a stub channel.
        /// </summary>
        /// <param name="kind">The channel kind this stub stands in for.</param>
        /// <param name="requirements">The tools the eventual implementation will need.</param>
        public StubChannel(ChannelKindEnum kind, IEnumerable<ToolRequirement> requirements)
        {
            _Kind = kind;
            _Requirements = requirements != null ? new List<ToolRequirement>(requirements) : new List<ToolRequirement>();
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public ChannelKindEnum Kind
        {
            get { return _Kind; }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public IEnumerable<ToolRequirement> Requirements()
        {
            return _Requirements;
        }

        /// <inheritdoc />
        public void Execute(ChannelContext context)
        {
            throw new NotImplementedException(
                "Channel kind '" + _Kind + "' is declared in publisher.json but its recipe is not yet implemented. "
                + "Author it under src/Armada.Publisher/Channels and register it in ChannelFactory. "
                + "See requirements/INSTALLERS.md for the format contract.");
        }

        #endregion
    }
}
