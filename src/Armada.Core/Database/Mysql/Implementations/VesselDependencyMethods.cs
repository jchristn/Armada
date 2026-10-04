namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of vessel dependency persistence.
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
        /// <param name="connectionString">MySQL connection string.</param>
        public VesselDependencyMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
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

            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);

                foreach (VesselDependency dependency in dependencies)
                {
                    token.ThrowIfCancellationRequested();
                    await MysqlCommandHelper.ExecuteAsync(conn, tx, @"INSERT INTO vessel_dependencies
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
            return await MysqlCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id ORDER BY ecosystem ASC, package_name ASC, project_path ASC, id ASC;",
                cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                },
                FromReader, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByVesselAsync(string tenantId, string vesselId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(vesselId)) throw new ArgumentNullException(nameof(vesselId));
            await MysqlCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (MySqlConnection conn, MySqlTransaction tx) =>
            {
                await MysqlCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_dependencies WHERE tenant_id = @tenant_id AND vessel_id = @vessel_id;", cmd =>
                {
                    MysqlCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    MysqlCommandHelper.Add(cmd, "@vessel_id", vesselId);
                }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void Bind(MySqlCommand cmd, VesselDependency dependency)
        {
            MysqlCommandHelper.Add(cmd, "@id", dependency.Id);
            MysqlCommandHelper.Add(cmd, "@tenant_id", dependency.TenantId);
            MysqlCommandHelper.Add(cmd, "@vessel_id", dependency.VesselId);
            MysqlCommandHelper.Add(cmd, "@ecosystem", dependency.Ecosystem);
            MysqlCommandHelper.Add(cmd, "@project_path", dependency.ProjectPath);
            MysqlCommandHelper.Add(cmd, "@package_name", dependency.PackageName);
            MysqlCommandHelper.Add(cmd, "@current_version", dependency.CurrentVersion);
            MysqlCommandHelper.Add(cmd, "@latest_version", dependency.LatestVersion);
            MysqlCommandHelper.Add(cmd, "@drift", dependency.Drift.ToString());
            MysqlCommandHelper.Add(cmd, "@is_vulnerable", dependency.IsVulnerable);
            MysqlCommandHelper.Add(cmd, "@severity", dependency.Severity.ToString());
            MysqlCommandHelper.Add(cmd, "@advisory_url", dependency.AdvisoryUrl);
            MysqlCommandHelper.AddDate(cmd, "@created_utc", dependency.CreatedUtc);
            MysqlCommandHelper.AddDate(cmd, "@last_update_utc", dependency.LastUpdateUtc);
        }

        private static VesselDependency FromReader(MySqlDataReader reader)
        {
            VesselDependency dependency = new VesselDependency();
            dependency.Id = reader["id"].ToString()!;
            dependency.TenantId = MysqlCommandHelper.ReadString(reader["tenant_id"]);
            dependency.VesselId = MysqlCommandHelper.ReadString(reader["vessel_id"]) ?? String.Empty;
            dependency.Ecosystem = MysqlCommandHelper.ReadString(reader["ecosystem"]) ?? String.Empty;
            dependency.ProjectPath = MysqlCommandHelper.ReadString(reader["project_path"]);
            dependency.PackageName = MysqlCommandHelper.ReadString(reader["package_name"]) ?? String.Empty;
            dependency.CurrentVersion = MysqlCommandHelper.ReadString(reader["current_version"]);
            dependency.LatestVersion = MysqlCommandHelper.ReadString(reader["latest_version"]);
            dependency.Drift = MysqlCommandHelper.ReadEnum(reader["drift"], DependencyDriftEnum.None);
            dependency.IsVulnerable = MysqlCommandHelper.ReadBool(reader["is_vulnerable"], false);
            dependency.Severity = MysqlCommandHelper.ReadEnum(reader["severity"], VulnerabilitySeverityEnum.None);
            dependency.AdvisoryUrl = MysqlCommandHelper.ReadString(reader["advisory_url"]);
            dependency.CreatedUtc = MysqlCommandHelper.ReadDate(reader["created_utc"]);
            dependency.LastUpdateUtc = MysqlCommandHelper.ReadDate(reader["last_update_utc"]);
            return dependency;
        }

        #endregion
    }
}
