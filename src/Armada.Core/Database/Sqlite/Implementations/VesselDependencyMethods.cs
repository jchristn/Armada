namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// SQLite implementation of vessel dependency persistence.
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
        /// <param name="driver">SQLite database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public VesselDependencyMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            if (driver == null) throw new ArgumentNullException(nameof(driver));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _ConnectionString = driver.ConnectionString;
            _WriteLock = driver.WriteLock;
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

            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);

                foreach (VesselDependency dependency in dependencies)
                {
                    token.ThrowIfCancellationRequested();
                    await SqliteCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_dependencies
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
            return await SqliteCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY ecosystem ASC, package_name ASC, project_path ASC, id ASC;",
                cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await SqliteCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqliteConnection conn, SqliteTransaction tx) =>
            {
                await SqliteCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    SqliteCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqliteCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(SqliteCommand cmd, VesselDependency dependency)
        {
            SqliteCommandHelper.Add(cmd, "@id", dependency.Id);
            SqliteCommandHelper.Add(cmd, "@tenant_id", dependency.TenantId);
            SqliteCommandHelper.Add(cmd, "@vessel_id", dependency.VesselId);
            SqliteCommandHelper.Add(cmd, "@ecosystem", dependency.Ecosystem);
            SqliteCommandHelper.Add(cmd, "@project_path", dependency.ProjectPath);
            SqliteCommandHelper.Add(cmd, "@package_name", dependency.PackageName);
            SqliteCommandHelper.Add(cmd, "@current_version", dependency.CurrentVersion);
            SqliteCommandHelper.Add(cmd, "@latest_version", dependency.LatestVersion);
            SqliteCommandHelper.Add(cmd, "@drift", dependency.Drift.ToString());
            SqliteCommandHelper.Add(cmd, "@is_vulnerable", dependency.IsVulnerable);
            SqliteCommandHelper.Add(cmd, "@severity", dependency.Severity.ToString());
            SqliteCommandHelper.Add(cmd, "@advisory_url", dependency.AdvisoryUrl);
            SqliteCommandHelper.AddDate(cmd, "@created_utc", dependency.CreatedUtc);
            SqliteCommandHelper.AddDate(cmd, "@last_update_utc", dependency.LastUpdateUtc);
        }

        private static VesselDependency FromReader(SqliteDataReader reader)
        {
            VesselDependency dependency = new VesselDependency();
            dependency.Id = reader["id"].ToString()!;
            dependency.TenantId = SqliteCommandHelper.ReadString(reader["tenant_id"]);
            dependency.VesselId = SqliteCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            dependency.Ecosystem = SqliteCommandHelper.ReadString(reader["ecosystem"]) ?? String.Empty;
            dependency.ProjectPath = SqliteCommandHelper.ReadString(reader["project_path"]);
            dependency.PackageName = SqliteCommandHelper.ReadString(reader["package_name"]) ?? String.Empty;
            dependency.CurrentVersion = SqliteCommandHelper.ReadString(reader["current_version"]);
            dependency.LatestVersion = SqliteCommandHelper.ReadString(reader["latest_version"]);
            dependency.Drift = SqliteCommandHelper.ReadEnum(reader["drift"], DependencyDriftEnum.None);
            dependency.IsVulnerable = SqliteCommandHelper.ReadBool(reader["is_vulnerable"], false);
            dependency.Severity = SqliteCommandHelper.ReadEnum(reader["severity"], VulnerabilitySeverityEnum.None);
            dependency.AdvisoryUrl = SqliteCommandHelper.ReadString(reader["advisory_url"]);
            dependency.CreatedUtc = SqliteCommandHelper.ReadDate(reader["created_utc"]);
            dependency.LastUpdateUtc = SqliteCommandHelper.ReadDate(reader["last_update_utc"]);
            return dependency;
        }

        #endregion
    }
}
