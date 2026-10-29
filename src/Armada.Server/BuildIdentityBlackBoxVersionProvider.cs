namespace Armada.Server
{
    using Armada.Core.BlackBox;

    /// <summary>
    /// Supplies the Admiral's <see cref="BuildIdentity"/> to the black box, so every entry records the Armada version and
    /// build commit that captured it.
    /// </summary>
    public class BuildIdentityBlackBoxVersionProvider : IBlackBoxVersionProvider
    {
        /// <inheritdoc />
        public string Version => BuildIdentity.InformationalVersion;

        /// <inheritdoc />
        public string? Commit => BuildIdentity.CommitHash;
    }
}
