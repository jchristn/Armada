namespace Armada.Core.Database.Mysql.Implementations
{
    using MySqlConnector;

    partial class CaptainMethods
    {
        private async Task<T> WithSlotsAsync<T>(Func<CaptainSlotStore, Task<T>> action, CancellationToken token)
        {
            using (MySqlConnection connection = new MySqlConnection(_ConnectionString))
            {
                await connection.OpenAsync(token).ConfigureAwait(false);
                return await action(new CaptainSlotStore(connection)).ConfigureAwait(false);
            }
        }

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
