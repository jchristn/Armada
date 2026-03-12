namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of event database operations.
    /// </summary>
    public class EventMethods : IEventMethods
    {
        #region Private-Members

        private NpgsqlDataSource _DataSource;
        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the PostgreSQL event methods.
        /// </summary>
        /// <param name="dataSource">NpgsqlDataSource instance.</param>
        public EventMethods(NpgsqlDataSource dataSource)
        {
            _DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create an event.
        /// </summary>
        /// <param name="armadaEvent">Event to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created event.</returns>
        public async Task<ArmadaEvent> CreateAsync(ArmadaEvent armadaEvent, CancellationToken token = default)
        {
            if (armadaEvent == null) throw new ArgumentNullException(nameof(armadaEvent));

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
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
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(armadaEvent.CreatedUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return armadaEvent;
        }

        /// <summary>
        /// Enumerate recent events.
        /// </summary>
        /// <param name="limit">Maximum number of events to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of recent events.</returns>
        public async Task<List<ArmadaEvent>> EnumerateRecentAsync(int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events ORDER BY created_utc DESC LIMIT @limit;",
                cmd => cmd.Parameters.AddWithValue("@limit", limit), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate events filtered by event type.
        /// </summary>
        /// <param name="eventType">Event type to filter by.</param>
        /// <param name="limit">Maximum number of events to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of events matching the type.</returns>
        public async Task<List<ArmadaEvent>> EnumerateByTypeAsync(string eventType, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE event_type = @event_type ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@event_type", eventType);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate events filtered by entity.
        /// </summary>
        /// <param name="entityType">Entity type to filter by.</param>
        /// <param name="entityId">Entity identifier to filter by.</param>
        /// <param name="limit">Maximum number of events to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of events matching the entity.</returns>
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

        /// <summary>
        /// Enumerate events filtered by captain.
        /// </summary>
        /// <param name="captainId">Captain identifier to filter by.</param>
        /// <param name="limit">Maximum number of events to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of events for the captain.</returns>
        public async Task<List<ArmadaEvent>> EnumerateByCaptainAsync(string captainId, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE captain_id = @captain_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@captain_id", captainId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate events filtered by mission.
        /// </summary>
        /// <param name="missionId">Mission identifier to filter by.</param>
        /// <param name="limit">Maximum number of events to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of events for the mission.</returns>
        public async Task<List<ArmadaEvent>> EnumerateByMissionAsync(string missionId, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE mission_id = @mission_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@mission_id", missionId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate events filtered by vessel.
        /// </summary>
        /// <param name="vesselId">Vessel identifier to filter by.</param>
        /// <param name="limit">Maximum number of events to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of events for the vessel.</returns>
        public async Task<List<ArmadaEvent>> EnumerateByVesselAsync(string vesselId, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE vessel_id = @vessel_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate events filtered by voyage.
        /// </summary>
        /// <param name="voyageId">Voyage identifier to filter by.</param>
        /// <param name="limit">Maximum number of events to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of events for the voyage.</returns>
        public async Task<List<ArmadaEvent>> EnumerateByVoyageAsync(string voyageId, int limit = 50, CancellationToken token = default)
        {
            return await QueryEventsAsync("SELECT * FROM events WHERE voyage_id = @voyage_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@voyage_id", voyageId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate events with pagination and filtering.
        /// </summary>
        /// <param name="query">Enumeration query with pagination and filter parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result of events.</returns>
        public async Task<EnumerationResult<ArmadaEvent>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) query = new EnumerationQuery();

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                List<string> conditions = new List<string>();
                List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new NpgsqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new NpgsqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.EventType))
                {
                    conditions.Add("event_type = @event_type");
                    parameters.Add(new NpgsqlParameter("@event_type", query.EventType));
                }
                if (!string.IsNullOrEmpty(query.CaptainId))
                {
                    conditions.Add("captain_id = @captain_id");
                    parameters.Add(new NpgsqlParameter("@captain_id", query.CaptainId));
                }
                if (!string.IsNullOrEmpty(query.MissionId))
                {
                    conditions.Add("mission_id = @mission_id");
                    parameters.Add(new NpgsqlParameter("@mission_id", query.MissionId));
                }
                if (!string.IsNullOrEmpty(query.VesselId))
                {
                    conditions.Add("vessel_id = @vessel_id");
                    parameters.Add(new NpgsqlParameter("@vessel_id", query.VesselId));
                }
                if (!string.IsNullOrEmpty(query.VoyageId))
                {
                    conditions.Add("voyage_id = @voyage_id");
                    parameters.Add(new NpgsqlParameter("@voyage_id", query.VoyageId));
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT COUNT(*) FROM events" + whereClause + ";";
                    foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.NpgsqlDbType) { Value = p.Value });
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<ArmadaEvent> results = new List<ArmadaEvent>();
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = "SELECT * FROM events" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (NpgsqlParameter p in parameters) cmd.Parameters.Add(new NpgsqlParameter(p.ParameterName, p.NpgsqlDbType) { Value = p.Value });
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(EventFromReader(reader));
                    }
                }

                return EnumerationResult<ArmadaEvent>.Create(query, results, totalCount);
            }
        }

        #endregion

        #region Private-Methods

        private static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }

        private static DateTime FromIso8601(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        private static ArmadaEvent EventFromReader(NpgsqlDataReader reader)
        {
            ArmadaEvent evt = new ArmadaEvent();
            evt.Id = reader["id"].ToString()!;
            evt.EventType = reader["event_type"].ToString()!;
            evt.EntityType = NullableString(reader["entity_type"]);
            evt.EntityId = NullableString(reader["entity_id"]);
            evt.CaptainId = NullableString(reader["captain_id"]);
            evt.MissionId = NullableString(reader["mission_id"]);
            evt.VesselId = NullableString(reader["vessel_id"]);
            evt.VoyageId = NullableString(reader["voyage_id"]);
            evt.Message = reader["message"].ToString()!;
            evt.Payload = NullableString(reader["payload"]);
            evt.CreatedUtc = FromIso8601(reader["created_utc"].ToString()!);
            return evt;
        }

        private async Task<List<ArmadaEvent>> QueryEventsAsync(string sql, Action<NpgsqlCommand> addParams, CancellationToken token)
        {
            List<ArmadaEvent> results = new List<ArmadaEvent>();

            using (NpgsqlConnection conn = await _DataSource.OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (NpgsqlCommand cmd = new NpgsqlCommand())
                {
                    cmd.Connection = conn;
                    cmd.CommandText = sql;
                    addParams(cmd);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(EventFromReader(reader));
                    }
                }
            }

            return results;
        }

        #endregion
    }
}
