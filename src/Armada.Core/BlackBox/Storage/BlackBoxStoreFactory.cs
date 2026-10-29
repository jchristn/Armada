namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Concurrent;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// Creates the <see cref="IBlackBoxStore"/> for a storage target. Database is built in. Other targets are added with
    /// one case in <see cref="CreateBuiltIn"/> (TODO(blackbox-wave2: storage) Blobject-backed Disk, S3, AzureBlob,
    /// GoogleCloud, Cifs and Nfs), or registered at runtime with <see cref="Register"/>. Stores are cached per target.
    /// Thread-safe.
    /// </summary>
    public class BlackBoxStoreFactory
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly BlackBoxSettings _Settings;
        private readonly ConcurrentDictionary<BlackBoxStorageTargetEnum, Func<BlackBoxStorageSettings, IBlackBoxStore>> _Registered = new ConcurrentDictionary<BlackBoxStorageTargetEnum, Func<BlackBoxStorageSettings, IBlackBoxStore>>();
        private readonly ConcurrentDictionary<BlackBoxStorageTargetEnum, IBlackBoxStore> _Cache = new ConcurrentDictionary<BlackBoxStorageTargetEnum, IBlackBoxStore>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Black box settings (read live, so storage changes apply to new stores).</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxStoreFactory(DatabaseDriver database, BlackBoxSettings settings)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register (or replace) the store builder for a target, for example a test double or an external target.
        /// </summary>
        /// <param name="target">Target.</param>
        /// <param name="builder">Builder.</param>
        /// <exception cref="ArgumentNullException">Thrown when builder is null.</exception>
        public void Register(BlackBoxStorageTargetEnum target, Func<BlackBoxStorageSettings, IBlackBoxStore> builder)
        {
            if (builder == null) throw new ArgumentNullException(nameof(builder));
            _Registered[target] = builder;
            _Cache.TryRemove(target, out _);
        }

        /// <summary>
        /// The store for the configured target (new entries).
        /// </summary>
        /// <returns>The store.</returns>
        /// <exception cref="NotSupportedException">Thrown when the configured target is not implemented.</exception>
        public IBlackBoxStore GetConfigured()
        {
            return Get(_Settings.Storage.Target);
        }

        /// <summary>
        /// The store for a target (existing entries are read from the target they were written to).
        /// </summary>
        /// <param name="target">Target.</param>
        /// <returns>The store.</returns>
        /// <exception cref="NotSupportedException">Thrown when the target is not implemented.</exception>
        public IBlackBoxStore Get(BlackBoxStorageTargetEnum target)
        {
            return _Cache.GetOrAdd(target, t =>
            {
                if (_Registered.TryGetValue(t, out Func<BlackBoxStorageSettings, IBlackBoxStore>? builder)) return builder(_Settings.Storage);
                return CreateBuiltIn(t);
            });
        }

        /// <summary>
        /// Drop cached stores (after a storage settings change).
        /// </summary>
        public void Reset()
        {
            _Cache.Clear();
        }

        #endregion

        #region Private-Methods

        private IBlackBoxStore CreateBuiltIn(BlackBoxStorageTargetEnum target)
        {
            switch (target)
            {
                case BlackBoxStorageTargetEnum.Database:
                    return new DatabaseBlackBoxStore(_Database);
                // TODO(blackbox-wave2: storage): add one case per Blobject target here, for example
                // case BlackBoxStorageTargetEnum.S3: return new BlobjectBlackBoxStore(target, _Settings.Storage);
                case BlackBoxStorageTargetEnum.Disk:
                case BlackBoxStorageTargetEnum.S3:
                case BlackBoxStorageTargetEnum.AzureBlob:
                case BlackBoxStorageTargetEnum.GoogleCloud:
                case BlackBoxStorageTargetEnum.Cifs:
                case BlackBoxStorageTargetEnum.Nfs:
                    throw new NotSupportedException("The black box " + target + " storage target is not yet implemented; use Database.");
                default:
                    throw new NotSupportedException("Unknown black box storage target " + target + ".");
            }
        }

        #endregion
    }
}
