namespace Armada.Core.Database.Mysql.Implementations
{
    using MySqlConnector;

    partial class MissionMethods
    {
        /// <inheritdoc />
        public async Task<int> CountExecutionSlotsAsync(CancellationToken token = default)
        {
            using MySqlConnection connection = new MySqlConnection(_ConnectionString);
            await connection.OpenAsync(token).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM missions WHERE status IN ('Assigned','InProgress');";
            return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
        }
    }
}
