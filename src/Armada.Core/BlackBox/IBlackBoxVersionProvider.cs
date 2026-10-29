namespace Armada.Core.BlackBox
{
    /// <summary>
    /// Build identity recorded on every black box entry. The Admiral supplies BuildIdentity's values; Core falls back to
    /// <see cref="AssemblyBlackBoxVersionProvider"/>.
    /// </summary>
    public interface IBlackBoxVersionProvider
    {
        /// <summary>
        /// Armada informational version (for example 1.2.0+abc1234).
        /// </summary>
        string Version { get; }

        /// <summary>
        /// Build commit, or null when unknown.
        /// </summary>
        string? Commit { get; }
    }
}
