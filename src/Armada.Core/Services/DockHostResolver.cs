namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using SyslogLogging;

    /// <summary>
    /// Maps a dock to the host it lives on. A dock created on a Harbor (it records the Harbor and the repository on that
    /// Harbor's host) is worked on through that Harbor; every other dock, including one created on the Admiral and only
    /// launched by a Harbor that shares the Admiral's filesystem, is worked on locally.
    /// </summary>
    public class DockHostResolver
    {
        #region Public-Members

        /// <summary>
        /// The Admiral's own host.
        /// </summary>
        public DockHost Local { get; }

        /// <summary>
        /// The Harbor connections, or null when Harbor delegation is not available.
        /// </summary>
        public HarborConnectionManager? Harbors { get; }

        #endregion

        #region Private-Members

        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="localGit">Git service for the Admiral's host.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="harbors">Harbor connections, or null.</param>
        public DockHostResolver(IGitService localGit, LoggingModule logging, HarborConnectionManager? harbors)
        {
            if (localGit == null) throw new ArgumentNullException(nameof(localGit));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            Harbors = harbors;
            Local = new DockHost(null, localGit, new LocalHostCommandExecutor(), new LocalDockFileSystem());
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a dock was created on a Harbor (rather than on the Admiral's host).
        /// </summary>
        /// <param name="dock">Dock, or null.</param>
        /// <returns>True for a Harbor-hosted dock.</returns>
        public static bool IsHarborDock(Dock? dock)
        {
            return dock != null && !String.IsNullOrWhiteSpace(dock.HarborId) && !String.IsNullOrWhiteSpace(dock.RepositoryPath);
        }

        /// <summary>
        /// The host a dock lives on.
        /// </summary>
        /// <param name="dock">Dock, or null (the Admiral's host).</param>
        /// <returns>The host.</returns>
        /// <exception cref="InvalidOperationException">Thrown for a Harbor-hosted dock when Harbor delegation is not
        /// available on this Admiral.</exception>
        public DockHost ForDock(Dock? dock)
        {
            if (!IsHarborDock(dock)) return Local;
            return ForHarbor(dock!.HarborId!);
        }

        /// <summary>
        /// A Harbor's host. Operations fail with <see cref="InvalidOperationException"/> while the Harbor is not connected.
        /// </summary>
        /// <param name="harborId">Harbor identifier.</param>
        /// <returns>The host.</returns>
        public DockHost ForHarbor(string harborId)
        {
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (Harbors == null) throw new InvalidOperationException("Harbor " + harborId + " owns this dock, but Harbor delegation is not available on this Admiral.");
            RemoteHostCommandExecutor commands = new RemoteHostCommandExecutor(Harbors, harborId);
            return new DockHost(harborId, new GitService(_Logging, commands), commands, new HarborDockFileSystem(Harbors, harborId));
        }

        #endregion
    }
}
