namespace Armada.Core.Database.SqlServer.Implementations
{
    using Microsoft.Data.SqlClient;

    partial class CaptainMethods
    {
        private async Task<T> WithSlotsAsync<T>(Func<CaptainSlotStore, Task<T>> action, CancellationToken token)
        {
            using (SqlConnection connection = new SqlConnection(_Driver.ConnectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                return await action(new CaptainSlotStore(connection)).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public Task<bool> TryClaimExclusiveAsync(string captainId, string sessionId, Armada.Core.Enums.CaptainStateEnum state, CancellationToken token = default, bool recover = false)
            => WithSlotsAsync(store => store.ClaimExclusiveAsync(captainId, sessionId, state, recover, token), token);

        /// <inheritdoc />
        public Task<bool> TryUpdateExclusiveAsync(string captainId, string sessionId, Armada.Core.Enums.CaptainStateEnum state, int? processId, CancellationToken token = default)
            => WithSlotsAsync(store => store.UpdateExclusiveAsync(captainId, sessionId, state, processId, token), token);

        /// <inheritdoc />
        public Task<bool> TryReleaseExclusiveAsync(string captainId, string sessionId, CancellationToken token = default)
            => WithSlotsAsync(store => store.ReleaseExclusiveAsync(captainId, sessionId, token), token);

        /// <inheritdoc />
        public Task<bool> ReleaseVerificationCapacityAsync(string captainId, string missionId, int processId, DateTime version, CancellationToken token = default)
            => WithSlotsAsync(store => store.ReleaseVerificationCapacityAsync(captainId, missionId, processId, version, token), token);

        /// <inheritdoc />
        public Task<bool> TryResumeVerificationAsync(string captainId, string missionId, DateTime version, int ceiling, CancellationToken token = default)
            => WithSlotsAsync(store => store.ResumeVerificationAsync(captainId, missionId, version, ceiling, token), token);

        /// <inheritdoc />
        public Task<int?> ReadMissionProcessAsync(string captainId, string missionId, CancellationToken token = default)
            => WithSlotsAsync(store => store.ReadProcessAsync(captainId, missionId, token), token);

        /// <inheritdoc />
        public async Task ReconcileMissionSlotsAsync(CancellationToken token = default)
        {
            await WithSlotsAsync(async store => { await store.ReconcileAsync(token).ConfigureAwait(false); return true; }, token).ConfigureAwait(false);
        }
    }
}
