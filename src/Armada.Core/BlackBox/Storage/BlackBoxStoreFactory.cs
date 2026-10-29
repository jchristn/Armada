namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Concurrent;
    using System.IO;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Settings;

    /// <summary>
    /// Creates the <see cref="IBlackBoxStore"/> for a storage target. Database is built in; Disk, S3, AzureBlob,
    /// GoogleCloud, Cifs and Nfs are <see cref="BlobjectBlackBoxStore"/> instances over their Blobject clients (one case
    /// each in <see cref="CreateBuiltIn"/>); other stores are registered at runtime with <see cref="Register"/>. A target
    /// whose settings are invalid throws <see cref="BlackBoxStoreUnavailableException"/> naming every problem (the writer
    /// treats it as a store failure and applies the database fallback). Stores are cached per target until
    /// <see cref="Reset"/>. Thread-safe.
    /// </summary>
    public class BlackBoxStoreFactory
    {
        #region Private-Members

        private readonly DatabaseDriver _Database;
        private readonly BlackBoxSettings _Settings;
        private readonly ConcurrentDictionary<BlackBoxStorageTargetEnum, Func<BlackBoxStorageSettings, IBlackBoxStore>> _Registered = new ConcurrentDictionary<BlackBoxStorageTargetEnum, Func<BlackBoxStorageSettings, IBlackBoxStore>>();
        private readonly ConcurrentDictionary<BlackBoxStorageTargetEnum, IBlackBoxStore> _Cache = new ConcurrentDictionary<BlackBoxStorageTargetEnum, IBlackBoxStore>();
        private readonly string? _DataDirectory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Black box settings (read live, so storage changes apply to new stores).</param>
        /// <param name="dataDirectory">Armada data directory; the Disk target defaults to {dataDirectory}/blackbox.</param>
        /// <exception cref="ArgumentNullException">Thrown when database or settings is null.</exception>
        public BlackBoxStoreFactory(DatabaseDriver database, BlackBoxSettings settings, string? dataDirectory = null)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _DataDirectory = String.IsNullOrWhiteSpace(dataDirectory) ? null : dataDirectory;
        }

        /// <summary>
        /// The Disk target's root directory under the current settings, or null when it cannot be resolved.
        /// </summary>
        public string? DiskDirectory
        {
            get
            {
                try { return BlackBoxBlobClientFactory.ResolveDiskDirectory(_Settings.Storage, _DataDirectory); }
                catch (ArgumentException) { return null; }
            }
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
        /// <exception cref="NotSupportedException">Thrown when the configured target is unknown.</exception>
        /// <exception cref="BlackBoxStoreUnavailableException">Thrown when the configured target's settings are invalid.</exception>
        public IBlackBoxStore GetConfigured()
        {
            return Get(_Settings.Storage.Target);
        }

        /// <summary>
        /// The store for a target (existing entries are read from the target they were written to).
        /// </summary>
        /// <param name="target">Target.</param>
        /// <returns>The store.</returns>
        /// <exception cref="NotSupportedException">Thrown when the target is unknown.</exception>
        /// <exception cref="BlackBoxStoreUnavailableException">Thrown when the target's settings are invalid.</exception>
        public IBlackBoxStore Get(BlackBoxStorageTargetEnum target)
        {
            return _Cache.GetOrAdd(target, t =>
            {
                if (_Registered.TryGetValue(t, out Func<BlackBoxStorageSettings, IBlackBoxStore>? builder)) return builder(_Settings.Storage);
                return CreateBuiltIn(t);
            });
        }

        /// <summary>
        /// Drop (and dispose) cached stores, after a storage settings change.
        /// </summary>
        public void Reset()
        {
            foreach (BlackBoxStorageTargetEnum target in _Cache.Keys)
            {
                if (_Cache.TryRemove(target, out IBlackBoxStore? store) && store is IDisposable disposable && !_Registered.ContainsKey(target))
                {
                    try { disposable.Dispose(); }
                    catch (ObjectDisposedException) { }
                }
            }
        }

        #endregion

        #region Private-Methods

        private IBlackBoxStore CreateBuiltIn(BlackBoxStorageTargetEnum target)
        {
            switch (target)
            {
                case BlackBoxStorageTargetEnum.Database:
                    return new DatabaseBlackBoxStore(_Database);
                case BlackBoxStorageTargetEnum.Disk:
                    return Blobject(target);
                case BlackBoxStorageTargetEnum.S3:
                    return Blobject(target);
                case BlackBoxStorageTargetEnum.AzureBlob:
                    return Blobject(target);
                case BlackBoxStorageTargetEnum.GoogleCloud:
                    return Blobject(target);
                case BlackBoxStorageTargetEnum.Cifs:
                    return Blobject(target);
                case BlackBoxStorageTargetEnum.Nfs:
                    return Blobject(target);
                default:
                    throw new NotSupportedException("Unknown black box storage target " + target + ".");
            }
        }

        private IBlackBoxStore Blobject(BlackBoxStorageTargetEnum target)
        {
            try
            {
                string? diskRoot = target == BlackBoxStorageTargetEnum.Disk ? BlackBoxBlobClientFactory.ResolveDiskDirectory(_Settings.Storage, _DataDirectory) : null;
                return new BlobjectBlackBoxStore(target, BlackBoxBlobClientFactory.Create(target, _Settings.Storage, _DataDirectory), diskRoot);
            }
            catch (ArgumentException e)
            {
                throw new BlackBoxStoreUnavailableException("The black box " + target + " storage target is misconfigured: " + e.Message, e);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new BlackBoxStoreUnavailableException("The black box " + target + " storage target could not be opened: " + e.Message, e);
            }
        }

        #endregion
    }
}
