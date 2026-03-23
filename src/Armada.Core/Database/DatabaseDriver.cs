namespace Armada.Core.Database
{
    using Armada.Core.Database.Interfaces;

    /// <summary>
    /// Abstract database driver providing access to all entity methods.
    /// </summary>
    public abstract class DatabaseDriver : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Fleet operations.
        /// </summary>
        public IFleetMethods Fleets { get; protected set; } = null!;

        /// <summary>
        /// Vessel operations.
        /// </summary>
        public IVesselMethods Vessels { get; protected set; } = null!;

        /// <summary>
        /// Captain operations.
        /// </summary>
        public ICaptainMethods Captains { get; protected set; } = null!;

        /// <summary>
        /// Mission operations.
        /// </summary>
        public IMissionMethods Missions { get; protected set; } = null!;

        /// <summary>
        /// Voyage operations.
        /// </summary>
        public IVoyageMethods Voyages { get; protected set; } = null!;

        /// <summary>
        /// Dock operations.
        /// </summary>
        public IDockMethods Docks { get; protected set; } = null!;

        /// <summary>
        /// Signal operations.
        /// </summary>
        public ISignalMethods Signals { get; protected set; } = null!;

        /// <summary>
        /// Event operations.
        /// </summary>
        public IEventMethods Events { get; protected set; } = null!;

        /// <summary>
        /// Merge entry operations.
        /// </summary>
        public IMergeEntryMethods MergeEntries { get; protected set; } = null!;

        /// <summary>
        /// Tenant operations.
        /// </summary>
        public ITenantMethods Tenants { get; protected set; } = null!;

        /// <summary>
        /// User operations.
        /// </summary>
        public IUserMethods Users { get; protected set; } = null!;

        /// <summary>
        /// Credential operations.
        /// </summary>
        public ICredentialMethods Credentials { get; protected set; } = null!;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public DatabaseDriver()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize the database schema and seed data.
        /// </summary>
        public abstract Task InitializeAsync(CancellationToken token = default);

        /// <summary>
        /// Dispose.
        /// </summary>
        public abstract void Dispose();

        #endregion
    }
}
