namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// State shared by the entries of one vessel and target branch group during one merge queue pass (RS-9.3). It
    /// remembers which repositories were fetched, so a group fetches once per host instead of once per entry: a
    /// successful push updates the repository's remote-tracking ref for the target itself, so later entries integrate
    /// against the new target without another fetch. A push refused because the target moved marks the repository
    /// stale, and the next attempt fetches again.
    /// </summary>
    public class MergeQueueGroupState
    {
        #region Public-Members

        /// <summary>
        /// Fetches this group ran in this pass.
        /// </summary>
        public int Fetches { get; private set; } = 0;

        #endregion

        #region Private-Members

        private readonly HashSet<string> _Fetched = new HashSet<string>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the host's repository still has to be fetched in this pass.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <returns>True when it has not been fetched, or was marked stale since.</returns>
        /// <exception cref="ArgumentNullException">Thrown when host is null.</exception>
        public bool NeedsFetch(MergeQueueHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            return !_Fetched.Contains(Key(host));
        }

        /// <summary>
        /// Record that the host's repository was fetched.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <exception cref="ArgumentNullException">Thrown when host is null.</exception>
        public void MarkFetched(MergeQueueHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            _Fetched.Add(Key(host));
            Fetches++;
        }

        /// <summary>
        /// Mark the host's repository stale, so the next attempt fetches it again.
        /// </summary>
        /// <param name="host">Host.</param>
        /// <exception cref="ArgumentNullException">Thrown when host is null.</exception>
        public void MarkStale(MergeQueueHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            _Fetched.Remove(Key(host));
        }

        #endregion

        #region Private-Methods

        private static string Key(MergeQueueHost host)
        {
            return (host.HarborId ?? "admiral") + "|" + host.RepoPath;
        }

        #endregion
    }
}
