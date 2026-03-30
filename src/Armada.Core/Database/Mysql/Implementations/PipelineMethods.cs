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
    /// MySQL implementation of pipeline and pipeline stage database operations.
    /// </summary>
    public class PipelineMethods : IPipelineMethods
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
        public PipelineMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a pipeline with its stages.
        /// </summary>
        /// <param name="pipeline">Pipeline to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created pipeline.</returns>
        public async Task<Pipeline> CreateAsync(Pipeline pipeline, CancellationToken token = default)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            pipeline.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO pipelines (id, tenant_id, name, description, is_built_in, active, created_utc, last_update_utc)
                        VALUES (@id, @tenant_id, @name, @description, @is_built_in, @active, @created_utc, @last_update_utc);";
                    cmd.Parameters.AddWithValue("@id", pipeline.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)pipeline.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", pipeline.Name);
                    cmd.Parameters.AddWithValue("@description", (object?)pipeline.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@is_built_in", pipeline.IsBuiltIn ? 1 : 0);
                    cmd.Parameters.AddWithValue("@active", pipeline.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@created_utc", ToIso8601(pipeline.CreatedUtc));
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(pipeline.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                foreach (PipelineStage stage in pipeline.Stages)
                {
                    stage.PipelineId = pipeline.Id;
                    await InsertStageAsync(conn, stage, token).ConfigureAwait(false);
                }
            }

            return pipeline;
        }

        /// <summary>
        /// Read a pipeline by identifier, including its stages.
        /// </summary>
        /// <param name="id">Pipeline identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Pipeline or null if not found.</returns>
        public async Task<Pipeline?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                Pipeline? pipeline = null;

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pipelines WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            pipeline = PipelineFromReader(reader);
                    }
                }

                if (pipeline != null)
                    pipeline.Stages = await LoadStagesAsync(conn, pipeline.Id, token).ConfigureAwait(false);

                return pipeline;
            }
        }

        /// <summary>
        /// Read a pipeline by name, including its stages.
        /// </summary>
        /// <param name="name">Pipeline name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Pipeline or null if not found.</returns>
        public async Task<Pipeline?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                Pipeline? pipeline = null;

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pipelines WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            pipeline = PipelineFromReader(reader);
                    }
                }

                if (pipeline != null)
                    pipeline.Stages = await LoadStagesAsync(conn, pipeline.Id, token).ConfigureAwait(false);

                return pipeline;
            }
        }

        /// <summary>
        /// Read a pipeline by tenant and name, including its stages.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="name">Pipeline name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Pipeline or null if not found.</returns>
        public async Task<Pipeline?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                Pipeline? pipeline = null;

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pipelines WHERE tenant_id = @tenant_id AND name = @name;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@name", name);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            pipeline = PipelineFromReader(reader);
                    }
                }

                if (pipeline != null)
                    pipeline.Stages = await LoadStagesAsync(conn, pipeline.Id, token).ConfigureAwait(false);

                return pipeline;
            }
        }

        /// <summary>
        /// Update a pipeline and its stages.
        /// </summary>
        /// <param name="pipeline">Pipeline with updated values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated pipeline.</returns>
        public async Task<Pipeline> UpdateAsync(Pipeline pipeline, CancellationToken token = default)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            pipeline.LastUpdateUtc = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE pipelines SET
                        tenant_id = @tenant_id,
                        name = @name,
                        description = @description,
                        is_built_in = @is_built_in,
                        active = @active,
                        last_update_utc = @last_update_utc
                        WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", pipeline.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)pipeline.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", pipeline.Name);
                    cmd.Parameters.AddWithValue("@description", (object?)pipeline.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@is_built_in", pipeline.IsBuiltIn ? 1 : 0);
                    cmd.Parameters.AddWithValue("@active", pipeline.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@last_update_utc", ToIso8601(pipeline.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Delete existing stages and reinsert
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM pipeline_stages WHERE pipeline_id = @pipeline_id;";
                    cmd.Parameters.AddWithValue("@pipeline_id", pipeline.Id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                foreach (PipelineStage stage in pipeline.Stages)
                {
                    stage.PipelineId = pipeline.Id;
                    await InsertStageAsync(conn, stage, token).ConfigureAwait(false);
                }
            }

            return pipeline;
        }

        /// <summary>
        /// Delete a pipeline and its stages by identifier.
        /// </summary>
        /// <param name="id">Pipeline identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                // Delete stages first
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM pipeline_stages WHERE pipeline_id = @pipeline_id;";
                    cmd.Parameters.AddWithValue("@pipeline_id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Delete pipeline
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM pipelines WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Enumerate all pipelines, including their stages.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of all pipelines.</returns>
        public async Task<List<Pipeline>> EnumerateAsync(CancellationToken token = default)
        {
            List<Pipeline> results = new List<Pipeline>();

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pipelines ORDER BY name;";
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(PipelineFromReader(reader));
                    }
                }

                foreach (Pipeline pipeline in results)
                {
                    pipeline.Stages = await LoadStagesAsync(conn, pipeline.Id, token).ConfigureAwait(false);
                }
            }

            return results;
        }

        /// <summary>
        /// Enumerate pipelines with pagination and filtering, including their stages.
        /// </summary>
        /// <param name="query">Enumeration query parameters.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Paginated enumeration result.</returns>
        public async Task<EnumerationResult<Pipeline>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
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
                    cmd.CommandText = "SELECT COUNT(*) FROM pipelines" + whereClause + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    totalCount = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                }

                // Query
                List<Pipeline> results = new List<Pipeline>();
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pipelines" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (MySqlParameter p in parameters) cmd.Parameters.Add(new MySqlParameter(p.ParameterName, p.Value));
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                            results.Add(PipelineFromReader(reader));
                    }
                }

                // Load stages for each pipeline
                foreach (Pipeline pipeline in results)
                {
                    pipeline.Stages = await LoadStagesAsync(conn, pipeline.Id, token).ConfigureAwait(false);
                }

                return EnumerationResult<Pipeline>.Create(query, results, totalCount);
            }
        }

        /// <summary>
        /// Check if a pipeline exists by identifier.
        /// </summary>
        /// <param name="id">Pipeline identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the pipeline exists.</returns>
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM pipelines WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        /// <summary>
        /// Check if a pipeline exists by name.
        /// </summary>
        /// <param name="name">Pipeline name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the pipeline exists.</returns>
        public async Task<bool> ExistsByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM pipelines WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    long count = Convert.ToInt64(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false));
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Insert a single pipeline stage row.
        /// </summary>
        /// <param name="conn">Open MySQL connection.</param>
        /// <param name="stage">Pipeline stage to insert.</param>
        /// <param name="token">Cancellation token.</param>
        private static async Task InsertStageAsync(MySqlConnection conn, PipelineStage stage, CancellationToken token)
        {
            using (MySqlCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO pipeline_stages (id, pipeline_id, stage_order, persona_name, is_optional, description)
                        VALUES (@id, @pipeline_id, @stage_order, @persona_name, @is_optional, @description);";
                cmd.Parameters.AddWithValue("@id", stage.Id);
                cmd.Parameters.AddWithValue("@pipeline_id", (object?)stage.PipelineId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@stage_order", stage.Order);
                cmd.Parameters.AddWithValue("@persona_name", stage.PersonaName);
                cmd.Parameters.AddWithValue("@is_optional", stage.IsOptional ? 1 : 0);
                cmd.Parameters.AddWithValue("@description", (object?)stage.Description ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Load all stages for a pipeline, ordered by stage_order.
        /// </summary>
        /// <param name="conn">Open MySQL connection.</param>
        /// <param name="pipelineId">Pipeline identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of pipeline stages ordered by stage_order.</returns>
        private static async Task<List<PipelineStage>> LoadStagesAsync(MySqlConnection conn, string pipelineId, CancellationToken token)
        {
            List<PipelineStage> stages = new List<PipelineStage>();

            using (MySqlCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM pipeline_stages WHERE pipeline_id = @pipeline_id ORDER BY stage_order;";
                cmd.Parameters.AddWithValue("@pipeline_id", pipelineId);
                using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        stages.Add(StageFromReader(reader));
                }
            }

            return stages;
        }

        /// <summary>
        /// Convert a MySqlDataReader row to a Pipeline model (without stages).
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Pipeline instance.</returns>
        private static Pipeline PipelineFromReader(MySqlDataReader reader)
        {
            Pipeline pipeline = new Pipeline();
            pipeline.Id = reader["id"].ToString()!;
            pipeline.TenantId = MysqlDatabaseDriver.NullableString(reader["tenant_id"]);
            pipeline.Name = reader["name"].ToString()!;
            pipeline.Description = MysqlDatabaseDriver.NullableString(reader["description"]);
            pipeline.IsBuiltIn = Convert.ToInt64(reader["is_built_in"]) == 1;
            pipeline.Active = Convert.ToInt64(reader["active"]) == 1;
            pipeline.CreatedUtc = MysqlDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!);
            pipeline.LastUpdateUtc = MysqlDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!);
            return pipeline;
        }

        /// <summary>
        /// Convert a MySqlDataReader row to a PipelineStage model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>PipelineStage instance.</returns>
        private static PipelineStage StageFromReader(MySqlDataReader reader)
        {
            PipelineStage stage = new PipelineStage();
            stage.Id = reader["id"].ToString()!;
            stage.PipelineId = MysqlDatabaseDriver.NullableString(reader["pipeline_id"]);
            stage.Order = Convert.ToInt32(reader["stage_order"]);
            stage.PersonaName = reader["persona_name"].ToString()!;
            stage.IsOptional = Convert.ToInt64(reader["is_optional"]) == 1;
            stage.Description = MysqlDatabaseDriver.NullableString(reader["description"]);
            return stage;
        }

        private static string ToIso8601(DateTime dt)
        {
            return dt.ToUniversalTime().ToString(_Iso8601Format, CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
