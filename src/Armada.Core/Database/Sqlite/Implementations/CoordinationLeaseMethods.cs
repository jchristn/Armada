namespace Armada.Core.Database.Sqlite.Implementations
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Sqlite;
    using SyslogLogging;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// SQLite implementation of durable coordination lease operations. Acquisition is an atomic
    /// compare-and-swap keyed by lease name using an upsert guarded on expiry, so a lease held by a
    /// live holder is never stolen while an expired lease may be taken over. Timestamps are stored
    /// as fixed-width ISO 8601 UTC text, which is lexicographically ordered and therefore safe to
    /// compare directly in SQL.
    /// </summary>
    public class CoordinationLeaseMethods : ICoordinationLeaseMethods
    {
        #region Private-Members

#pragma warning disable CS0414
        private readonly string _Header = "[CoordinationLeaseMethods] ";
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
        public CoordinationLeaseMethods(SqliteDatabaseDriver driver, DatabaseSettings settings, LoggingModule logging)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> TryAcquireAsync(string name, string holder, TimeSpan ttl, string? tenantId = null, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(holder)) throw new ArgumentNullException(nameof(holder));

            DateTime now = DateTime.UtcNow;
            DateTime expires = now.Add(ttl);
            string nowIso = SqliteDatabaseDriver.ToIso8601(now);
            string expiresIso = SqliteDatabaseDriver.ToIso8601(expires);

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO coordination_leases (name, holder, tenant_id, acquired_utc, expires_utc)
                            VALUES (@name, @holder, @tenant_id, @acquired_utc, @expires_utc)
                            ON CONFLICT(name) DO UPDATE SET
                                holder = excluded.holder,
                                tenant_id = excluded.tenant_id,
                                acquired_utc = excluded.acquired_utc,
                                expires_utc = excluded.expires_utc
                            WHERE coordination_leases.expires_utc <= @now;";
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@holder", holder);
                    cmd.Parameters.AddWithValue("@tenant_id", (object?)tenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@acquired_utc", nowIso);
                    cmd.Parameters.AddWithValue("@expires_utc", expiresIso);
                    cmd.Parameters.AddWithValue("@now", nowIso);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT holder FROM coordination_leases WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result == null || result == DBNull.Value) return false;
                    return String.Equals(result.ToString(), holder, StringComparison.Ordinal);
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> TryRenewAsync(string name, string holder, TimeSpan ttl, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(holder)) throw new ArgumentNullException(nameof(holder));

            DateTime now = DateTime.UtcNow;
            string nowIso = SqliteDatabaseDriver.ToIso8601(now);
            string newExpiresIso = SqliteDatabaseDriver.ToIso8601(now.Add(ttl));

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE coordination_leases SET expires_utc = @new_expires
                            WHERE name = @name AND holder = @holder AND expires_utc > @now;";
                    cmd.Parameters.AddWithValue("@new_expires", newExpiresIso);
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@holder", holder);
                    cmd.Parameters.AddWithValue("@now", nowIso);
                    int rowsAffected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rowsAffected > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task ReleaseAsync(string name, string holder, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (String.IsNullOrEmpty(holder)) throw new ArgumentNullException(nameof(holder));

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM coordination_leases WHERE name = @name AND holder = @holder;";
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@holder", holder);
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        /// <inheritdoc />
        public async Task<CoordinationLease?> ReadAsync(string name, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM coordination_leases WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (SqliteDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return LeaseFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<int> PurgeExpiredAsync(CancellationToken token = default)
        {
            string nowIso = SqliteDatabaseDriver.ToIso8601(DateTime.UtcNow);

            using (SqliteWriteLease writeLease = await _Driver.WriteGate.EnterAsync(token).ConfigureAwait(false))
            using (SqliteConnection conn = new SqliteProviderConnection(_Driver.ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqliteCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM coordination_leases WHERE expires_utc <= @now;";
                    cmd.Parameters.AddWithValue("@now", nowIso);
                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Convert a SqliteDataReader row to a CoordinationLease model.
        /// </summary>
        /// <param name="reader">Data reader positioned on a row.</param>
        /// <returns>CoordinationLease instance.</returns>
        private static CoordinationLease LeaseFromReader(SqliteDataReader reader)
        {
            CoordinationLease lease = new CoordinationLease();
            lease.Name = reader["name"].ToString()!;
            lease.Holder = reader["holder"].ToString()!;
            lease.TenantId = SqliteDatabaseDriver.NullableString(reader["tenant_id"]);
            lease.AcquiredUtc = SqliteDatabaseDriver.FromIso8601(reader["acquired_utc"].ToString()!);
            lease.ExpiresUtc = SqliteDatabaseDriver.FromIso8601(reader["expires_utc"].ToString()!);
            return lease;
        }

        #endregion
    }
}
