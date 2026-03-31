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
    /// MySQL implementation of prompt template database operations.
    /// </summary>
    public class PromptTemplateMethods : IPromptTemplateMethods
    {
        #region Private-Members

        private string _ConnectionString;
        private static readonly string _Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with a MySQL connection string.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        public PromptTemplateMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a prompt template.
        /// </summary>
        /// <param name="template">Prompt template to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created prompt template.</returns>
        public async Task<PromptTemplate> CreateAsync(PromptTemplate template, CancellationToken token = default)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            template.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO prompt_templates (id, tenant_id, name, description, category, content, is_built_in, active, created_utc, last_update_utc)
                        VALUES (@id, @tenant_id, @name, @description, @category, @content, @is_built_in, @active, @created_utc, @last_update_utc);";
                    cmd.Parameters.AddWithValue("@id", template.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)template.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", template.Name);
                    cmd.Parameters.AddWithValue("@description", (object?)template.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@category", template.Category);
                    cmd.Parameters.AddWithValue("@content", template.Content);
                    cmd.Parameters.AddWithValue("@is_built_in", template.IsBuiltIn ? 1 : 0);
                    cmd.Parameters.AddWithValue("@active", template.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(template.CreatedUtc));
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(template.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return template;
        }

        /// <summary>
        /// Read a prompt template by identifier.
        /// </summary>
        /// <param name="id">Prompt template identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt template or null if not found.</returns>
        public async Task<PromptTemplate?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM prompt_templates WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return PromptTemplateFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Read a prompt template by name.
        /// </summary>
        /// <param name="name">Prompt template name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt template or null if not found.</returns>
        public async Task<PromptTemplate?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM prompt_templates WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return PromptTemplateFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Read a prompt template by tenant and name.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="name">Prompt template name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Prompt template or null if not found.</returns>
        public async Task<PromptTemplate?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM prompt_templates WHERE tenant_id = @tenant_id AND name = @name;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@name", name);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return PromptTemplateFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Update a prompt template.
        /// </summary>
        /// <param name="template">Prompt template with updated values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated prompt template.</returns>
        public async Task<PromptTemplate> UpdateAsync(PromptTemplate template, CancellationToken token = default)
        {
            if (template == null) throw new ArgumentNullException(nameof(template));
            template.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE prompt_templates SET
                        tenant_id = @tenant_id,
                        name = @name,
                        description = @description,
                        category = @category,
                        content = @content,
                        is_built_in = @is_built_in,
                        active = @active,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", template.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)template.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", template.Name);
                    cmd.Parameters.AddWithValue("@description", (object?)template.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@category", template.Category);
                    cmd.Parameters.AddWithValue("@content", template.Content);
                    cmd.Parameters.AddWithValue("@is_built_in", template.IsBuiltIn ? 1 : 0);
                    cmd.Parameters.AddWithValue("@active", template.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(template.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }

            return template;
        }

        /// <summary>
        /// Delete a prompt template by identifier.
        /// </summary>
        /// <param name="id">Prompt template identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM prompt_templates WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all prompt templates.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of all prompt templates.</returns>
        public async Task<List<PromptTemplate>> EnumerateAsync(CancellationToken token = default)
        {
            List<PromptTemplate> results = new List<PromptTemplate>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM prompt_templates ORDER BY name;";
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(PromptTemplateFromReader(reader));
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate prompt templates with pagination and filtering.
        /// </summary>
        /// <param name="query">Enumeration query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result.</returns>
        public async Task<EnumerationResult<PromptTemplate>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
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
                    cmd.CommandText = "SELECT COUNT(*) FROM prompt_templates" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                // Query
                List<PromptTemplate> results = new List<PromptTemplate>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM prompt_templates" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(PromptTemplateFromReader(reader));
                    }
                }

                return EnumerationResult<PromptTemplate>.Create(query, results, totalCount);
            }
        }

        /// <summary>
        /// Check if a prompt template exists by identifier.
        /// </summary>
        /// <param name="id">Prompt template identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the prompt template exists.</returns>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM prompt_templates WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        /// <summary>
        /// Check if a prompt template exists by name.
        /// </summary>
        /// <param name="name">Prompt template name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the prompt template exists.</returns>
        public async Task<bool> ExistsByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM prompt_templates WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Convert a MySqlDataReader row to a PromptTemplate model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>PromptTemplate instance.</returns>
        private static PromptTemplate PromptTemplateFromReader(MySqlDataReader reader)
        {
            PromptTemplate template = new PromptTemplate();
            template.Id = reader["id"].ToString()!;
            template.TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]);
            template.Name = reader["name"].ToString()!;
            template.Description = MysqlDatabaseDriver.NullableString(reader["description"]);
            template.Category = reader["category"].ToString()!;
            template.Content = reader["content"].ToString()!;
            template.IsBuiltIn = Convert.ToInt64(reader["is_built_in"]) == 1;
            template.Active = Convert.ToInt64(reader["active"]) == 1;
            template.CreatedUtc = MysqlDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!);
            template.LastUpdateUtc = MysqlDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!);
            return template;
        }

        private static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
