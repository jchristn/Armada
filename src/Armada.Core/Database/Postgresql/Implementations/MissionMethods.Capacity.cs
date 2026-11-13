namespace Armada.Core.Database.Postgresql.Implementations
{
    using Npgsql;

    partial class MissionMethods
    {
        /// <inheritdoc />
        public async Task<int> CountExecutionSlotsAsync(CancellationToken token = default)
        {
            using NpgsqlConnection connection = new NpgsqlConnection(_Settings.GetConnectionString());
            await connection.OpenAsync(token).ConfigureAwait(false);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM missions WHERE status IN ('Assigned','InProgress');";
            return Convert.ToInt32(await command.ExecuteScalarAsync(token).ConfigureAwait(false));
        }
    }
}
