namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of skill persistence.
    /// </summary>
    public class SkillMethods : ISkillMethods
    {
        private readonly SqliteDatabaseDriver _Driver;
        private readonly DatabaseSettings _Settings;
        private readonly LoggingModule _Logging;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SkillMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        /// <inheritdoc />
        public async Task<Skill> CreateAsync(Skill skill, CancellationToken token = default)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            skill.LastUpdateUtc = DateTime.UtcNow;

            using SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO skills
                (id, tenant_id, user_id, scope, name, description, category, content, is_built_in, active, created_utc, last_update_utc)
                VALUES
                (@id, @tenant_id, @user_id, @scope, @name, @description, @category, @content, @is_built_in, @active, @created_utc, @last_update_utc);";
            AddParameters(cmd, skill);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return skill;
        }

        /// <inheritdoc />
        public async Task<Skill?> ReadAsync(string id, SkillQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();

            List<string> conditions = new List<string> { "id = @id" };
            List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
            ApplyQueryFilters(query, conditions, parameters);
            cmd.CommandText = "SELECT * FROM skills WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);

            using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            if (await reader.ReadAsync(token).ConfigureAwait(false))
                return FromReader(reader);
            return null;
        }

        /// <inheritdoc />
        public async Task<Skill> UpdateAsync(Skill skill, CancellationToken token = default)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            skill.LastUpdateUtc = DateTime.UtcNow;

            using SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = @"UPDATE skills SET
                tenant_id = @tenant_id,
                user_id = @user_id,
                scope = @scope,
                name = @name,
                description = @description,
                category = @category,
                content = @content,
                is_built_in = @is_built_in,
                active = @active,
                last_update_utc = @last_update_utc
                WHERE id = @id;";
            AddParameters(cmd, skill);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return skill;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, SkillQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();
            List<string> conditions = new List<string> { "id = @id" };
            List<SqliteParameter> parameters = new List<SqliteParameter> { new SqliteParameter("@id", id) };
            ApplyQueryFilters(query, conditions, parameters);
            cmd.CommandText = "DELETE FROM skills WHERE " + String.Join(" AND ", conditions) + ";";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Skill>> EnumerateAsync(SkillQuery query, CancellationToken token = default)
        {
            query ??= new SkillQuery();

            using SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);

            List<string> conditions = new List<string>();
            List<SqliteParameter> parameters = new List<SqliteParameter>();
            ApplyQueryFilters(query, conditions, parameters);
            string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

            long totalCount;
            using (SqliteCommand countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM skills" + whereClause + ";";
                foreach (SqliteParameter parameter in parameters) countCmd.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
                totalCount = (long)(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            }

            List<Skill> results = new List<Skill>();
            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM skills" + whereClause +
                    " ORDER BY name ASC" +
                    " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(new SqliteParameter(parameter.ParameterName, parameter.Value));
                using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await reader.ReadAsync(token).ConfigureAwait(false))
                    results.Add(FromReader(reader));
            }

            return new EnumerationResult<Skill>
            {
                PageNumber = query.PageNumber,
                PageSize = query.PageSize,
                TotalRecords = totalCount,
                TotalPages = query.PageSize > 0 ? (int)Math.Ceiling((double)totalCount / query.PageSize) : 0,
                Objects = results
            };
        }

        /// <inheritdoc />
        public async Task<List<Skill>> EnumerateAllAsync(SkillQuery query, CancellationToken token = default)
        {
            query ??= new SkillQuery();

            using SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString);
            await conn.OpenAsync(token).ConfigureAwait(false);
            using SqliteCommand cmd = conn.CreateCommand();

            List<string> conditions = new List<string>();
            List<SqliteParameter> parameters = new List<SqliteParameter>();
            ApplyQueryFilters(query, conditions, parameters);
            string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
            cmd.CommandText = "SELECT * FROM skills" + whereClause + " ORDER BY name ASC;";
            foreach (SqliteParameter parameter in parameters) cmd.Parameters.Add(parameter);

            List<Skill> results = new List<Skill>();
            using SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
                results.Add(FromReader(reader));
            return results;
        }

        private static void ApplyQueryFilters(SkillQuery? query, List<string> conditions, List<SqliteParameter> parameters)
        {
            if (query == null) return;

            if (!String.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new SqliteParameter("@tenant_id", query.TenantId));
            }
            if (!String.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new SqliteParameter("@user_id", query.UserId));
            }
            if (!String.IsNullOrWhiteSpace(query.Category))
            {
                conditions.Add("category = @category");
                parameters.Add(new SqliteParameter("@category", query.Category));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(LOWER(name) LIKE @search OR LOWER(COALESCE(description, '')) LIKE @search)");
                parameters.Add(new SqliteParameter("@search", "%" + query.Search.ToLowerInvariant() + "%"));
            }
            if (query.Active.HasValue)
            {
                conditions.Add("active = @active");
                parameters.Add(new SqliteParameter("@active", query.Active.Value ? 1 : 0));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new SqliteParameter("@from_utc", SqliteDatabaseDriver.ToIso8601(query.FromUtc.Value)));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new SqliteParameter("@to_utc", SqliteDatabaseDriver.ToIso8601(query.ToUtc.Value)));
            }
        }

        private static void AddParameters(SqliteCommand cmd, Skill skill)
        {
            cmd.Parameters.AddWithValue("@id", skill.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)skill.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)skill.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@scope", skill.Scope.ToString());
            cmd.Parameters.AddWithValue("@name", skill.Name);
            cmd.Parameters.AddWithValue("@description", (object?)skill.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@category", (object?)skill.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@content", (object?)skill.Content ?? String.Empty);
            cmd.Parameters.AddWithValue("@is_built_in", skill.IsBuiltIn ? 1 : 0);
            cmd.Parameters.AddWithValue("@active", skill.Active ? 1 : 0);
            cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(skill.CreatedUtc));
            cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(skill.LastUpdateUtc));
        }

        private static Skill FromReader(SqliteDataReader reader)
        {
            return new Skill
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]),
                UserId = SqliteDatabaseDriver.NullableString(reader["user_id"]),
                Scope = System.Enum.TryParse<Armada.Core.Enums.ScopeEnum>(reader["scope"]?.ToString(), true, out Armada.Core.Enums.ScopeEnum __sc) ? __sc : Armada.Core.Enums.ScopeEnum.TenantWide,                Name = reader["name"].ToString() ?? String.Empty,
                Description = SqliteDatabaseDriver.NullableString(reader["description"]),
                Category = SqliteDatabaseDriver.NullableString(reader["category"]),
                Content = SqliteDatabaseDriver.NullableString(reader["content"]) ?? String.Empty,
                IsBuiltIn = Convert.ToInt64(reader["is_built_in"]) == 1,
                Active = Convert.ToInt64(reader["active"]) == 1,
                CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!),
                LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!)
            };
        }
    }
}
