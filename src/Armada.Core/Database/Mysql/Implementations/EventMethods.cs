namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of event database operations.
    /// </summary>
    public class EventMethods : IEventMethods
    {
        #region Private-Members

        private string _ConnectionString;
        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the MySQL event methods.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        public EventMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO events (id, tenant_id, user_id, event_type, entity_type, entity_id, captain_id, mission_id, vessel_id, voyage_id, message, payload, created_utc)
                        VALUES (@id, @tenant_id, @user_id, @event_type, @entity_type, @entity_id, @captain_id, @mission_id, @vessel_id, @voyage_id, @message, @payload, @created_utc);";
                    cmd.Parameters.AddWithValue("@id", armadaEvent.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)armadaEvent.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@user_id", (object?)armadaEvent.UserId ?? DBNull.Value);
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
        /// Read an event by identifier.
        /// </summary>
        /// <param name="id">Event identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Event if found, null otherwise.</returns>
        public async Task<ArmadaEvent?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM events WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return EventFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Delete an event by identifier.
        /// </summary>
        /// <param name="id">Event identifier.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM events WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
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

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<MySqlParameter> parameters = new List<MySqlParameter>();

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new MySqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new MySqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.EventType))
                {
                    conditions.Add("event_type = @event_type");
                    parameters.Add(new MySqlParameter("@event_type", query.EventType));
                }
                if (!string.IsNullOrEmpty(query.CaptainId))
                {
                    conditions.Add("captain_id = @captain_id");
                    parameters.Add(new MySqlParameter("@captain_id", query.CaptainId));
                }
                if (!string.IsNullOrEmpty(query.MissionId))
                {
                    conditions.Add("mission_id = @mission_id");
                    parameters.Add(new MySqlParameter("@mission_id", query.MissionId));
                }
                if (!string.IsNullOrEmpty(query.VesselId))
                {
                    conditions.Add("vessel_id = @vessel_id");
                    parameters.Add(new MySqlParameter("@vessel_id", query.VesselId));
                }
                if (!string.IsNullOrEmpty(query.VoyageId))
                {
                    conditions.Add("voyage_id = @voyage_id");
                    parameters.Add(new MySqlParameter("@voyage_id", query.VoyageId));
                }

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM events" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<ArmadaEvent> results = new List<ArmadaEvent>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM events" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(EventFromReader(reader));
                    }
                }

                return EnumerationResult<ArmadaEvent>.Create(query, results, totalCount);
            }
        }

        /// <summary>
        /// Read an event by tenant and identifier (tenant-scoped).
        /// </summary>
        public async Task<ArmadaEvent?> ReadAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM events WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return EventFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Delete an event by tenant and identifier (tenant-scoped).
        /// </summary>
        public async Task DeleteAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM events WHERE tenant_id = @tenantId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateAsync(string tenantId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            List<ArmadaEvent> results = new List<ArmadaEvent>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM events WHERE tenant_id = @tenantId ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(EventFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateRecentAsync(string tenantId, int limit = 50, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));

            return await QueryEventsAsync("SELECT * FROM events WHERE tenant_id = @tenantId ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByTypeAsync(string tenantId, string eventType, int limit = 50, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(eventType)) throw new ArgumentNullException(nameof(eventType));

            return await QueryEventsAsync("SELECT * FROM events WHERE tenant_id = @tenantId AND event_type = @event_type ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@event_type", eventType);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByEntityAsync(string tenantId, string entityType, string entityId, int limit = 50, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(entityType)) throw new ArgumentNullException(nameof(entityType));
            if (string.IsNullOrEmpty(entityId)) throw new ArgumentNullException(nameof(entityId));

            return await QueryEventsAsync("SELECT * FROM events WHERE tenant_id = @tenantId AND entity_type = @entity_type AND entity_id = @entity_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@entity_type", entityType);
                    cmd.Parameters.AddWithValue("@entity_id", entityId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByCaptainAsync(string tenantId, string captainId, int limit = 50, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(captainId)) throw new ArgumentNullException(nameof(captainId));

            return await QueryEventsAsync("SELECT * FROM events WHERE tenant_id = @tenantId AND captain_id = @captain_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@captain_id", captainId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByMissionAsync(string tenantId, string missionId, int limit = 50, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(missionId)) throw new ArgumentNullException(nameof(missionId));

            return await QueryEventsAsync("SELECT * FROM events WHERE tenant_id = @tenantId AND mission_id = @mission_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@mission_id", missionId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByVesselAsync(string tenantId, string vesselId, int limit = 50, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));

            return await QueryEventsAsync("SELECT * FROM events WHERE tenant_id = @tenantId AND vessel_id = @vessel_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@vessel_id", vesselId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateByVoyageAsync(string tenantId, string voyageId, int limit = 50, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(voyageId)) throw new ArgumentNullException(nameof(voyageId));

            return await QueryEventsAsync("SELECT * FROM events WHERE tenant_id = @tenantId AND voyage_id = @voyage_id ORDER BY created_utc DESC LIMIT @limit;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@voyage_id", voyageId);
                    cmd.Parameters.AddWithValue("@limit", limit);
                }, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Enumerate events with pagination and filtering (tenant-scoped).
        /// </summary>
        public async Task<EnumerationResult<ArmadaEvent>> EnumerateAsync(string tenantId, EnumerationQuery query, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (query == null) query = new EnumerationQuery();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "tenant_id = @tenantId" };
                List<MySqlParameter> parameters = new List<MySqlParameter> { new MySqlParameter("@tenantId", tenantId) };

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new MySqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new MySqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.EventType))
                {
                    conditions.Add("event_type = @event_type");
                    parameters.Add(new MySqlParameter("@event_type", query.EventType));
                }
                if (!string.IsNullOrEmpty(query.CaptainId))
                {
                    conditions.Add("captain_id = @captain_id");
                    parameters.Add(new MySqlParameter("@captain_id", query.CaptainId));
                }
                if (!string.IsNullOrEmpty(query.MissionId))
                {
                    conditions.Add("mission_id = @mission_id");
                    parameters.Add(new MySqlParameter("@mission_id", query.MissionId));
                }
                if (!string.IsNullOrEmpty(query.VesselId))
                {
                    conditions.Add("vessel_id = @vessel_id");
                    parameters.Add(new MySqlParameter("@vessel_id", query.VesselId));
                }
                if (!string.IsNullOrEmpty(query.VoyageId))
                {
                    conditions.Add("voyage_id = @voyage_id");
                    parameters.Add(new MySqlParameter("@voyage_id", query.VoyageId));
                }

                string whereClause = " WHERE " + string.Join(" AND ", conditions);
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                long totalCount = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM events" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                List<ArmadaEvent> results = new List<ArmadaEvent>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM events" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(EventFromReader(reader));
                    }
                }

                return EnumerationResult<ArmadaEvent>.Create(query, results, totalCount);
            }
        }

        /// <inheritdoc />
        public async Task<ArmadaEvent?> ReadAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM events WHERE tenant_id = @tenantId AND user_id = @userId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return EventFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string tenantId, string userId, string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM events WHERE tenant_id = @tenantId AND user_id = @userId AND id = @id;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<ArmadaEvent>> EnumerateAsync(string tenantId, string userId, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            List<ArmadaEvent> results = new List<ArmadaEvent>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM events WHERE tenant_id = @tenantId AND user_id = @userId ORDER BY created_utc DESC;";
                    cmd.Parameters.AddWithValue("@tenantId", tenantId);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(EventFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<ArmadaEvent>> EnumerateAsync(string tenantId, string userId, EnumerationQuery query, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(userId)) throw new ArgumentNullException(nameof(userId));
            if (query == null) query = new EnumerationQuery();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string> { "tenant_id = @tenantId", "user_id = @userId" };
                List<MySqlParameter> parameters = new List<MySqlParameter>
                {
                    new MySqlParameter("@tenantId", tenantId),
                    new MySqlParameter("@userId", userId)
                };

                if (query.CreatedAfter.HasValue)
                {
                    conditions.Add("created_utc > @created_after");
                    parameters.Add(new MySqlParameter("@created_after", ToIso8601(query.CreatedAfter.Value)));
                }
                if (query.CreatedBefore.HasValue)
                {
                    conditions.Add("created_utc < @created_before");
                    parameters.Add(new MySqlParameter("@created_before", ToIso8601(query.CreatedBefore.Value)));
                }
                if (!string.IsNullOrEmpty(query.EventType))
                {
                    conditions.Add("event_type = @event_type");
                    parameters.Add(new MySqlParameter("@event_type", query.EventType));
                }
                if (!string.IsNullOrEmpty(query.CaptainId))
                {
                    conditions.Add("captain_id = @captain_id");
                    parameters.Add(new MySqlParameter("@captain_id", query.CaptainId));
                }
                if (!string.IsNullOrEmpty(query.MissionId))
                {
                    conditions.Add("mission_id = @mission_id");
                    parameters.Add(new MySqlParameter("@mission_id", query.MissionId));
                }
                if (!string.IsNullOrEmpty(query.VesselId))
                {
                    conditions.Add("vessel_id = @vessel_id");
                    parameters.Add(new MySqlParameter("@vessel_id", query.VesselId));
                }
                if (!string.IsNullOrEmpty(query.VoyageId))
                {
                    conditions.Add("voyage_id = @voyage_id");
                    parameters.Add(new MySqlParameter("@voyage_id", query.VoyageId));
                }

                string whereClause = " WHERE " + string.Join(" AND ", conditions);
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                long totalCount = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM events" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                List<ArmadaEvent> results = new List<ArmadaEvent>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM events" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

        private static ArmadaEvent EventFromReader(MySqlDataReader reader)
        {
            ArmadaEvent evt = new ArmadaEvent();
            evt.Id = reader["id"].ToString()!;
            evt.TenantId = NullableString(reader["tenant_id"]);
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

        private async Task<List<ArmadaEvent>> QueryEventsAsync(string sql, Action<MySqlCommand> addParams, CancellationToken token)
        {
            List<ArmadaEvent> results = new List<ArmadaEvent>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = sql;
                    addParams(cmd);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

