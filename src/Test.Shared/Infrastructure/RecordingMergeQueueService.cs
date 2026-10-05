namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Merge queue stand-in for landing-handler tests: records enqueued entries and otherwise does nothing.
    /// </summary>
    public sealed class RecordingMergeQueueService : IMergeQueueService
    {
        #region Public-Members

        /// <summary>
        /// Entries passed to <see cref="EnqueueAsync"/>, in order.
        /// </summary>
        public List<MergeEntry> Enqueued { get; } = new List<MergeEntry>();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<MergeEntry> EnqueueAsync(MergeEntry entry, CancellationToken token = default)
        {
            Enqueued.Add(entry);
            return Task.FromResult(entry);
        }

        /// <inheritdoc />
        public Task ProcessQueueAsync(CancellationToken token = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task CancelAsync(string entryId, string? tenantId = null, CancellationToken token = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task<List<MergeEntry>> ListAsync(string? tenantId = null, CancellationToken token = default) => Task.FromResult(new List<MergeEntry>(Enqueued));

        /// <inheritdoc />
        public Task<MergeEntry?> ProcessSingleAsync(string entryId, string? tenantId = null, CancellationToken token = default) => Task.FromResult<MergeEntry?>(null);

        /// <inheritdoc />
        public Task<MergeEntry?> GetAsync(string entryId, string? tenantId = null, CancellationToken token = default)
            => Task.FromResult<MergeEntry?>(Enqueued.Find(e => e.Id == entryId));

        /// <inheritdoc />
        public Task<bool> DeleteAsync(string entryId, string? tenantId = null, CancellationToken token = default) => Task.FromResult(false);

        /// <inheritdoc />
        public Task<MergeQueuePurgeResult> DeleteMultipleAsync(List<string> entryIds, string? tenantId = null, CancellationToken token = default)
            => Task.FromResult(new MergeQueuePurgeResult());

        /// <inheritdoc />
        public Task<int> PurgeTerminalAsync(string? vesselId = null, MergeStatusEnum? status = null, string? tenantId = null, CancellationToken token = default)
            => Task.FromResult(0);

        #endregion
    }
}
