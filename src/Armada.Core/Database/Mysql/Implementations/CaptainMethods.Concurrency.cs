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
        public Task<int?> ReadMissionProcessAsync(string captainId, string missionId, CancellationToken token = default)
            => WithSlotsAsync(store => store.ReadProcessAsync(captainId, missionId, token), token);

        /// <inheritdoc />
        public async Task ReconcileMissionSlotsAsync(CancellationToken token = default)
        {
            await WithSlotsAsync(async store => { await store.ReconcileAsync(token).ConfigureAwait(false); return true; }, token).ConfigureAwait(false);
        }
    }
}
