namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// Raised when an Admiral self-rebuild cannot proceed or fails: an unresolvable source path, a failed
    /// <c>git worktree</c> setup, a non-zero <c>dotnet publish</c>, or a failed dashboard build. Carries a
    /// contextual message describing which step failed. See docs/SERVER_REBUILD.md.
    /// </summary>
    public class RebuildException : Exception
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Context describing which rebuild step failed.</param>
        public RebuildException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Context describing which rebuild step failed.</param>
        /// <param name="innerException">The underlying exception.</param>
        public RebuildException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
