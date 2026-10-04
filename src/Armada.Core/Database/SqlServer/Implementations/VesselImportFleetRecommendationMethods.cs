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
    /// SQL Server implementation of vessel import fleet recommendation persistence.
    /// </summary>
    public class VesselImportFleetRecommendationMethods : IVesselImportFleetRecommendationMethods
    {
        #region Private-Members

        private static readonly string _InsertRecommendation = @"INSERT INTO vessel_import_fleet_recommendations
            (id, tenant_id, batch_id, name, description, rationale, sort_order, applied_fleet_id, created_utc, last_update_utc)
            VALUES
            (@id, @tenant_id, @batch_id, @name, @description, @rationale, @sort_order, @applied_fleet_id, @created_utc, @last_update_utc);";

        private static readonly string _InsertVessel = @"INSERT INTO vessel_import_fleet_recommendation_vessels
            (recommendation_id, tenant_id, batch_id, vessel_id, sort_order)
            VALUES
            (@recommendation_id, @tenant_id, @batch_id, @vessel_id, @sort_order);";

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
        public VesselImportFleetRecommendationMethods(SqlServerDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
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
        public async Task<List<VesselImportFleetRecommendation>> ReplaceForBatchAsync(string tenantId, string batchId, List<VesselImportFleetRecommendation> recommendations, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(batchId)) throw new ArgumentNullException(nameof(batchId));
            if (recommendations == null) throw new ArgumentNullException(nameof(recommendations));

            DateTime now = DateTime.UtcNow;
            foreach (VesselImportFleetRecommendation recommendation in recommendations)
            {
                if (recommendation == null) throw new ArgumentException("Recommendations must not contain null entries.", nameof(recommendations));
                if (String.IsNullOrEmpty(recommendation.Id)) recommendation.Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselImportFleetRecommendationIdPrefix, 24);
                recommendation.TenantId = tenantId;
                recommendation.BatchId = batchId;
                recommendation.LastUpdateUtc = now;
            }

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                Action<SqlCommand> bindBatch = cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@batch_id", batchId);
                };
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendation_vessels WHERE tenant_id = @tenant_id AND batch_id = @batch_id;", bindBatch, token).ConfigureAwait(false);
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendations WHERE tenant_id = @tenant_id AND batch_id = @batch_id;", bindBatch, token).ConfigureAwait(false);

                foreach (VesselImportFleetRecommendation recommendation in recommendations)
                {
                    token.ThrowIfCancellationRequested();
                    await SqlServerCommandHelper.ExecuteAsync(conn, tx, _InsertRecommendation, cmd => BindRecommendation(cmd, recommendation), token).ConfigureAwait(false);

                    int order = 0;
                    foreach (string vesselId in recommendation.VesselIds.Where(v => !String.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal))
                    {
                        int sortOrder = order++;
                        await SqlServerCommandHelper.ExecuteAsync(conn, tx, _InsertVessel, cmd =>
                        {
                            SqlServerCommandHelper.Add(cmd, "@recommendation_id", recommendation.Id);
                            SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                            SqlServerCommandHelper.Add(cmd, "@batch_id", batchId);
                            SqlServerCommandHelper.Add(cmd, "@vessel_id", vesselId);
                            SqlServerCommandHelper.Add(cmd, "@sort_order", sortOrder);
                        }, token).ConfigureAwait(false);
                    }
                }
            }, token).ConfigureAwait(false);

            return recommendations;
        }

        /// <inheritdoc />
        public async Task<List<VesselImportFleetRecommendation>> EnumerateByBatchAsync(string tenantId, string batchId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(batchId)) throw new ArgumentNullException(nameof(batchId));

            Action<SqlCommand> bind = cmd =>
            {
                SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                SqlServerCommandHelper.Add(cmd, "@batch_id", batchId);
            };

            List<VesselImportFleetRecommendation> recommendations = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT * FROM vessel_import_fleet_recommendations WHERE tenant_id = @tenant_id AND batch_id = @batch_id ORDER BY sort_order ASC, id ASC;",
                bind, FromReader, token).ConfigureAwait(false);
            if (recommendations.Count == 0) return recommendations;

            List<VesselImportFleetRecommendationLink> links = await SqlServerCommandHelper.QueryAsync(_ConnectionString,
                "SELECT recommendation_id, vessel_id FROM vessel_import_fleet_recommendation_vessels WHERE tenant_id = @tenant_id AND batch_id = @batch_id ORDER BY recommendation_id ASC, sort_order ASC;",
                bind, LinkFromReader, token).ConfigureAwait(false);

            Dictionary<string, VesselImportFleetRecommendation> byId = recommendations.ToDictionary(r => r.Id, r => r, StringComparer.Ordinal);
            foreach (VesselImportFleetRecommendationLink link in links)
            {
                if (byId.TryGetValue(link.RecommendationId, out VesselImportFleetRecommendation? owner)) owner.VesselIds.Add(link.VesselId);
            }

            return recommendations;
        }

        /// <inheritdoc />
        public async Task UpdateAppliedFleetAsync(string tenantId, string id, string? fleetId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                await SqlServerCommandHelper.ExecuteAsync(conn, tx,
                    "UPDATE vessel_import_fleet_recommendations SET applied_fleet_id = @applied_fleet_id, last_update_utc = @last_update_utc WHERE tenant_id = @tenant_id AND id = @id;",
                    cmd =>
                    {
                        SqlServerCommandHelper.Add(cmd, "@applied_fleet_id", fleetId);
                        SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", DateTime.UtcNow);
                        SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                        SqlServerCommandHelper.Add(cmd, "@id", id);
                    }, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteByBatchAsync(string tenantId, string batchId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(tenantId)) throw new ArgumentNullException(nameof(tenantId));
            if (String.IsNullOrEmpty(batchId)) throw new ArgumentNullException(nameof(batchId));

            await SqlServerCommandHelper.WriteAsync(_ConnectionString, _WriteLock, async (SqlConnection conn, SqlTransaction tx) =>
            {
                Action<SqlCommand> bind = cmd =>
                {
                    SqlServerCommandHelper.Add(cmd, "@tenant_id", tenantId);
                    SqlServerCommandHelper.Add(cmd, "@batch_id", batchId);
                };
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendation_vessels WHERE tenant_id = @tenant_id AND batch_id = @batch_id;", bind, token).ConfigureAwait(false);
                await SqlServerCommandHelper.ExecuteAsync(conn, tx, "DELETE FROM vessel_import_fleet_recommendations WHERE tenant_id = @tenant_id AND batch_id = @batch_id;", bind, token).ConfigureAwait(false);
            }, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static void BindRecommendation(SqlCommand cmd, VesselImportFleetRecommendation recommendation)
        {
            SqlServerCommandHelper.Add(cmd, "@id", recommendation.Id);
            SqlServerCommandHelper.Add(cmd, "@tenant_id", recommendation.TenantId);
            SqlServerCommandHelper.Add(cmd, "@batch_id", recommendation.BatchId);
            SqlServerCommandHelper.Add(cmd, "@name", recommendation.Name);
            SqlServerCommandHelper.Add(cmd, "@description", recommendation.Description);
            SqlServerCommandHelper.Add(cmd, "@rationale", recommendation.Rationale);
            SqlServerCommandHelper.Add(cmd, "@sort_order", recommendation.SortOrder);
            SqlServerCommandHelper.Add(cmd, "@applied_fleet_id", recommendation.AppliedFleetId);
            SqlServerCommandHelper.AddDate(cmd, "@created_utc", recommendation.CreatedUtc);
            SqlServerCommandHelper.AddDate(cmd, "@last_update_utc", recommendation.LastUpdateUtc);
        }

        private static VesselImportFleetRecommendation FromReader(SqlDataReader reader)
        {
            VesselImportFleetRecommendation recommendation = new VesselImportFleetRecommendation();
            recommendation.Id = reader["id"].ToString()!;
            recommendation.TenantId = SqlServerCommandHelper.ReadString(reader["tenant_id"]);
            recommendation.BatchId = reader["batch_id"].ToString()!;
            recommendation.Name = reader["name"].ToString()!;
            recommendation.Description = SqlServerCommandHelper.ReadString(reader["description"]);
            recommendation.Rationale = SqlServerCommandHelper.ReadString(reader["rationale"]);
            recommendation.SortOrder = SqlServerCommandHelper.ReadInt(reader["sort_order"], 0);
            recommendation.AppliedFleetId = SqlServerCommandHelper.ReadString(reader["applied_fleet_id"]);
            recommendation.CreatedUtc = SqlServerCommandHelper.ReadDate(reader["created_utc"]);
            recommendation.LastUpdateUtc = SqlServerCommandHelper.ReadDate(reader["last_update_utc"]);
            return recommendation;
        }

        private static VesselImportFleetRecommendationLink LinkFromReader(SqlDataReader reader)
        {
            VesselImportFleetRecommendationLink link = new VesselImportFleetRecommendationLink();
            link.RecommendationId = reader["recommendation_id"].ToString()!;
            link.VesselId = reader["vessel_id"].ToString()!;
            return link;
        }

        #endregion
    }
}
