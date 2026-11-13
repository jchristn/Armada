namespace Armada.Core.Database.Sqlite.Implementations
{
    using Microsoft.Data.Sqlite;

    partial class MissionMethods
    {
        /// <inheritdoc />
        public async Task<int> CountExecutionSlotsAsync(CancellationToken token = default)
        {
            using SqliteConnection connection = new SqliteProviderConnection(_Driver.ConnectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM missions WHERE status IN ('Assigned','InProgress');";
            return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
        }
    }
}
