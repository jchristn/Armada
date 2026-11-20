namespace Armada.Core.Database.Mysql.Implementations
{
    using System;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using MySqlConnector;
    using Armada.Core.Database.Interfaces;
    using Armada.Core.Models;

    /// <summary>
    /// MySQL implementation of durable coordination lease operations. Provides restart-safe,
    /// multi-instance-safe mutual exclusion via an atomic compare-and-swap on the lease name,
    /// with TTL-based takeover so a crashed holder never blocks progress permanently.
    /// </summary>
    public class CoordinationLeaseMethods : ICoordinationLeaseMethods
    {
        #region Private-Members

        private string _ConnectionString;
        private static readonly string _Iso8601Format = "yyyy-MM-dd HH:mm:ss.ffffff";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with a MySQL connection string.
        /// </summary>
        /// <param name="connectionString">MySQL connection string.</param>
        public CoordinationLeaseMethods(string connectionString)
        {
            _ConnectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> TryAcquireAsync(string name, string holder, TimeSpan ttl, string? tenantId = null, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(holder)) throw new ArgumentNullException(nameof(holder));

            DateTime now = DateTime.UtcNow;
            DateTime expires = now.Add(ttl);

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"INSERT INTO coordination_leases (name, holder, tenant_id, acquired_utc, expires_utc)
                        VALUES (@name, @holder, @tenant, @now, @exp)
                        ON DUPLICATE KEY UPDATE
                            holder = IF(expires_utc <= @now, VALUES(holder), holder),
                            tenant_id = IF(expires_utc <= @now, VALUES(tenant_id), tenant_id),
                            acquired_utc = IF(expires_utc <= @now, VALUES(acquired_utc), acquired_utc),
                            expires_utc = IF(expires_utc <= @now, VALUES(expires_utc), expires_utc);";
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@holder", holder);
                    cmd.Parameters.AddWithValue("@tenant", (object?)tenantId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@now", ToIso8601(now));
                    cmd.Parameters.AddWithValue("@exp", ToIso8601(expires));
                    await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT holder FROM coordination_leases WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    string? currentHolder = NullableString(result!);
                    return string.Equals(currentHolder, holder, StringComparison.Ordinal);
                }
            }
        }

        /// <inheritdoc />
        public async Task<bool> TryRenewAsync(string name, string holder, TimeSpan ttl, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(holder)) throw new ArgumentNullException(nameof(holder));

            DateTime now = DateTime.UtcNow;
            DateTime newExpires = now.Add(ttl);

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"UPDATE coordination_leases
                        SET expires_utc = @newExp
                        WHERE name = @name AND holder = @holder AND expires_utc > @now;";
                    cmd.Parameters.AddWithValue("@newExp", ToIso8601(newExpires));
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@holder", holder);
                    cmd.Parameters.AddWithValue("@now", ToIso8601(now));
                    int rowsAffected = await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    return rowsAffected > 0;
                }
            }
        }

        /// <inheritdoc />
        public async Task ReleaseAsync(string name, string holder, CancellationToken token = default)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (string.IsNullOrEmpty(holder)) throw new ArgumentNullException(nameof(holder));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
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
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT * FROM coordination_leases WHERE name = @name;";
                    cmd.Parameters.AddWithValue("@name", name);
                    using (MySqlDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        if (await reader.ReadAsync(token).ConfigureAwait(false))
                            return CoordinationLeaseFromReader(reader);
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public async Task<int> PurgeExpiredAsync(CancellationToken token = default)
        {
            DateTime now = DateTime.UtcNow;

            using (MySqlConnection conn = new MySqlConnection(_ConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "DELETE FROM coordination_leases WHERE expires_utc <= @now;";
                    cmd.Parameters.AddWithValue("@now", ToIso8601(now));
                    return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static string ToIso8601(DateTime dt)
        {
            return UtcClock.Format(dt, _Iso8601Format);
        }

        private static DateTime FromIso8601(string value)
        {
            return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }

        private static string? NullableString(object value)
        {
            if (value == null || value == DBNull.Value) return null;
            string str = value.ToString()!;
            return string.IsNullOrEmpty(str) ? null : str;
        }

        private static CoordinationLease CoordinationLeaseFromReader(MySqlDataReader reader)
        {
            CoordinationLease lease = new CoordinationLease();
            lease.Name = reader["name"].ToString()!;
            lease.Holder = reader["holder"].ToString()!;
            lease.TenantId = NullableString(reader["tenant_id"]);
            lease.AcquiredUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["acquired_utc"]), DateTimeKind.Utc);
            lease.ExpiresUtc = DateTime.SpecifyKind(Convert.ToDateTime(reader["expires_utc"]), DateTimeKind.Utc);
            return lease;
        }

        #endregion
    }
}
