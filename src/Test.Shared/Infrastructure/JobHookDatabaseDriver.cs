namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;

    /// <summary>
    /// Database driver that shares every entity accessor of a real driver except <see cref="DatabaseDriver.Jobs"/>,
    /// which is wrapped in <see cref="HookedJobMethods"/> so a test can interleave a change with a worker's job write.
    /// Does not own the real driver: disposing this instance leaves it open.
    /// </summary>
    public sealed class JobHookDatabaseDriver : DatabaseDriver
    {
        #region Public-Members

        /// <summary>
        /// The hooked job methods (also exposed as <see cref="DatabaseDriver.Jobs"/>).
        /// </summary>
        public HookedJobMethods HookedJobs { get; }

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Inner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate over an initialized driver.
        /// </summary>
        /// <param name="inner">Real driver.</param>
        public JobHookDatabaseDriver(DatabaseDriver inner)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            foreach (PropertyInfo property in typeof(DatabaseDriver).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                MethodInfo? setter = property.GetSetMethod(true);
                if (setter == null || property.GetIndexParameters().Length > 0) continue;
                setter.Invoke(this, new object?[] { property.GetValue(inner) });
            }

            HookedJobs = new HookedJobMethods(inner.Jobs);
            Jobs = HookedJobs;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task InitializeAsync(CancellationToken token = default)
        {
            return _Inner.InitializeAsync(token);
        }

        /// <inheritdoc />
        public override Task<int> GetSchemaVersionAsync(CancellationToken token = default)
        {
            return _Inner.GetSchemaVersionAsync(token);
        }

        /// <inheritdoc />
        public override int GetLatestSchemaVersion()
        {
            return _Inner.GetLatestSchemaVersion();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
        }

        #endregion

        #region Internal-Methods

        /// <inheritdoc />
        internal override IReadOnlyList<SchemaMigration> GetMigrationsForVerification()
        {
            return _Inner.GetMigrationsForVerification();
        }

        /// <inheritdoc />
        internal override Task ReplayMigrationsAsync(CancellationToken token = default)
        {
            return _Inner.ReplayMigrationsAsync(token);
        }

        #endregion
    }
}
