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
    /// SQLite implementation of pipeline and pipeline stage database operations.
    /// </summary>
    public class PipelineMethods : IPipelineMethods
    {
        #region Private-Members

#pragma warning disable CS0414
        private readonly string _Header = "[PipelineMethods] ";
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
        public PipelineMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Pipeline> CreateAsync(Pipeline pipeline, CancellationToken token = default)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            pipeline.LastUpdateUtc = DateTime.UtcNow;

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO pipelines (id, tenant_id, name, description, is_built_in, active, created_utc, last_update_utc)
                            VALUES (@id, @tenant_id, @name, @description, @is_built_in, @active, @created_utc, @last_update_utc);";
                    cmd.Parameters.AddWithValue("@id", pipeline.Id);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)pipeline.TenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@name", pipeline.Name);
                    cmd.Parameters.AddWithValue("@description", (object?)pipeline.Description ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@is_built_in", pipeline.IsBuiltIn ? 1 : 0);
                    cmd.Parameters.AddWithValue("@active", pipeline.Active ? 1 : 0);
                    cmd.Parameters.AddWithValue("@created_utc", SqliteDatabaseDriver.ToIso8601(pipeline.CreatedUtc));
                    cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(pipeline.LastUpdateUtc));
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

        /// <inheritdoc />
        public async Task<Pipeline?> ReadAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                Pipeline? pipeline = null;

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pipelines WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

        /// <inheritdoc />
        public async Task<Pipeline?> ReadByNameAsync(string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                Pipeline? pipeline = null;

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pipelines WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

        /// <inheritdoc />
        public async Task<Pipeline?> ReadByNameAsync(string tenantId, string name, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                Pipeline? pipeline = null;

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pipelines WHERE tenant_id = @tenant_id AND name = @name;";
                    cmd.Parameters.AddWithValue("@tenant_id", tenantId);
                    cmd.Parameters.AddWithValue("@name", name);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

        /// <inheritdoc />
        public async Task<Pipeline> UpdateAsync(Pipeline pipeline, CancellationToken token = default)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            pipeline.LastUpdateUtc = DateTime.UtcNow;

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                using (SqliteCommand cmd = conn.CreateCommand())
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
                    cmd.Parameters.AddWithValue("@last_update_utc", SqliteDatabaseDriver.ToIso8601(pipeline.LastUpdateUtc));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Delete existing stages and reinsert
                using (SqliteCommand cmd = conn.CreateCommand())
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

        /// <inheritdoc />
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                // Delete stages first
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM pipeline_stages WHERE pipeline_id = @pipeline_id;";
                    cmd.Parameters.AddWithValue("@pipeline_id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // Delete pipeline
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM pipelines WHERE id = @id;";
                    cmd.Parameters.AddWithValue("@id", id);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<List<Pipeline>> EnumerateAsync(CancellationToken token = default)
        {
            List<Pipeline> results = new List<Pipeline>();

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pipelines ORDER BY name;";
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

        /// <inheritdoc />
        public async Task<EnumerationResult<Pipeline>> EnumerateAsync(EnumerationQuery query, CancellationToken token = default)
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

                string whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";
                string orderDirection = query.Order == EnumerationOrderEnum.CreatedAscending ? "ASC" : "DESC";

                // Count
                long totalCount = 0;
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM pipelines" + whereClause + ";";
                    foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                    totalCount = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                }

                // Query
                List<Pipeline> results = new List<Pipeline>();
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM pipelines" + whereClause +
                        " ORDER BY created_utc " + orderDirection +
                        " LIMIT " + query.PageSize + " OFFSET " + query.Offset + ";";
                    foreach (SqliteParameter p in parameters) cmd.Parameters.Add(new SqliteParameter(p.ParameterName, p.Value));
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
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

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(string id, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM pipelines WHERE id = @id;";
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

            using (SqliteConnection conn = new SqliteConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM pipelines WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    long count = (long)(await cmd.ExecuteScalarAsync(token).ConfigureAwait(false))!;
                    return count > 0;
                }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Insert a single pipeline stage row.
        /// </summary>
        /// <param name="conn">Open SQLite connection.</param>
        /// <param name="stage">Pipeline stage to insert.</param>
        /// <param name="token">Cancellation token.</param>
        private static async Task InsertStageAsync(SqliteConnection conn, PipelineStage stage, CancellationToken token)
        {
            using (SqliteCommand cmd = conn.CreateCommand())
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
        /// <param name="conn">Open SQLite connection.</param>
        /// <param name="pipelineId">Pipeline identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>List of pipeline stages ordered by stage_order.</returns>
        private static async Task<List<PipelineStage>> LoadStagesAsync(SqliteConnection conn, string pipelineId, CancellationToken token)
        {
            List<PipelineStage> stages = new List<PipelineStage>();

            using (SqliteCommand cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM pipeline_stages WHERE pipeline_id = @pipeline_id ORDER BY stage_order;";
                cmd.Parameters.AddWithValue("@pipeline_id", pipelineId);
                using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(token).ConfigureAwait(false))
                        stages.Add(StageFromReader(reader));
                }
            }

            return stages;
        }

        /// <summary>
        /// Convert a SqliteDataReader row to a Pipeline model (without stages).
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>Pipeline instance.</returns>
        private static Pipeline PipelineFromReader(SqliteDataReader reader)
        {
            Pipeline pipeline = new Pipeline();
            pipeline.Id = reader["id"].ToString()!;
            pipeline.TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]);
            pipeline.Name = reader["name"].ToString()!;
            pipeline.Description = SqliteDatabaseDriver.NullableString(reader["description"]);
            pipeline.IsBuiltIn = Convert.ToInt64(reader["is_built_in"]) == 1;
            pipeline.Active = Convert.ToInt64(reader["active"]) == 1;
            pipeline.CreatedUtc = SqliteDatabaseDriver.FromIso8601(reader["created_utc"].ToString()!);
            pipeline.LastUpdateUtc = SqliteDatabaseDriver.FromIso8601(reader["last_update_utc"].ToString()!);
            return pipeline;
        }

        /// <summary>
        /// Convert a SqliteDataReader row to a PipelineStage model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>PipelineStage instance.</returns>
        private static PipelineStage StageFromReader(SqliteDataReader reader)
        {
            PipelineStage stage = new PipelineStage();
            stage.Id = reader["id"].ToString()!;
            stage.PipelineId = SqliteDatabaseDriver.NullableString(reader["pipeline_id"]);
            stage.Order = Convert.ToInt32(reader["stage_order"]);
            stage.PersonaName = reader["persona_name"].ToString()!;
            stage.IsOptional = Convert.ToInt64(reader["is_optional"]) == 1;
            stage.Description = SqliteDatabaseDriver.NullableString(reader["description"]);
            return stage;
        }

        #endregion
    }
}
