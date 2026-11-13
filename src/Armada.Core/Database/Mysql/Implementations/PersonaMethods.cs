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
    using Armada.Core.Services;

    /// <summary>
    /// MySQL implementation of persona database operations.
    /// </summary>
    public class PersonaMethods : IPersonaMethods
    {
        #region Private-Members

        private string _ConnectionString;
        private static readonly string _Iso8601Format = "yyyy-MM-dd HH:mm:ss.ffffff";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with a MySQL connection string.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        public PersonaMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a persona.
        /// </summary>
        /// <param name="persona">Persona to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created persona.</returns>
        public async Task<Persona> CreateAsync(Persona persona, CancellationToken token = default)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            string? routingError = PersonaCaptainPrioritySelector.Validate(persona);
            if (routingError != null) throw new ArgumentException(routingError, nameof(persona));
            persona.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlTransaction transaction = (MySqlTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"INSERT INTO personas (id, tenant_id, user_id, scope, name, description, prompt_template_name, is_built_in, active, default_captain_id, priority_availability, priority_exhaustion, created_utc, last_update_utc)
                            VALUES (@id, @tenant_id, @user_id, @scope, @name, @description, @prompt_template_name, @is_built_in, @active, @default_captain_id, @priority_availability, @priority_exhaustion, @created_utc, @last_update_utc);";
                        cmd.Parameters.AddWithValue("@id", persona.Id);
                        cmd.Parameters.AddWithValue("@tenant_id", (object?)persona.TenantId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@user_id", (object?)persona.UserId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@scope", persona.Scope.ToString());
                        cmd.Parameters.AddWithValue("@name", persona.Name);
                        cmd.Parameters.AddWithValue("@description", (object?)persona.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@prompt_template_name", persona.PromptTemplateName);
                        cmd.Parameters.AddWithValue("@is_built_in", persona.IsBuiltIn ? 1 : 0);
                        cmd.Parameters.AddWithValue("@active", persona.Active ? 1 : 0);
                        cmd.Parameters.AddWithValue("@default_captain_id", (object?)persona.DefaultCaptainId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@priority_availability", persona.PriorityAvailability.ToString());
                        cmd.Parameters.AddWithValue("@priority_exhaustion", persona.PriorityExhaustion.ToString());
                        cmd.Parameters.AddWithValue("@created_utc", ToIso8601(persona.CreatedUtc));
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(persona.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                    await SavePrioritiesAsync(conn, transaction, persona, token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
            }

            return persona;
        }

        /// <summary>
        /// Read a persona by identifier.
        /// </summary>
        /// <param name="id">Persona identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Persona or null if not found.</returns>
        public async Task<Persona?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM personas WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    Persona? result = null;
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false)) result = PersonaFromReader(reader);
                    }
                    if (result != null) await LoadPrioritiesAsync(conn, result, token).ConfigureAwait(false);
                    return result;
                }
            }

        }

        /// <summary>
        /// Read a persona by name.
        /// </summary>
        /// <param name="name">Persona name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Persona or null if not found.</returns>
        public async Task<Persona?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM personas WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    Persona? result = null;
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false)) result = PersonaFromReader(reader);
                    }
                    if (result != null) await LoadPrioritiesAsync(conn, result, token).ConfigureAwait(false);
                    return result;
                }
            }

        }

        /// <summary>
        /// Read a persona by tenant and name.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="name">Persona name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Persona or null if not found.</returns>
        public async Task<Persona?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM personas WHERE tenant_id = @tenant_id AND name = @name;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@name", name);
                    Persona? result = null;
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false)) result = PersonaFromReader(reader);
                    }
                    if (result != null) await LoadPrioritiesAsync(conn, result, token).ConfigureAwait(false);
                    return result;
                }
            }

        }

        /// <summary>
        /// Update a persona.
        /// </summary>
        /// <param name="persona">Persona with updated values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated persona.</returns>
        public async Task<Persona> UpdateAsync(Persona persona, CancellationToken token = default)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            string? routingError = PersonaCaptainPrioritySelector.Validate(persona);
            if (routingError != null) throw new ArgumentException(routingError, nameof(persona));
            persona.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlTransaction transaction = (MySqlTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = transaction;
                        cmd.CommandText = @"UPDATE personas SET
                            tenant_id = @tenant_id,
                            user_id = @user_id,
                            scope = @scope,
                            name = @name,
                            description = @description,
                            prompt_template_name = @prompt_template_name,
                            is_built_in = @is_built_in,
                            active = @active,
                            default_captain_id = @default_captain_id,
                                priority_availability = @priority_availability,
                                priority_exhaustion = @priority_exhaustion,
                            last_update_utc = @last_update_utc
                            WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", persona.Id);
                        cmd.Parameters.AddWithValue("@tenant_id", (object?)persona.TenantId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@user_id", (object?)persona.UserId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@scope", persona.Scope.ToString());
                        cmd.Parameters.AddWithValue("@name", persona.Name);
                        cmd.Parameters.AddWithValue("@description", (object?)persona.Description ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@prompt_template_name", persona.PromptTemplateName);
                        cmd.Parameters.AddWithValue("@is_built_in", persona.IsBuiltIn ? 1 : 0);
                        cmd.Parameters.AddWithValue("@active", persona.Active ? 1 : 0);
                        cmd.Parameters.AddWithValue("@default_captain_id", (object?)persona.DefaultCaptainId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@priority_availability", persona.PriorityAvailability.ToString());
                        cmd.Parameters.AddWithValue("@priority_exhaustion", persona.PriorityExhaustion.ToString());
                        cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(persona.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                    await SavePrioritiesAsync(conn, transaction, persona, token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
            }

            return persona;
        }

        /// <summary>
        /// Delete a persona by identifier.
        /// </summary>
        /// <param name="id">Persona identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlTransaction transaction = (MySqlTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (MySqlCommand cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = transaction;
                        cmd.CommandText = "DELETE FROM personas WHERE id = @id;";
                        cmd.Parameters.AddWithValue("@id", id);
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all personas.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of all personas.</returns>
        public async Task<List<Persona>> EnumerateAsync(CancellationToken token = default)
        {
            List<Persona> results = new List<Persona>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM personas ORDER BY name;";
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(PersonaFromReader(reader));
                    }
                    foreach (Persona result in results)
                        await LoadPrioritiesAsync(conn, result, token).ConfigureAwait(false);
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate personas with pagination and filtering.
        /// </summary>
        /// <param name="query">Enumeration query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result.</returns>
        public async Task<EnumerationResult<Persona>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
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

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM personas" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                // Query
                List<Persona> results = new List<Persona>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM personas" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(PersonaFromReader(reader));
                    }
                    foreach (Persona result in results)
                        await LoadPrioritiesAsync(conn, result, token).ConfigureAwait(false);
                }

                return EnumerationResult<Persona>.Create(query, results, totalCount);
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Persona>> EnumeratePageAsync(EnumerationQuery query, ScopedEnumerationFilter filter, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (filter == null) throw new ArgumentNullException(nameof(filter));
            ScopedEnumerationSqlQuery sql = ScopedEnumerationQueryBuilder.Build(query, filter, true);
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM personas" + sql.Where + ";", cmd => MysqlCommandHelper.Bind(cmd, sql.Parameters), token).ConfigureAwait(false);
                List<Persona> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT * FROM personas" + sql.Where + sql.OrderBy + MysqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    cmd => MysqlCommandHelper.Bind(cmd, sql.Parameters), PersonaFromReader, token).ConfigureAwait(false);
                foreach (Persona row in rows) await LoadPrioritiesAsync(conn, row, token).ConfigureAwait(false);
                return EnumerationResult<Persona>.Create(query, rows, total);
            }
        }

        /// <summary>
        /// Check if a persona exists by identifier.
        /// </summary>
        /// <param name="id">Persona identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the persona exists.</returns>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM personas WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        /// <summary>
        /// Check if a persona exists by name.
        /// </summary>
        /// <param name="name">Persona name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the persona exists.</returns>
        public async Task<bool> ExistsByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM personas WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Convert a MySqlDataReader row to a Persona model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Persona instance.</returns>
        private static Persona PersonaFromReader(MySqlDataReader reader)
        {
            Persona persona = new Persona();
            persona.Id = reader["id"].ToString()!;
            persona.TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]);
            persona.UserId = MysqlDatabaseDriver.NullableString(reader["user_id"]);
            persona.Scope = System.Enum.TryParse<Armada.Core.Enums.ScopeEnum>(reader["scope"]?.ToString(), true, out Armada.Core.Enums.ScopeEnum __sc) ? __sc : Armada.Core.Enums.ScopeEnum.TenantWide;
            persona.Name = reader["name"].ToString()!;
            persona.Description = MysqlDatabaseDriver.NullableString(reader["description"]);
            persona.PromptTemplateName = reader["prompt_template_name"].ToString()!;
            persona.IsBuiltIn = Convert.ToInt64(reader["is_built_in"]) == 1;
            persona.Active = Convert.ToInt64(reader["active"]) == 1;
            persona.DefaultCaptainId = MysqlDatabaseDriver.NullableString(reader["default_captain_id"]);
            persona.PriorityAvailability = Enum.Parse<PersonaPriorityAvailabilityEnum>(reader["priority_availability"].ToString()!);
            persona.PriorityExhaustion = Enum.Parse<PersonaPriorityExhaustionEnum>(reader["priority_exhaustion"].ToString()!);
            persona.CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc);
            persona.LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc);
            return persona;
        }

        private static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }


        private static async Task SavePrioritiesAsync(MySqlConnection conn, MySqlTransaction transaction, Persona persona, CancellationToken token)
        {
            using (MySqlCommand cmd = conn.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "DELETE FROM persona_captain_priorities WHERE persona_id = @persona_id;";
                cmd.Parameters.AddWithValue("@persona_id", persona.Id);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
            for (int index = 0; index < persona.CaptainPriorities.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                PersonaCaptainPriority priority = persona.CaptainPriorities[index];
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.Transaction = transaction;
                    cmd.CommandText = @"INSERT INTO persona_captain_priorities
                        (persona_id, priority_order, runtime, model, endpoint, minimum_tier)
                        VALUES (@persona_id, @priority_order, @runtime, @model, @endpoint, @minimum_tier);";
                    cmd.Parameters.AddWithValue("@persona_id", persona.Id);
                    cmd.Parameters.AddWithValue("@priority_order", index);
                    cmd.Parameters.AddWithValue("@runtime", (object?)priority.Runtime?.ToString() ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@model", (object?)priority.Model ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@endpoint", (object?)priority.Endpoint ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@minimum_tier", (object?)priority.MinimumTier?.ToString() ?? DBNull.Value);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        private static async Task LoadPrioritiesAsync(MySqlConnection conn, Persona persona, CancellationToken token)
        {
            using (MySqlCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM persona_captain_priorities WHERE persona_id = @persona_id ORDER BY priority_order;";
                cmd.Parameters.AddWithValue("@persona_id", persona.Id);
                using (MySqlDataReader reader = (MySqlDataReader)await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                    {
                        PersonaCaptainPriority priority = new PersonaCaptainPriority();
                        priority.Runtime = reader["runtime"] == DBNull.Value ? null : Enum.Parse<AgentRuntimeEnum>(reader["runtime"].ToString()!);
                        priority.Model = reader["model"] == DBNull.Value ? null : reader["model"].ToString();
                        priority.Endpoint = reader["endpoint"] == DBNull.Value ? null : reader["endpoint"].ToString();
                        priority.MinimumTier = reader["minimum_tier"] == DBNull.Value ? null : Enum.Parse<CaptainTierEnum>(reader["minimum_tier"].ToString()!);
                        persona.CaptainPriorities.Add(priority);
                    }
                }
            }
        }

        #endregion
    }
}
