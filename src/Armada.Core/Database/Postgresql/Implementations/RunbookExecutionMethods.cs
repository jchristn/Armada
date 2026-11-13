namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of runbook execution persistence (runbook_executions: one row per runbook execution holding its current state, paged and
    /// counted in the database).
    /// </summary>
    public class RunbookExecutionMethods : IRunbookExecutionMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO runbook_executions
            (id, tenant_id, user_id, runbook_id, deployment_id, incident_id, status, search_text, payload, started_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @runbook_id, @deployment_id, @incident_id, @status, @search_text, @payload, @started_utc, @last_update_utc);";

        private static readonly string _Update = @"UPDATE runbook_executions SET
            tenant_id = @tenant_id, user_id = @user_id, runbook_id = @runbook_id, deployment_id = @deployment_id, incident_id = @incident_id, status = @status, search_text = @search_text, payload = @payload, started_utc = @started_utc, last_update_utc = @last_update_utc
            WHERE id = @id;";

        private readonly string _ConnectionString;
        private readonly SemaphoreSlim? _WriteLock;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public RunbookExecutionMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<RunbookExecution> CreateAsync(RunbookExecution execution, CancellationToken token = default)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, execution), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return execution;
        }

        /// <inheritdoc />
        public async Task<RunbookExecution> UpdateAsync(RunbookExecution execution, CancellationToken token = default)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, execution), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return execution;
        }

        /// <inheritdoc />
        public async Task<RunbookExecution?> ReadAsync(string id, RunbookExecutionQuery? scope = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            List<string> conditions = new List<string> { "id = @id" };
            if (scope != null && !String.IsNullOrWhiteSpace(scope.TenantId)) conditions.Add("tenant_id = @tenant_id");
            if (scope != null && !String.IsNullOrWhiteSpace(scope.UserId)) conditions.Add("user_id = @user_id");
            List<RunbookExecution?> rows = await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT id, payload FROM runbook_executions WHERE " + String.Join(" AND ", conditions) + ";",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@id", id);
                    if (scope != null && !String.IsNullOrWhiteSpace(scope.TenantId)) PostgresqlCommandHelper.Add(cmd, "@tenant_id", scope.TenantId);
                    if (scope != null && !String.IsNullOrWhiteSpace(scope.UserId)) PostgresqlCommandHelper.Add(cmd, "@user_id", scope.UserId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault(row => row != null);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                deleted = await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM runbook_executions WHERE id = @id;",
                    cmd => PostgresqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted > 0;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<RunbookExecution>> EnumerateAsync(RunbookExecutionQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> conditions = new List<string>();
            if (!String.IsNullOrWhiteSpace(query.TenantId)) conditions.Add("tenant_id = @tenant_id");
            if (!String.IsNullOrWhiteSpace(query.UserId)) conditions.Add("user_id = @user_id");
            if (!String.IsNullOrWhiteSpace(query.RunbookId)) conditions.Add("runbook_id = @runbook_id");
            if (!String.IsNullOrWhiteSpace(query.DeploymentId)) conditions.Add("deployment_id = @deployment_id");
            if (!String.IsNullOrWhiteSpace(query.IncidentId)) conditions.Add("incident_id = @incident_id");
            if (query.Status.HasValue) conditions.Add("status = @status");
            if (!String.IsNullOrWhiteSpace(query.Search)) conditions.Add("search_text LIKE @search");
            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
            Action<NpgsqlCommand> bind = cmd =>
            {
                if (!String.IsNullOrWhiteSpace(query.TenantId)) PostgresqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (!String.IsNullOrWhiteSpace(query.UserId)) PostgresqlCommandHelper.Add(cmd, "@user_id", query.UserId);
                if (!String.IsNullOrWhiteSpace(query.RunbookId)) PostgresqlCommandHelper.Add(cmd, "@runbook_id", query.RunbookId);
                if (!String.IsNullOrWhiteSpace(query.DeploymentId)) PostgresqlCommandHelper.Add(cmd, "@deployment_id", query.DeploymentId);
                if (!String.IsNullOrWhiteSpace(query.IncidentId)) PostgresqlCommandHelper.Add(cmd, "@incident_id", query.IncidentId);
                if (query.Status.HasValue) PostgresqlCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (!String.IsNullOrWhiteSpace(query.Search)) PostgresqlCommandHelper.Add(cmd, "@search", "%" + query.Search.Trim().ToLowerInvariant() + "%");
            };

            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM runbook_executions" + where + ";", bind, token).ConfigureAwait(false);
                List<RunbookExecution?> rows = await PostgresqlCommandHelper.QueryAsync(conn,
                    "SELECT id, payload FROM runbook_executions" + where + " ORDER BY last_update_utc DESC, id DESC" + PostgresqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<RunbookExecution> result = new EnumerationResult<RunbookExecution>();
                result.PageNumber = query.PageNumber;
                result.PageSize = query.PageSize;
                result.TotalRecords = total;
                result.TotalPages = (int)Math.Ceiling((double)total / query.PageSize);
                result.Objects = rows.Where(row => row != null).Select(row => row!).ToList();
                return result;
            }
        }

        /// <inheritdoc />
        public async Task<long> CountAsync(CancellationToken token = default)
        {
            using (NpgsqlConnection conn = new NpgsqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await PostgresqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM runbook_executions;", null, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, RunbookExecution execution)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", execution.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", execution.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@user_id", execution.UserId);
            PostgresqlCommandHelper.Add(cmd, "@runbook_id", execution.RunbookId);
            PostgresqlCommandHelper.Add(cmd, "@deployment_id", execution.DeploymentId);
            PostgresqlCommandHelper.Add(cmd, "@incident_id", execution.IncidentId);
            PostgresqlCommandHelper.Add(cmd, "@status", execution.Status.ToString());
            PostgresqlCommandHelper.Add(cmd, "@search_text", RunbookExecutionRowCodec.SearchText(execution));
            PostgresqlCommandHelper.Add(cmd, "@payload", RunbookExecutionRowCodec.Serialize(execution));
            PostgresqlCommandHelper.AddDate(cmd, "@started_utc", execution.StartedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", execution.LastUpdateUtc);
        }

        private static RunbookExecution? FromReader(NpgsqlDataReader reader)
        {
            return RunbookExecutionRowCodec.Deserialize(reader["id"].ToString() ?? String.Empty, PostgresqlCommandHelper.ReadString(reader["payload"]));
        }

        #endregion
    }
}
