namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Objective refinement session persistence decorator that runs a test hook immediately after every session
    /// update reaches the database and before the writer continues. A test uses it to act at the exact moment a
    /// state change becomes durable and visible to other callers, deterministically and without sleeps.
    /// </summary>
    public sealed class HookedObjectiveRefinementSessionMethods : IObjectiveRefinementSessionMethods
    {
        #region Public-Members

        /// <summary>
        /// Invoked after each update with the persisted session. Awaited before the update returns to its caller.
        /// Null disables the hook.
        /// </summary>
        public Func<ObjectiveRefinementSession, Task>? AfterUpdateAsync { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly IObjectiveRefinementSessionMethods _Inner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="inner">Real session methods.</param>
        public HookedObjectiveRefinementSessionMethods(IObjectiveRefinementSessionMethods inner)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<ObjectiveRefinementSession> CreateAsync(ObjectiveRefinementSession session, CancellationToken token = default)
        {
            return _Inner.CreateAsync(session, token);
        }

        /// <inheritdoc />
        public async Task<ObjectiveRefinementSession> UpdateAsync(ObjectiveRefinementSession session, CancellationToken token = default)
        {
            ObjectiveRefinementSession updated = await _Inner.UpdateAsync(session, token).ConfigureAwait(false);
            Func<ObjectiveRefinementSession, Task>? hook = AfterUpdateAsync;
            if (hook != null) await hook(updated).ConfigureAwait(false);
            return updated;
        }

        /// <inheritdoc />
        public Task<ObjectiveRefinementSession?> ReadAsync(string id, CancellationToken token = default)
        {
            return _Inner.ReadAsync(id, token);
        }

        /// <inheritdoc />
        public Task<ObjectiveRefinementSession?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            return _Inner.ReadAsync(tenantId, id, token);
        }

        /// <inheritdoc />
        public Task<ObjectiveRefinementSession?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            return _Inner.ReadAsync(tenantId, userId, id, token);
        }

        /// <inheritdoc />
        public Task DeleteAsync(string id, CancellationToken token = default)
        {
            return _Inner.DeleteAsync(id, token);
        }

        /// <inheritdoc />
        public Task<List<ObjectiveRefinementSession>> EnumerateAsync(CancellationToken token = default)
        {
            return _Inner.EnumerateAsync(token);
        }

        /// <inheritdoc />
        public Task<List<ObjectiveRefinementSession>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            return _Inner.EnumerateAsync(tenantId, token);
        }

        /// <inheritdoc />
        public Task<List<ObjectiveRefinementSession>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            return _Inner.EnumerateAsync(tenantId, userId, token);
        }

        /// <inheritdoc />
        public Task<List<ObjectiveRefinementSession>> EnumerateByObjectiveAsync(string objectiveId, CancellationToken token = default)
        {
            return _Inner.EnumerateByObjectiveAsync(objectiveId, token);
        }

        /// <inheritdoc />
        public Task<List<ObjectiveRefinementSession>> EnumerateByCaptainAsync(string captainId, CancellationToken token = default)
        {
            return _Inner.EnumerateByCaptainAsync(captainId, token);
        }

        /// <inheritdoc />
        public Task<List<ObjectiveRefinementSession>> EnumerateByStatusAsync(ObjectiveRefinementSessionStatusEnum status, CancellationToken token = default)
        {
            return _Inner.EnumerateByStatusAsync(status, token);
        }

        #endregion
    }
}
