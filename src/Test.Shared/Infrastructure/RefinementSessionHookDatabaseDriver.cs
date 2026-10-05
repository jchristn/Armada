namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;

    /// <summary>
    /// Database driver that shares every entity accessor of a real driver except
    /// <see cref="DatabaseDriver.ObjectiveRefinementSessions"/>, which is wrapped in
    /// <see cref="HookedObjectiveRefinementSessionMethods"/> so a test can act the moment a session update is durable.
    /// Does not own the real driver: disposing this instance leaves it open.
    /// </summary>
    public sealed class RefinementSessionHookDatabaseDriver : DatabaseDriver
    {
        #region Public-Members

        /// <summary>
        /// The hooked session methods (also exposed as <see cref="DatabaseDriver.ObjectiveRefinementSessions"/>).
        /// </summary>
        public HookedObjectiveRefinementSessionMethods HookedSessions { get; }

        #endregion

        #region Private-Members

        private readonly DatabaseDriver _Inner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate over an initialized driver.
        /// </summary>
        /// <param name="inner">Real driver.</param>
        public RefinementSessionHookDatabaseDriver(DatabaseDriver inner)
        {
            _Inner = inner ?? throw new ArgumentNullException(nameof(inner));
            foreach (PropertyInfo property in typeof(DatabaseDriver).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                MethodInfo? setter = property.GetSetMethod(true);
                if (setter == null || property.GetIndexParameters().Length > 0) continue;
                setter.Invoke(this, new object?[] { property.GetValue(inner) });
            }

            HookedSessions = new HookedObjectiveRefinementSessionMethods(inner.ObjectiveRefinementSessions);
            ObjectiveRefinementSessions = HookedSessions;
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
