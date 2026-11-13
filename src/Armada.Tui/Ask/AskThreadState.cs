namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// A thread's detail plus the collections the conversation view needs, each read from its paged enumeration: the
    /// tracked work, the pending proposals, and the pending CLI permission requests. Every page is requested
    /// explicitly until the server's TotalRecords is reached, so nothing is taken from a first page as if it were all.
    /// </summary>
    public class AskThreadState
    {
        #region Public-Members

        /// <summary>
        /// Rows requested per page.
        /// </summary>
        public const int PageSize = 100;

        /// <summary>
        /// The thread detail (counts and running turn).
        /// </summary>
        public AskThreadDetail? Detail { get; set; } = null;

        /// <summary>
        /// Every tracked work item, newest first.
        /// </summary>
        public List<AskTrackedWork> TrackedWork { get; set; } = new List<AskTrackedWork>();

        /// <summary>
        /// Every pending proposal, oldest first.
        /// </summary>
        public List<AskActionProposal> PendingProposals { get; set; } = new List<AskActionProposal>();

        /// <summary>
        /// Every pending CLI permission request of the thread, newest first.
        /// </summary>
        public List<CliPermissionRequest> PendingCliPermissions { get; set; } = new List<CliPermissionRequest>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskThreadState()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read a thread's detail and its collections.
        /// </summary>
        /// <param name="client">API client.</param>
        /// <param name="threadId">Thread ID.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The state; Detail is null when the thread was not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when client or threadId is null.</exception>
        public static async Task<AskThreadState> ReadAsync(ArmadaClient client, string threadId, CancellationToken token = default)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (String.IsNullOrEmpty(threadId)) throw new ArgumentNullException(nameof(threadId));
            AskThreadState state = new AskThreadState();
            state.Detail = await client.GetAskThreadAsync(threadId, token).ConfigureAwait(false);
            if (state.Detail == null) return state;

            if (state.Detail.TrackedWorkCount > 0)
            {
                state.TrackedWork = await ReadAllAsync((page, t) => client.EnumerateAskWorkAsync(threadId, Query(page, null, EnumerationOrderEnum.CreatedDescending), t), token).ConfigureAwait(false);
            }

            if (state.Detail.PendingProposalCount > 0)
            {
                state.PendingProposals = await ReadAllAsync((page, t) => client.EnumerateAskProposalsAsync(threadId, Query(page, AskProposalStatusEnum.Pending.ToString(), EnumerationOrderEnum.CreatedAscending), t), token).ConfigureAwait(false);
            }

            if (state.Detail.PendingCliPermissionCount > 0)
            {
                state.PendingCliPermissions = await ReadAllAsync((page, t) =>
                {
                    CliPermissionRequestEnumerateRequest q = new CliPermissionRequestEnumerateRequest();
                    q.ThreadId = threadId;
                    q.Status = CliPermissionRequestStatusEnum.Pending.ToString();
                    q.PageNumber = page;
                    q.PageSize = PageSize;
                    return client.ListCliPermissionRequestsAsync(q, t);
                }, token).ConfigureAwait(false);
            }

            return state;
        }

        #endregion

        #region Private-Methods

        private static EnumerationQuery Query(int pageNumber, string? status, EnumerationOrderEnum order)
        {
            EnumerationQuery query = new EnumerationQuery();
            query.PageNumber = pageNumber;
            query.PageSize = PageSize;
            query.Status = status;
            query.Order = order;
            return query;
        }

        private static async Task<List<T>> ReadAllAsync<T>(Func<int, CancellationToken, Task<EnumerationResult<T>?>> readPage, CancellationToken token)
        {
            List<T> all = new List<T>();
            int pageNumber = 1;
            while (!token.IsCancellationRequested)
            {
                EnumerationResult<T>? page = await readPage(pageNumber, token).ConfigureAwait(false);
                if (page == null || page.Objects.Count == 0) break;
                all.AddRange(page.Objects);
                if (pageNumber >= page.TotalPages) break;
                pageNumber++;
            }

            return all;
        }

        #endregion
    }
}
