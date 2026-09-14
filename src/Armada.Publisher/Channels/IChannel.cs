namespace Armada.Publisher.Channels
{
    using System.Collections.Generic;
    using Armada.Publisher.Manifest;
    using Armada.Publisher.Preflight;

    /// <summary>
    /// A single packaging channel. Implementations turn published binaries into one distribution format
    /// (an installer, an OS package, or a package-manager manifest) and, where applicable, submit it.
    /// </summary>
    public interface IChannel
    {
        #region Public-Members

        /// <summary>
        /// The channel kind this implementation handles.
        /// </summary>
        ChannelKindEnum Kind { get; }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The external tools this channel requires, each with the exact command to install it. Used by
        /// the Doctor preflight to report missing tooling instead of failing opaquely mid-run.
        /// </summary>
        /// <returns>Tool requirements for the channel.</returns>
        IEnumerable<ToolRequirement> Requirements();

        /// <summary>
        /// Package (and, where the channel submits, publish) the artifact for the given context.
        /// </summary>
        /// <param name="context">Channel context.</param>
        void Execute(ChannelContext context);

        #endregion
    }
}
