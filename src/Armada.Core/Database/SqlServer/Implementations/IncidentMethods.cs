namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of incident persistence (incidents: one row per incident holding its current state, paged and
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
        /// <param name="driver">SQL Server database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is null.</exception>
        public IncidentMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<Incident> CreateAsync(Incident incident, CancellationToken token = default)
        {
            if (incident == null) throw new ArgumentNullException(nameof(incident));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Insert, cmd => Bind(cmd, incident), token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
            return incident;
        }

        /// <inheritdoc />
        public async Task<Incident> UpdateAsync(Incident incident, CancellationToken token = default)
        {
            if (incident == null) throw new ArgumentNullException(nameof(incident));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, _Update, cmd => Bind(cmd, incident), token).ConfigureAwait(false);
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
            List<Incident?> rows = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT id, payload FROM incidents WHERE " + String.Join(" AND ", conditions) + ";",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@id", id);
                    if (scope != null && !String.IsNullOrWhiteSpace(scope.TenantId)) SqlServerCommandHelper.Add(cmd, "@tenant_id", scope.TenantId);
                    if (scope != null && !String.IsNullOrWhiteSpace(scope.UserId)) SqlServerCommandHelper.Add(cmd, "@user_id", scope.UserId);
                }, FromReader, token).ConfigureAwait(false);
            return rows.FirstOrDefault(row => row != null);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            int deleted = 0;
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                deleted = await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM incidents WHERE id = @id;",
                    cmd => SqlServerCommandHelper.Add(cmd, "@id", id), token).ConfigureAwait(false);
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
            Action<SqlCommand> bind = cmd =>
            {
                if (!String.IsNullOrWhiteSpace(query.TenantId)) SqlServerCommandHelper.Add(cmd, "@tenant_id", query.TenantId);
                if (!String.IsNullOrWhiteSpace(query.UserId)) SqlServerCommandHelper.Add(cmd, "@user_id", query.UserId);
                if (!String.IsNullOrWhiteSpace(query.VesselId)) SqlServerCommandHelper.Add(cmd, "@vessel_id", query.VesselId);
                if (!String.IsNullOrWhiteSpace(query.EnvironmentId)) SqlServerCommandHelper.Add(cmd, "@environment_id", query.EnvironmentId);
                if (!String.IsNullOrWhiteSpace(query.DeploymentId)) SqlServerCommandHelper.Add(cmd, "@deployment_id", query.DeploymentId);
                if (!String.IsNullOrWhiteSpace(query.ReleaseId)) SqlServerCommandHelper.Add(cmd, "@release_id", query.ReleaseId);
                if (!String.IsNullOrWhiteSpace(query.MissionId)) SqlServerCommandHelper.Add(cmd, "@mission_id", query.MissionId);
                if (!String.IsNullOrWhiteSpace(query.VoyageId)) SqlServerCommandHelper.Add(cmd, "@voyage_id", query.VoyageId);
                if (query.Status.HasValue) SqlServerCommandHelper.Add(cmd, "@status", query.Status.Value.ToString());
                if (query.Severity.HasValue) SqlServerCommandHelper.Add(cmd, "@severity", query.Severity.Value.ToString());
                if (!String.IsNullOrWhiteSpace(query.Search)) SqlServerCommandHelper.Add(cmd, "@search", "%" + query.Search.Trim().ToLowerInvariant() + "%");
            };

            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                long total = await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM incidents" + where + ";", bind, token).ConfigureAwait(false);
                List<Incident?> rows = await SqlServerCommandHelper.QueryAsync(conn,
                    "SELECT id, payload FROM incidents" + where + " ORDER BY last_update_utc DESC, id DESC" + SqlServerCommandHelper.Page(query.Offset, query.PageSize) + ";",
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
            using (SqlConnection conn = new SqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                return await SqlServerCommandHelper.CountAsync(conn, "SELECT COUNT(*) FROM incidents;", null, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, Incident incident)
        {
            SqlServerCommandHelper.Add(cmd, "@id", incident.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", incident.TenantId);
            SqlServerCommandHelper.Add(cmd, "@user_id", incident.UserId);
            SqlServerCommandHelper.Add(cmd, "@title", incident.Title);
            SqlServerCommandHelper.Add(cmd, "@status", incident.Status.ToString());
            SqlServerCommandHelper.Add(cmd, "@severity", incident.Severity.ToString());
            SqlServerCommandHelper.Add(cmd, "@vessel_id", incident.VesselId);
            SqlServerCommandHelper.Add(cmd, "@environment_id", incident.EnvironmentId);
            SqlServerCommandHelper.Add(cmd, "@deployment_id", incident.DeploymentId);
            SqlServerCommandHelper.Add(cmd, "@release_id", incident.ReleaseId);
            SqlServerCommandHelper.Add(cmd, "@mission_id", incident.MissionId);
            SqlServerCommandHelper.Add(cmd, "@voyage_id", incident.VoyageId);
            SqlServerCommandHelper.Add(cmd, "@failure_kind", incident.FailureKind);
            SqlServerCommandHelper.Add(cmd, "@search_text", IncidentRowCodec.SearchText(incident));
            SqlServerCommandHelper.Add(cmd, "@payload", IncidentRowCodec.Serialize(incident));
            SqlServerCommandHelper.AddDate(cmd, "@detected_utc", incident.DetectedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", incident.LastUpdateUtc);
        }

        private static Incident? FromReader(SqlDataReader reader)
        {
            return IncidentRowCodec.Deserialize(reader["id"].ToString() ?? String.Empty, SqlServerCommandHelper.ReadString(reader["payload"]));
        }

        #endregion
    }
}
