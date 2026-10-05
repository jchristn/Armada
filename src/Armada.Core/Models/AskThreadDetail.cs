namespace Armada.Core.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// A thread with the work it tracks and its pending proposals.
    /// </summary>
    public class AskThreadDetail
    {
        #region Public-Members

        /// <summary>
        /// The thread.
        /// </summary>
        public AskThread Thread
        {
            get => _Thread;
            set => _Thread = value ?? new AskThread();
        }

        /// <summary>
        /// Work tracked by the thread, newest first, each with its latest snapshot.
        /// </summary>
        public List<AskTrackedWork> TrackedWork
        {
            get => _TrackedWork;
            set => _TrackedWork = value ?? new List<AskTrackedWork>();
        }

        /// <summary>
        /// Proposals still waiting for a decision, oldest first.
        /// </summary>
        public List<AskActionProposal> PendingProposals
        {
            get => _PendingProposals;
            set => _PendingProposals = value ?? new List<AskActionProposal>();
        }

        /// <summary>
        /// Pending CLI permission requests of the thread's captain, newest first.
        /// </summary>
        public List<CliPermissionRequest> PendingCliPermissions
        {
            get => _PendingCliPermissions;
            set => _PendingCliPermissions = value ?? new List<CliPermissionRequest>();
        }

        #endregion

        #region Private-Members

        private AskThread _Thread = new AskThread();
        private List<AskTrackedWork> _TrackedWork = new List<AskTrackedWork>();
        private List<AskActionProposal> _PendingProposals = new List<AskActionProposal>();
        private List<CliPermissionRequest> _PendingCliPermissions = new List<CliPermissionRequest>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskThreadDetail()
        {
        }

        #endregion
    }
}
