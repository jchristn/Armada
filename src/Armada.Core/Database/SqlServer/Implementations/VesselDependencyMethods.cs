namespace Armada.Core.Database.SqlServer.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.SqlClient;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQL Server implementation of vessel dependency persistence.
    /// </summary>
    public class VesselDependencyMethods : IVesselDependencyMethods
    {
        #region Private-Members

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
        public VesselDependencyMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task ReplaceForVesselAsync(string tenantId, string vesselId, List<VesselDependency> dependencies, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            if (dependencies == null) throw new ArgumentNullException(nameof(dependencies));

            DateTime now = DateTime.UtcNow;
            foreach (VesselDependency dependency in dependencies)
            {
                if (dependency == null) throw new ArgumentException("Dependencies must not contain null entries.", nameof(dependencies));
                if (String.IsNullOrEmpty(dependency.Id)) dependency.Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselDependencyIdPrefix, 24);
                dependency.TenantId = tenantId;
                dependency.VesselId = vesselId;
                dependency.LastUpdateUtc = now;
            }

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);

                foreach (VesselDependency dependency in dependencies)
                {
                    token.ThrowIfCancellationRequested();
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_dependencies
                        (id, tenant_id, vessel_id, ecosystem, project_path, package_name, current_version, latest_version, drift, is_vulnerable, severity, advisory_url, created_utc, last_update_utc)
                        VALUES
                        (@id, @tenant_id, @vessel_id, @ecosystem, @project_path, @package_name, @current_version, @latest_version, @drift, @is_vulnerable, @severity, @advisory_url, @created_utc, @last_update_utc);",
                        cmd => Bind(cmd, dependency), token).ConfigureAwait(false);
                }
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<VesselDependency>> ReadByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            return await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY ecosystem ASC, package_name ASC, project_path ASC, id ASC;",
                cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqlCommand cmd, VesselDependency dependency)
        {
            SqlServerCommandHelper.Add(cmd, "@id", dependency.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", dependency.TenantId);
            SqlServerCommandHelper.Add(cmd, "@vessel_id", dependency.VesselId);
            SqlServerCommandHelper.Add(cmd, "@ecosystem", dependency.Ecosystem);
            SqlServerCommandHelper.Add(cmd, "@project_path", dependency.ProjectPath);
            SqlServerCommandHelper.Add(cmd, "@package_name", dependency.PackageName);
            SqlServerCommandHelper.Add(cmd, "@current_version", dependency.CurrentVersion);
            SqlServerCommandHelper.Add(cmd, "@latest_version", dependency.LatestVersion);
            SqlServerCommandHelper.Add(cmd, "@drift", dependency.Drift.ToString());
            SqlServerCommandHelper.Add(cmd, "@is_vulnerable", dependency.IsVulnerable);
            SqlServerCommandHelper.Add(cmd, "@severity", dependency.Severity.ToString());
            SqlServerCommandHelper.Add(cmd, "@advisory_url", dependency.AdvisoryUrl);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", dependency.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", dependency.LastUpdateUtc);
        }

        private static VesselDependency FromReader(SqlDataReader reader)
        {
            VesselDependency dependency = new VesselDependency();
            dependency.Id = reader["id"].ToString()!;
            dependency.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            dependency.VesselId = SqlServerCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            dependency.Ecosystem = SqlServerCommandHelper.ReadString(reader["ecosystem"]) ?? String.Empty;
            dependency.ProjectPath = SqlServerCommandHelper.ReadString(reader["project_path"]);
            dependency.PackageName = SqlServerCommandHelper.ReadString(reader["package_name"]) ?? String.Empty;
            dependency.CurrentVersion = SqlServerCommandHelper.ReadString(reader["current_version"]);
            dependency.LatestVersion = SqlServerCommandHelper.ReadString(reader["latest_version"]);
            dependency.Drift = SqlServerCommandHelper.ReadEnum(reader["drift"], DependencyDriftEnum.None);
            dependency.IsVulnerable = SqlServerCommandHelper.ReadBool(reader["is_vulnerable"], false);
            dependency.Severity = SqlServerCommandHelper.ReadEnum(reader["severity"], VulnerabilitySeverityEnum.None);
            dependency.AdvisoryUrl = SqlServerCommandHelper.ReadString(reader["advisory_url"]);
            dependency.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            dependency.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return dependency;
        }

        #endregion
    }
}
