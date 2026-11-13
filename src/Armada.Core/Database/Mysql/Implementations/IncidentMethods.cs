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
    /// MySQL implementation of incident persistence (incidents: one row per incident holding its current state, paged and
    /// counted in the database).
    /// </summary>
    public class IncidentMethods : IIncidentMethods
    {
        #region Private-Members

        private static readonly string _Insert = @"INSERT INTO incidents
            (id, tenant_id, user_id, title, status, severity, vessel_id, environment_id, deployment_id, release_id, mission_id, voyage_id, failure_kind, search_text, payload, detected_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @user_id, @title, @status, @severity, @vessel_id, @environment_id, @deployment_id, @release_id, @mission_id, @voyage_id, @failure_kind, @search_text, @payload, @detected_utc, @last_update_utc);";

        private static readonly string _Update = @"UPDATE incidents SET
            tenant_id = @tenant_id, user_id = @user_id, title = @title, status = @status, severity = @severity, vessel_id = @vessel_id, environment_id = @environment_id, deployment_id = @deployment_id, release_id = @release_id, mission_id = @mission_id, voyage_id = @voyage_id, failure_kind = @failure_kind, search_text = @search_text, payload = @payload, detected_utc = @detected_utc, last_update_utc = @last_update_utc
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
        public IncidentMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _WriteLock = null;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Incident> CreateAsync(Incident incident, CancellationToken token = default)
        {
            if (incident == null) throw new ArgumentNullException(nameof(incident));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, incident), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return incident;
        }

        /// <inheritdoc />
        public async Task<Incident> UpdateAsync(Incident incident, CancellationToken token = default)
        {
            if (incident == null) throw new ArgumentNullException(nameof(incident));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, incident), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return incident;
        }

        /// <inheritdoc />
        public async Task<Incident?> ReadAsync(string id, IncidentQuery? scope = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            List<string> conditions = new List<string> { "id = @id" };
            if (scope != null && !String.IsNullOrWhiteSpace(scope.TenantId)) conditions.Add("tenant_id = @tenant_id");
            if (scope != null && !String.IsNullOrWhiteSpace(scope.UserId)) conditions.Add("user_id = @user_id");
            List<Incident?> rows = await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT id, payload FROM incidents WHERE " + String.Join(" AND ", conditions) + ";",
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
                deleted = await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM incidents WHERE id = @id;",
                    cmd => MysqlCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return deleted > 0;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Incident>> EnumerateAsync(IncidentQuery query, CancellationToken token = default)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            List<string> conditions = new List<string>();
            if (!String.IsNullOrWhiteSpace(query.TenantId)) conditions.Add("tenant_id = @tenant_id");
            if (!String.IsNullOrWhiteSpace(query.UserId)) conditions.Add("user_id = @user_id");
            if (!String.IsNullOrWhiteSpace(query.VesselId)) conditions.Add("vessel_id = @vessel_id");
            if (!String.IsNullOrWhiteSpace(query.EnvironmentId)) conditions.Add("environment_id = @environment_id");
            if (!String.IsNullOrWhiteSpace(query.DeploymentId)) conditions.Add("deployment_id = @deployment_id");
            if (!String.IsNullOrWhiteSpace(query.ReleaseId)) conditions.Add("release_id = @release_id");
            if (!String.IsNullOrWhiteSpace(query.MissionId)) conditions.Add("mission_id = @mission_id");
            if (!String.IsNullOrWhiteSpace(query.VoyageId)) conditions.Add("voyage_id = @voyage_id");
            if (query.Status.HasValue) conditions.Add("status = @status");
            if (query.Severity.HasValue) conditions.Add("severity = @severity");
            if (query.HasFailureKind.HasValue) conditions.Add(query.HasFailureKind.Value ? "failure_kind IS NOT NULL" : "failure_kind IS NULL");
            if (!String.IsNullOrWhiteSpace(query.Search)) conditions.Add("search_text LIKE @search");
            string where = conditions.Count > 0 ? " WHERE " + String.Join(" AND ", conditions) : String.Empty;
            Action<MySqlCommand> bind = cmd =>
            {
                if (!String.IsNullOrWhiteSpace(query.TenantId)) MysqlCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (!String.IsNullOrWhiteSpace(query.UserId)) MysqlCommandHelper.Add(cmd, "@user_id", query.UserId);
                if (!String.IsNullOrWhiteSpace(query.VesselId)) MysqlCommandHelper.Add(cmd, "@vessel_id", query.VesselId);
                if (!String.IsNullOrWhiteSpace(query.EnvironmentId)) MysqlCommandHelper.Add(cmd, "@environment_id", query.EnvironmentId);
                if (!String.IsNullOrWhiteSpace(query.DeploymentId)) MysqlCommandHelper.Add(cmd, "@deployment_id", query.DeploymentId);
                if (!String.IsNullOrWhiteSpace(query.ReleaseId)) MysqlCommandHelper.Add(cmd, "@release_id", query.ReleaseId);
                if (!String.IsNullOrWhiteSpace(query.MissionId)) MysqlCommandHelper.Add(cmd, "@mission_id", query.MissionId);
                if (!String.IsNullOrWhiteSpace(query.VoyageId)) MysqlCommandHelper.Add(cmd, "@voyage_id", query.VoyageId);
                if (query.Status.HasValue) MysqlCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.Severity.HasValue) MysqlCommandHelper.Add(cmd, "@severity", query.Severity.Value.ToString());
                if (!String.IsNullOrWhiteSpace(query.Search)) MysqlCommandHelper.Add(cmd, "@search", "%" + query.Search.Trim().ToLowerInvariant() + "%");
            };

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM incidents" + where + ";", bind, token).ConfigureAwait(false);
                List<Incident?> rows = await MysqlCommandHelper.QueryAsync(conn,
                    "SELECT id, payload FROM incidents" + where + " ORDER BY last_update_utc DESC, id DESC" + MysqlCommandHelper.Page(query.Offset, query.PageSize) + ";",
                    bind, FromReader, token).ConfigureAwait(false);

                EnumerationResult<Incident> result = new EnumerationResult<Incident>();
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
                return await MysqlCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM incidents;", null, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, Incident incident)
        {
            MysqlCommandHelper.Add(cmd, "@id", incident.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", incident.TenantId);
            MysqlCommandHelper.Add(cmd, "@user_id", incident.UserId);
            MysqlCommandHelper.Add(cmd, "@title", incident.Title);
            MysqlCommandHelper.Add(cmd, "@status", incident.Status.ToString());
            MysqlCommandHelper.Add(cmd, "@severity", incident.Severity.ToString());
            MysqlCommandHelper.Add(cmd, "@vessel_id", incident.VesselId);
            MysqlCommandHelper.Add(cmd, "@environment_id", incident.EnvironmentId);
            MysqlCommandHelper.Add(cmd, "@deployment_id", incident.DeploymentId);
            MysqlCommandHelper.Add(cmd, "@release_id", incident.ReleaseId);
            MysqlCommandHelper.Add(cmd, "@mission_id", incident.MissionId);
            MysqlCommandHelper.Add(cmd, "@voyage_id", incident.VoyageId);
            MysqlCommandHelper.Add(cmd, "@failure_kind", incident.FailureKind);
            MysqlCommandHelper.Add(cmd, "@search_text", IncidentRowCodec.SearchText(incident));
            MysqlCommandHelper.Add(cmd, "@payload", IncidentRowCodec.Serialize(incident));
            MysqlCommandHelper.AddDate(cmd, "@detected_utc", incident.DetectedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", incident.LastUpdateUtc);
        }

        private static Incident? FromReader(MySqlDataReader reader)
        {
            return IncidentRowCodec.Deserialize(reader["id"].ToString() ?? String.Empty, MysqlCommandHelper.ReadString(reader["payload"]));
        }

        #endregion
    }
}
