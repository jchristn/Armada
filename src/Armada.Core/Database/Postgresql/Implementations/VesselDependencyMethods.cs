namespace Armada.Core.Database.Postgresql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Npgsql;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// PostgreSQL implementation of vessel dependency persistence.
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
        /// <param name="driver">PostgreSQL database driver.</param>
        /// <param name="settings">Database settings.</param>
        /// <param name="logging">Logging module.</param>
        public VesselDependencyMethods(PostgresqlDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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

            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);

                foreach (VesselDependency dependency in dependencies)
                {
                    token.ThrowIfCancellationRequested();
                    await PostgresqlCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_dependencies
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
            return await PostgresqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY ecosystem ASC, package_name ASC, project_path ASC, id ASC;",
                cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await PostgresqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (NpgsqlConnection conn, NpgsqlTransaction tx) =>
            {
                await PostgresqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    PostgresqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    PostgresqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(NpgsqlCommand cmd, VesselDependency dependency)
        {
            PostgresqlCommandHelper.Add(cmd, "@id", dependency.Id);
            PostgresqlCommandHelper.Add(cmd, "@tenant_id", dependency.TenantId);
            PostgresqlCommandHelper.Add(cmd, "@vessel_id", dependency.VesselId);
            PostgresqlCommandHelper.Add(cmd, "@ecosystem", dependency.Ecosystem);
            PostgresqlCommandHelper.Add(cmd, "@project_path", dependency.ProjectPath);
            PostgresqlCommandHelper.Add(cmd, "@package_name", dependency.PackageName);
            PostgresqlCommandHelper.Add(cmd, "@current_version", dependency.CurrentVersion);
            PostgresqlCommandHelper.Add(cmd, "@latest_version", dependency.LatestVersion);
            PostgresqlCommandHelper.Add(cmd, "@drift", dependency.Drift.ToString());
            PostgresqlCommandHelper.Add(cmd, "@is_vulnerable", dependency.IsVulnerable);
            PostgresqlCommandHelper.Add(cmd, "@severity", dependency.Severity.ToString());
            PostgresqlCommandHelper.Add(cmd, "@advisory_url", dependency.AdvisoryUrl);
            PostgresqlCommandHelper.AddDate(cmd, "@created_utc", dependency.CreatedUtc);
            PostgresqlCommandHelper.AddDate(cmd, "@last_update_utc", dependency.LastUpdateUtc);
        }

        private static VesselDependency FromReader(NpgsqlDataReader reader)
        {
            VesselDependency dependency = new VesselDependency();
            dependency.Id = reader["id"].ToString()!;
            dependency.TenantId = PostgresqlCommandHelper.ReadString(reader["tenant_id"]);
            dependency.VesselId = PostgresqlCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            dependency.Ecosystem = PostgresqlCommandHelper.ReadString(reader["ecosystem"]) ?? String.Empty;
            dependency.ProjectPath = PostgresqlCommandHelper.ReadString(reader["project_path"]);
            dependency.PackageName = PostgresqlCommandHelper.ReadString(reader["package_name"]) ?? String.Empty;
            dependency.CurrentVersion = PostgresqlCommandHelper.ReadString(reader["current_version"]);
            dependency.LatestVersion = PostgresqlCommandHelper.ReadString(reader["latest_version"]);
            dependency.Drift = PostgresqlCommandHelper.ReadEnum(reader["drift"], DependencyDriftEnum.None);
            dependency.IsVulnerable = PostgresqlCommandHelper.ReadBool(reader["is_vulnerable"], false);
            dependency.Severity = PostgresqlCommandHelper.ReadEnum(reader["severity"], VulnerabilitySeverityEnum.None);
            dependency.AdvisoryUrl = PostgresqlCommandHelper.ReadString(reader["advisory_url"]);
            dependency.CreatedUtc = PostgresqlCommandHelper.ReadDate(reader["created_utc"]);
            dependency.LastUpdateUtc = PostgresqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return dependency;
        }

        #endregion
    }
}
