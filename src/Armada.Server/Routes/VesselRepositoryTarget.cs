namespace Armada.Server.Routes
{
    using System;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// The repository a vessel branch route works in, and the git that reaches it: the bare clone or working directory on
    /// the Admiral host, or the checkout on a Harbor (through that Harbor's git). When there is none, <see cref="Error"/>
    /// says why.
    /// </summary>
    public class VesselRepositoryTarget
    {
        #region Public-Members

        /// <summary>
        /// Git for the host the repository is on, or null when there is none.
        /// </summary>
        public IGitService? Git { get; }

        /// <summary>
        /// The repository's path on its host, or null when there is none.
        /// </summary>
        public string? Path { get; }

        /// <summary>
        /// Why there is no repository, or null.
        /// </summary>
        public string? Error { get; }

        /// <summary>
        /// Whether a repository was found.
        /// </summary>
        public bool Found
        {
            get { return Git != null && !String.IsNullOrEmpty(Path); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="git">Git for the repository's host, or null.</param>
        /// <param name="path">The repository's path, or null.</param>
        /// <param name="error">Why there is none, or null.</param>
        public VesselRepositoryTarget(IGitService? git, string? path, string? error)
        {
            Git = git;
            Path = path;
            Error = error;
        }

        #endregion
    }
}
