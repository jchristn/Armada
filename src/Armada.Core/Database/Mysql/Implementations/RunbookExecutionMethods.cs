namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of runbook execution persistence (runbook_executions: one row per runbook execution holding its current state, paged and
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
        /// <param name="connectionString">Connection string.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public RunbookExecutionMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<RunbookExecution> CreateAsync(RunbookExecution execution, CancellationToken token = default)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, execution), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return execution;
        }

        /// <inheritdoc />
        public async Task<RunbookExecution> UpdateAsync(RunbookExecution execution, CancellationToken token = default)
        {
            if (execution == null) throw new ArgumentNullException(nameof(execution));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, execution), token).ConfigureAwait(false);
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
            List<RunbookExecution?> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT id, payload FROM runbook_executions WHERE " + String.Join(" AND ", conditions) + ";",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@id", id);
                    if (scope != null && !String.IsNullOrWhiteSpace(scope.TenantId)) MysqlCommandHelper.Add(cmd, "@tenant_id", scope.TenantId);
                    if (scope != null && !String.IsNullOrWhiteSpace(scope.UserId)) MysqlCommandHelper.Add(cmd, "@user_id", scope.UserId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault(row => row != null);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM runbook_executions WHERE id = @id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
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
            Action<MySqlCommand> bind = cmd =>
            {
                if (!String.IsNullOrWhiteSpace(query.TenantId)) MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (!String.IsNullOrWhiteSpace(query.UserId)) MysqlCommandHelper.Add(cmd, "@user_id", query.UserId);
                if (!String.IsNullOrWhiteSpace(query.RunbookId)) MysqlCommandHelper.Add(cmd, "@runbook_id", query.RunbookId);
                if (!String.IsNullOrWhiteSpace(query.DeploymentId)) MysqlCommandHelper.Add(cmd, "@deployment_id", query.DeploymentId);
                if (!String.IsNullOrWhiteSpace(query.IncidentId)) MysqlCommandHelper.Add(cmd, "@incident_id", query.IncidentId);
                if (query.Status.HasValue) MysqlCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (!String.IsNullOrWhiteSpace(query.Search)) MysqlCommandHelper.Add(cmd, "@search", "%" + query.Search.Trim().ToLowerInvariant() + "%");
            };

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM runbook_executions" + where + ";", bind, token).ConfigureAwait(false);
                List<RunbookExecution?> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT id, payload FROM runbook_executions" + where + " ORDER BY last_update_utc DESC, id DESC" + MysqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
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
            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM runbook_executions;", null, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, RunbookExecution execution)
        {
            MysqlCommandHelper.Add(cmd, "@id", execution.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", execution.TenantId);
            MysqlCommandHelper.Add(cmd, "@user_id", execution.UserId);
            MysqlCommandHelper.Add(cmd, "@runbook_id", execution.RunbookId);
            MysqlCommandHelper.Add(cmd, "@deployment_id", execution.DeploymentId);
            MysqlCommandHelper.Add(cmd, "@incident_id", execution.IncidentId);
            MysqlCommandHelper.Add(cmd, "@status", execution.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@search_text", RunbookExecutionRowCodec.SearchText(execution));
            MysqlCommandHelper.Add(cmd, "@payload", RunbookExecutionRowCodec.Serialize(execution));
            MysqlCommandHelper.AddDate(cmd, "@started_utc", execution.StartedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", execution.LastUpdateUtc);
        }

        private static RunbookExecution? FromReader(MySqlDataReader reader)
        {
            return RunbookExecutionRowCodec.Deserialize(reader["id"].ToString() ?? String.Empty, MysqlCommandHelper.ReadString(reader["payload"]));
        }

        #endregion
    }
}
