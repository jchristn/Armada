namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// SQLite implementation of event database operations.
    /// </summary>
    public class EventMethods : IEventMethods
    {
        #region Private-Members

#pragma warning disable CS0414
        private readonly string _Header = "[EventMethods] ";
#pragma warning restore CS0414
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public EventMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<ArmadaEvent> CreateAsync(ArmadaEvent armadaEvent, CancellationToken token = default)
        {
            if (armadaEvent == null) throw new ArgumentNullException(nameof(armadaEvent));

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO events (id, event_type, entity_type, entity_id, captain_id, mission_id, vessel_id, voyage_id, message, payload, created_utc)
                            VALUES (@id, @event_type, @entity_type, @entity_id, @captain_id, @mission_id, @vessel_id, @voyage_id, @message, @payload, @created_utc);";
                    cmd.Parameters.AddWithValue("@id", armadaEvent.Id);
                    cmd.Parameters.AddWithValue("@event_type", armadaEvent.EventType);
                    cmd.Parameters.AddWithValue("@entity_type", (object?)armadaEvent.EntityType ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@entity_id", (object?)armadaEvent.EntityId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@captain_id", (object?)armadaEvent.CaptainId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@mission_id", (object?)armadaEvent.MissionId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@vessel_id", (object?)armadaEvent.VesselId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@voyage_id", (object?)armadaEvent.VoyageId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@message", armadaEvent.Message);
                    cmd.Parameters.AddWithValue("@payload", (object?)armadaEvent.Payload ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(armadaEvent.CreatedUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return armadaEvent;
        }

        /// <inheritdoc />
        public async Task<ArmadaEvent?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM events WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return SqliteDatabaseDriver.EventFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM events WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateRecentAsync(int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events ORDER BY created_utc DESC LIMIT @limit;",
                cmd => cmd.Parameters.AddWithValue("@limit", limit), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByTypeAsync(string eventType, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE event_type = @event_type ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@event_type", eventType);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByEntityAsync(string entityType, string entityId, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE entity_type = @entity_type AND entity_id = @entity_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@entity_type", entityType);
                    cmd.Parameters.AddWithValue("@entity_id", entityId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByCaptainAsync(string captainId, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE captain_id = @captain_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@captain_id", captainId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByMissionAsync(string missionId, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE mission_id = @mission_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@mission_id", missionId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByVesselAsync(string vesselId, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE vessel_id = @vessel_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByVoyageAsync(string voyageId, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE voyage_id = @voyage_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@voyage_id", voyageId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ArmadaEvent>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) query = new EnumerationQuery();

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<SqliteParameter> parameters = new List<SqliteParameter>();

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new SqliteParameter("@created_after", SqliteDatabaseDriver.ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new SqliteParameter("@created_before", SqliteDatabaseDriver.ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.EventType))
                {
                    conditions.Add("event_type = @event_type");
                    parameters.Add(new SqliteParameter("@event_type", query.EventType));
                }
                if (!string.IsNullOrEmpty(query.CaptainId))
                {
                    conditions.Add("captain_id = @captain_id");
                    parameters.Add(new SqliteParameter("@captain_id", query.CaptainId));
                }
                if (!string.IsNullOrEmpty(query.MissionId))
                {
                    conditions.Add("mission_id = @mission_id");
                    parameters.Add(new SqliteParameter("@mission_id", query.MissionId));
                }
                if (!string.IsNullOrEmpty(query.VesselId))
                {
                    conditions.Add("vessel_id = @vessel_id");
                    parameters.Add(new SqliteParameter("@vessel_id", query.VesselId));
                }
                if (!string.IsNullOrEmpty(query.VoyageId))
                {
                    conditions.Add("voyage_id = @voyage_id");
                    parameters.Add(new SqliteParameter("@voyage_id", query.VoyageId));
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM events" + whereClause + ";";
                    foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<ArmadaEvent> results = new List<ArmadaEvent>();
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM events" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqliteDatabaseDriver.EventFromReader(reader));
                    }
                }

                return EnumerationResult<ArmadaEvent>.Create(query, results, totalCount);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<List<ArmadaEvent>> QueryEventsAsync(string sql, Action<SqliteCommand> addParams, CancellationToken token)
        {
            List<ArmadaEvent> results = new List<ArmadaEvent>();

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    addParams(cmd);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(SqliteDatabaseDriver.EventFromReader(reader));
                    }
                }
            }

            return results;
        }

        #endregion
    }
}
