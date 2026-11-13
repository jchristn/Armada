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
    using Armada.Core.Services;
    using Armada.Core.Settings;

    /// <summary>
    /// SQLite implementation of persona database operations.
    /// </summary>
    public class PersonaMethods : IPersonaMethods
    {
        #region Private-Members

#pragma warning disable CS0414
        private readonly string _Header = "[PersonaMethods] ";
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
        public PersonaMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Persona> CreateAsync(Persona persona, CancellationToken token = default)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            string? routingError = PersonaCaptainPrioritySelector.Validate(persona);
            if (routingError != null) throw new ArgumentException(routingError, nameof(persona));
            persona.LastUpdateUtc = DateTime.UtcNow;

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteTransaction transaction = (SqliteTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (SqliteCommand cmd = conn.CreateCommand())
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
                        cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(persona.CreatedUtc));
                        cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(persona.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                    await SavePrioritiesAsync(conn, transaction, persona, token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
            }

            return persona;
        }

        /// <inheritdoc />
        public async Task<Persona?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM personas WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    Persona? result = null;
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false)) result = PersonaFromReader(reader);
                    }
                    if (result != null) await LoadPrioritiesAsync(conn, result, token).ConfigureAwait(false);
                    return result;
                }
            }

        }

        /// <inheritdoc />
        public async Task<Persona?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM personas WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    Persona? result = null;
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false)) result = PersonaFromReader(reader);
                    }
                    if (result != null) await LoadPrioritiesAsync(conn, result, token).ConfigureAwait(false);
                    return result;
                }
            }

        }

        /// <inheritdoc />
        public async Task<Persona?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM personas WHERE tenant_id = @tenant_id AND name = @name;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@name", name);
                    Persona? result = null;
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false)) result = PersonaFromReader(reader);
                    }
                    if (result != null) await LoadPrioritiesAsync(conn, result, token).ConfigureAwait(false);
                    return result;
                }
            }

        }

        /// <inheritdoc />
        public async Task<Persona> UpdateAsync(Persona persona, CancellationToken token = default)
        {
            if (persona == null) throw new ArgumentNullException(nameof(persona));
            string? routingError = PersonaCaptainPrioritySelector.Validate(persona);
            if (routingError != null) throw new ArgumentException(routingError, nameof(persona));
            persona.LastUpdateUtc = DateTime.UtcNow;

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteTransaction transaction = (SqliteTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (SqliteCommand cmd = conn.CreateCommand())
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
                        cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(persona.LastUpdateUtc));
                        await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                    await SavePrioritiesAsync(conn, transaction, persona, token).ConfigureAwait(false);
                    await transaction.CommitAsync(token).ConfigureAwait(false);
                }
            }

            return persona;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteTransaction transaction = (SqliteTransaction)await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                {
                    using (SqliteCommand cmd = conn.CreateCommand())
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

        /// <inheritdoc />
        public async Task<List<Persona>> EnumerateAsync(CancellationToken token = default)
        {
            List<Persona> results = new List<Persona>();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM personas ORDER BY name;";
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

        /// <inheritdoc />
        public async Task<EnumerationResult<Persona>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
        {
            if (query == null) query = new EnumerationQuery();

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
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

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM personas" + whereClause + ";";
                    foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<Persona> results = new List<Persona>();
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM personas" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqliteCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM personas" + sql.Where + ";", cmd => SqliteCommandHelper.Bind(cmd, sql.Parameters), token).ConfigureAwait(false);
                List<Persona> rows = await SqliteCommandHelper.QueryAsync(conn,
                    "SELECT * FROM personas" + sql.Where + sql.OrderBy + SqliteCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    cmd => SqliteCommandHelper.Bind(cmd, sql.Parameters), PersonaFromReader, token).ConfigureAwait(false);
                foreach (Persona row in rows) await LoadPrioritiesAsync(conn, row, token).ConfigureAwait(false);
                return EnumerationResult<Persona>.Create(query, rows, total);
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM personas WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    return count > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> ExistsByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM personas WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Convert a SqliteDataReader row to a Persona model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Persona instance.</returns>
        private static Persona PersonaFromReader(SqliteDataReader reader)
        {
            Persona persona = new Persona();
            persona.Id = reader["id"].ToString()!;
            persona.TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]);
            persona.UserId = SqliteDatabaseDriver.NullableString(reader["user_id"]);
            persona.Scope = System.Enum.TryParse<Armada.Core.Enums.ScopeEnum>(reader["scope"]?.ToString(), true, out Armada.Core.Enums.ScopeEnum __sc) ? __sc : Armada.Core.Enums.ScopeEnum.TenantWide;
            persona.Name = reader["name"].ToString()!;
            persona.Description = SqliteDatabaseDriver.NullableString(reader["description"]);
            persona.PromptTemplateName = reader["prompt_template_name"].ToString()!;
            persona.IsBuiltIn = Convert.ToInt64(reader["is_built_in"]) == 1;
            persona.Active = Convert.ToInt64(reader["active"]) == 1;
            persona.DefaultCaptainId = SqliteDatabaseDriver.NullableString(reader["default_captain_id"]);
            persona.PriorityAvailability = Enum.Parse<PersonaPriorityAvailabilityEnum>(reader["priority_availability"].ToString()!);
            persona.PriorityExhaustion = Enum.Parse<PersonaPriorityExhaustionEnum>(reader["priority_exhaustion"].ToString()!);
            persona.CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!);
            persona.LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!);
            return persona;
        }


        private static async Task SavePrioritiesAsync(SqliteConnection conn, SqliteTransaction transaction, Persona persona, CancellationToken token)
        {
            using (SqliteCommand cmd = conn.CreateCommand())
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
                using (SqliteCommand cmd = conn.CreateCommand())
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

        private static async Task LoadPrioritiesAsync(SqliteConnection conn, Persona persona, CancellationToken token)
        {
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM persona_captain_priorities WHERE persona_id = @persona_id ORDER BY priority_order;";
                cmd.Parameters.AddWithValue("@persona_id", persona.Id);
                using (SqliteDataReader reader = (SqliteDataReader)await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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
