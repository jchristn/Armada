namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>
    /// PostgreSQL implementation of skill persistence.
    /// </summary>
    public class SkillMethods : ISkillMethods
    {
        private readonly PostgresqlDatabaseDriver _Driver;

        /// <summary>
        /// Instantiate.
        /// </summary>
        public SkillMethods(PostgresqlDatabaseDriver driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        /// <inheritdoc />
        public async Task<Skill> CreateAsync(Skill skill, CancellationToken token = default)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            skill.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO skills
                        (id, tenant_id, user_id, scope, name, description, category, content, is_built_in, active, created_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @user_id, @scope, @name, @description, @category, @content, @is_built_in, @active, @created_utc, @last_update_utc);";
                    AddParameters(cmd, skill);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return skill;
        }

        /// <inheritdoc />
        public async Task<Skill?> ReadAsync(string id, SkillQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<NpgsqlParameter> parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "SELECT * FROM skills WHERE " + String.Join(" AND ", conditions) + " LIMIT 1;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return FromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<Skill> UpdateAsync(Skill skill, CancellationToken token = default)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            skill.LastUpdateUtc = DateTime.UtcNow;

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
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
                }
            }

            return skill;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string id, SkillQuery? query = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string> { "id = @id" };
                    List<NpgsqlParameter> parameters = new List<NpgsqlParameter> { new NpgsqlParameter("@id", id) };
                    ApplyQueryFilters(query, conditions, parameters);
                    cmd.CommandText = "DELETE FROM skills WHERE " + String.Join(" AND ", conditions) + ";";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(parameter);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Skill>> EnumerateAsync(SkillQuery query, CancellationToken token = default)
        {
            query ??= new SkillQuery();
            int pageSize = query.PageSize <= 0 ? 25 : query.PageSize;
            int offset = query.PageNumber <= 1 ? 0 : (query.PageNumber - 1) * pageSize;

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                List<string> conditions = new List<string>();
                List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();
                ApplyQueryFilters(query, conditions, parameters);
                string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;

                long totalCount;
                using (NpgsqlCommand countCmd = conn.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM skills" + whereClause + ";";
                    foreach (NpgsqlParameter parameter in parameters) countCmd.Parameters.Add(CloneParameter(parameter));
                    totalCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                List<Skill> results = new List<Skill>();
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM skills" + whereClause + " ORDER BY name ASC LIMIT @page_size OFFSET @offset;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(CloneParameter(parameter));
                    cmd.Parameters.AddWithValue("@page_size", pageSize);
                    cmd.Parameters.AddWithValue("@offset", offset);
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }
                }

                return new EnumerationResult<Skill>
                {
                    PageNumber = query.PageNumber,
                    PageSize = pageSize,
                    TotalRecords = totalCount,
                    TotalPages = pageSize > 0 ? (int)Math.Ceiling((double)totalCount / pageSize) : 0,
                    Objects = results
                };
            }
        }

        /// <inheritdoc />
        public async Task<List<Skill>> EnumerateAllAsync(SkillQuery query, CancellationToken token = default)
        {
            query ??= new SkillQuery();

            using (NpgsqlConnection conn = _Driver.CreateConnection())
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand cmd = conn.CreateCommand())
                {
                    List<string> conditions = new List<string>();
                    List<NpgsqlParameter> parameters = new List<NpgsqlParameter>();
                    ApplyQueryFilters(query, conditions, parameters);
                    string whereClause = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
                    cmd.CommandText = "SELECT * FROM skills" + whereClause + " ORDER BY name ASC;";
                    foreach (NpgsqlParameter parameter in parameters) cmd.Parameters.Add(parameter);

                    List<Skill> results = new List<Skill>();
                    using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(FromReader(reader));
                    }

                    return results;
                }
            }
        }

        private static void ApplyQueryFilters(SkillQuery? query, List<string> conditions, List<NpgsqlParameter> parameters)
        {
            if (query == null) return;

            if (!String.IsNullOrWhiteSpace(query.TenantId))
            {
                conditions.Add("tenant_id = @tenant_id");
                parameters.Add(new NpgsqlParameter("@tenant_id", query.TenantId));
            }
            if (!String.IsNullOrWhiteSpace(query.UserId))
            {
                conditions.Add("user_id = @user_id");
                parameters.Add(new NpgsqlParameter("@user_id", query.UserId));
            }
            if (!String.IsNullOrWhiteSpace(query.Category))
            {
                conditions.Add("category = @category");
                parameters.Add(new NpgsqlParameter("@category", query.Category));
            }
            if (!String.IsNullOrWhiteSpace(query.Search))
            {
                conditions.Add("(name ILIKE @search OR COALESCE(description, '') ILIKE @search)");
                parameters.Add(new NpgsqlParameter("@search", "%" + query.Search + "%"));
            }
            if (query.Active.HasValue)
            {
                conditions.Add("active = @active");
                parameters.Add(new NpgsqlParameter("@active", query.Active.Value));
            }
            if (query.FromUtc.HasValue)
            {
                conditions.Add("created_utc >= @from_utc");
                parameters.Add(new NpgsqlParameter("@from_utc", query.FromUtc.Value));
            }
            if (query.ToUtc.HasValue)
            {
                conditions.Add("created_utc <= @to_utc");
                parameters.Add(new NpgsqlParameter("@to_utc", query.ToUtc.Value));
            }
        }

        private static void AddParameters(NpgsqlCommand cmd, Skill skill)
        {
            cmd.Parameters.AddWithValue("@id", skill.Id);
            cmd.Parameters.AddWithValue("@tenant_id", (object?)skill.TenantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@user_id", (object?)skill.UserId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@scope", skill.Scope.ToString());
            cmd.Parameters.AddWithValue("@name", skill.Name);
            cmd.Parameters.AddWithValue("@description", (object?)skill.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@category", (object?)skill.Category ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@content", (object?)skill.Content ?? String.Empty);
            cmd.Parameters.AddWithValue("@is_built_in", skill.IsBuiltIn);
            cmd.Parameters.AddWithValue("@active", skill.Active);
            cmd.Parameters.AddWithValue("@created_utc", skill.CreatedUtc);
            cmd.Parameters.AddWithValue("@last_update_utc", skill.LastUpdateUtc);
        }

        private static Skill FromReader(NpgsqlDataReader reader)
        {
            return new Skill
            {
                Id = reader["id"].ToString() ?? String.Empty,
                TenantId = NullableString(reader["tenant_id"]),
                UserId = NullableString(reader["user_id"]),
                Scope = System.Enum.TryParse<Armada.Core.Enums.ScopeEnum>(reader["scope"]?.ToString(), true, out Armada.Core.Enums.ScopeEnum __sc) ? __sc : Armada.Core.Enums.ScopeEnum.TenantWide,                Name = reader["name"].ToString() ?? String.Empty,
                Description = NullableString(reader["description"]),
                Category = NullableString(reader["category"]),
                Content = NullableString(reader["content"]) ?? String.Empty,
                IsBuiltIn = Convert.ToBoolean(reader["is_built_in"]),
                Active = Convert.ToBoolean(reader["active"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["created_utc"]), DateTimeKind.Utc),
                LastUpdateUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["last_update_utc"]), DateTimeKind.Utc)
            };
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return String.IsNullOrEmpty(str) ? null : str;
        }

        private static NpgsqlParameter CloneParameter(NpgsqlParameter parameter)
        {
            return new NpgsqlParameter(parameter.ParameterName, parameter.Value ?? DBNull.Value);
        }
    }
}
