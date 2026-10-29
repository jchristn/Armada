namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Moves black box objects between storage targets (plan section 4.6): a background job that pages through the
    /// entries not yet on the target in id order, copies each object, verifies its SHA-256 (on the source read and on a
    /// read back from the target), switches the entry, then deletes the source copy. Progress and the resume cursor are
    /// saved in blackbox_migrations after every entry, so a job interrupted by a restart resumes where it stopped
    /// (<see cref="ResumePendingAsync"/>) and running it again is harmless: entries already on the target are skipped.
    /// Every step leaves each object readable (an object with no storage key reads from the database; external objects
    /// keep their key, so an external-to-external move only flips the entry's target). One job runs at a time.
    /// Thread-safe.
    /// </summary>
    public class BlackBoxStorageMigrator
    {
        #region Public-Members

        /// <summary>
        /// Called after each entry is processed (entry id); tests use it to interrupt a job. Null by default.
        /// </summary>
        public Func<string, CancellationToken, Task>? OnEntryProcessedAsync { get; set; } = null;

        /// <summary>
        /// The id of the job running now, or null.
        /// </summary>
        public string? ActiveJobId
        {
            get { lock (_Lock) return _ActiveJobId; }
        }

        #endregion

        #region Private-Members

        private readonly string _Header = "[BlackBoxStorageMigrator] ";
        private readonly DatabaseDriver _Database;
        private readonly BlackBoxSettings _Settings;
        private readonly BlackBoxStoreFactory _Stores;
        private readonly LoggingModule _Logging;
        private readonly object _Lock = new object();
        private readonly SemaphoreSlim _RunGate = new SemaphoreSlim(1, 1);
        private string? _ActiveJobId = null;
        private Task _Active = Task.CompletedTask;
        private CancellationTokenSource _Stop = new CancellationTokenSource();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="settings">Black box settings.</param>
        /// <param name="stores">Store factory.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public BlackBoxStorageMigrator(DatabaseDriver database, BlackBoxSettings settings, BlackBoxStoreFactory stores, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Stores = stores ?? throw new ArgumentNullException(nameof(stores));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a job and (unless runInBackground is false) start it. Starting the same move while it runs returns the
        /// running job.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="runInBackground">Run the job on a background task (false: the caller runs <see cref="RunAsync"/>).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the target is Unknown, equals From, or is misconfigured.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a different migration is running.</exception>
        /// <exception cref="BlackBoxStoreUnavailableException">Thrown when the target store fails its health check.</exception>
        public async Task<BlackBoxStorageMigrationJob> StartAsync(BlackBoxStorageMigrationRequest request, bool runInBackground = true, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (request.To == BlackBoxStorageTargetEnum.Unknown) throw new ArgumentException("Choose a storage target to migrate to (Database, Disk, S3, AzureBlob, GoogleCloud, Cifs or Nfs).", nameof(request));
            if (request.From.HasValue && request.From.Value == request.To) throw new ArgumentException("From and To are the same target (" + request.To + ").", nameof(request));
            if (request.From.HasValue && request.From.Value == BlackBoxStorageTargetEnum.Unknown) throw new ArgumentException("From is not a known storage target.", nameof(request));
            List<string> errors = BlackBoxStorageSettingsValidator.Validate(_Settings.Storage, request.To);
            if (errors.Count > 0) throw new ArgumentException("The " + request.To + " target is misconfigured: " + String.Join(" ", errors), nameof(request));

            EnumerationResult<BlackBoxStorageMigrationJob> active = await _Database.BlackBoxMigrations.EnumerateActiveAsync(new EnumerationQuery { PageSize = 10 }, token).ConfigureAwait(false);
            foreach (BlackBoxStorageMigrationJob running in active.Objects)
            {
                if (running.To == request.To && running.From == request.From)
                {
                    if (runInBackground) EnsureRunning(running.Id);
                    return running;
                }
                throw new InvalidOperationException("Black box storage migration " + running.Id + " (" + (running.From?.ToString() ?? "any") + " to " + running.To + ") is still " + running.State + "; wait for it to finish.");
            }

            IBlackBoxStore target = _Stores.Get(request.To);
            BlackBoxStoreHealth health = await target.CheckHealthAsync(token).ConfigureAwait(false);
            if (!health.Healthy) throw new BlackBoxStoreUnavailableException("The " + request.To + " store is not healthy: " + health.Message);

            BlackBoxStorageMigrationJob job = new BlackBoxStorageMigrationJob
            {
                State = BlackBoxJobStateEnum.Pending,
                From = request.From,
                To = request.To,
                DeleteSource = request.DeleteSource,
                BatchSize = request.BatchSize
            };
            job.TotalEntries = (await _Database.BlackBoxEntries.ScanAsync(ScanFor(job, null, 1), token).ConfigureAwait(false)).TotalRecords;
            if (job.TotalEntries == 0)
            {
                // Nothing to move: the job is born finished, so it never blocks the next migration.
                job.State = BlackBoxJobStateEnum.Completed;
                job.StartedUtc = DateTime.UtcNow;
                job.FinishedUtc = job.StartedUtc;
                runInBackground = false;
            }
            await _Database.BlackBoxMigrations.CreateAsync(job, token).ConfigureAwait(false);
            _Logging.Info(_Header + "migration " + job.Id + " created: " + job.TotalEntries + " entries " + (job.From?.ToString() ?? "from any target") + " to " + job.To);
            if (runInBackground) EnsureRunning(job.Id);
            return job;
        }

        /// <summary>
        /// Read a job.
        /// </summary>
        /// <param name="id">Job id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        public Task<BlackBoxStorageMigrationJob?> GetAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            return _Database.BlackBoxMigrations.ReadAsync(id, token);
        }

        /// <summary>
        /// Resume every job left Pending or Running (after a restart), one after another, in the background.
        /// </summary>
        /// <returns>The task running them.</returns>
        public Task ResumePendingAsync()
        {
            return Task.Run(async () =>
            {
                try
                {
                    EnumerationResult<BlackBoxStorageMigrationJob> active = await _Database.BlackBoxMigrations.EnumerateActiveAsync(new EnumerationQuery { PageSize = 10 }, _Stop.Token).ConfigureAwait(false);
                    foreach (BlackBoxStorageMigrationJob job in active.Objects)
                    {
                        _Logging.Info(_Header + "resuming migration " + job.Id + " after " + (job.LastEntryId ?? "the start"));
                        await RunAsync(job.Id, _Stop.Token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (_Stop.IsCancellationRequested)
                {
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "resuming migrations failed: " + e.Message);
                }
            });
        }

        /// <summary>
        /// Run (or continue) a job to completion on the calling task. Cancellation leaves it Running with its cursor saved.
        /// </summary>
        /// <param name="jobId">Job id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job as it finished (or as it stood when cancelled).</returns>
        /// <exception cref="ArgumentNullException">Thrown when jobId is null or empty.</exception>
        /// <exception cref="BlackBoxNotFoundException">Thrown when the job does not exist.</exception>
        /// <exception cref="OperationCanceledException">Thrown when cancelled (the job stays resumable).</exception>
        public async Task<BlackBoxStorageMigrationJob> RunAsync(string jobId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(jobId)) throw new ArgumentNullException(nameof(jobId));
            await _RunGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                BlackBoxStorageMigrationJob job = await _Database.BlackBoxMigrations.ReadAsync(jobId, token).ConfigureAwait(false)
                    ?? throw new BlackBoxNotFoundException("Black box storage migration " + jobId + " not found.");
                if (job.State != BlackBoxJobStateEnum.Pending && job.State != BlackBoxJobStateEnum.Running) return job;
                lock (_Lock) _ActiveJobId = job.Id;
                job.State = BlackBoxJobStateEnum.Running;
                if (!job.StartedUtc.HasValue) job.StartedUtc = DateTime.UtcNow;
                await _Database.BlackBoxMigrations.UpdateAsync(job, token).ConfigureAwait(false);

                for (int round = 0; round < 1000000; round++)
                {
                    token.ThrowIfCancellationRequested();
                    EnumerationResult<BlackBoxEntry> page = await _Database.BlackBoxEntries.ScanAsync(ScanFor(job, job.LastEntryId, job.BatchSize), token).ConfigureAwait(false);
                    if (page.Objects.Count == 0) break;
                    foreach (BlackBoxEntry entry in page.Objects)
                    {
                        token.ThrowIfCancellationRequested();
                        try
                        {
                            long copied = await MigrateEntryAsync(entry, job, token).ConfigureAwait(false);
                            job.MigratedEntries++;
                            job.BytesCopied += copied;
                        }
                        catch (Exception e) when (e is not OperationCanceledException)
                        {
                            job.FailedEntries++;
                            job.LastError = BlackBoxLimits.Clip(entry.Id + ": " + e.Message, BlackBoxLimits.MaxShortTextChars);
                            _Logging.Warn(_Header + "migration " + job.Id + " could not move " + entry.Id + ": " + e.Message);
                        }
                        job.LastEntryId = entry.Id;
                        await _Database.BlackBoxMigrations.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
                        if (OnEntryProcessedAsync != null) await OnEntryProcessedAsync(entry.Id, token).ConfigureAwait(false);
                    }
                }

                job.State = job.FailedEntries > 0 ? BlackBoxJobStateEnum.Failed : BlackBoxJobStateEnum.Completed;
                job.FinishedUtc = DateTime.UtcNow;
                await _Database.BlackBoxMigrations.UpdateAsync(job, CancellationToken.None).ConfigureAwait(false);
                _Logging.Info(_Header + "migration " + job.Id + " " + job.State + ": " + job.MigratedEntries + " moved, " + job.FailedEntries + " failed, " + job.BytesCopied + " bytes");
                return job;
            }
            finally
            {
                lock (_Lock) _ActiveJobId = null;
                _RunGate.Release();
            }
        }

        /// <summary>
        /// Stop background work (jobs stay resumable).
        /// </summary>
        /// <returns>Task.</returns>
        public async Task StopAsync()
        {
            Task active;
            lock (_Lock)
            {
                _Stop.Cancel();
                active = _Active;
            }
            try { await active.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            lock (_Lock)
            {
                _Stop.Dispose();
                _Stop = new CancellationTokenSource();
            }
        }

        /// <summary>
        /// Move one entry to the target. Returns the bytes copied (0 when it was already there).
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="job">Job (target and DeleteSource).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Bytes copied.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        /// <exception cref="BlackBoxObjectCorruptException">Thrown when a source or copied object fails its SHA-256 check (the entry is not switched).</exception>
        /// <exception cref="BlackBoxStoreUnavailableException">Thrown when a store fails (the entry is not switched).</exception>
        public async Task<long> MigrateEntryAsync(BlackBoxEntry entry, BlackBoxStorageMigrationJob job, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (job == null) throw new ArgumentNullException(nameof(job));
            BlackBoxStorageTargetEnum to = job.To;
            List<BlackBoxObject> objects = await ObjectsAsync(entry.Id, token).ConfigureAwait(false);
            long copied = 0;

            if (to == BlackBoxStorageTargetEnum.Database)
            {
                if (entry.StorageTarget == BlackBoxStorageTargetEnum.Database) return 0;
                // Read and verify everything first, then move each object into its row (it then reads from the database).
                List<BlackBoxObjectRef> sources = new List<BlackBoxObjectRef>();
                foreach (BlackBoxObject obj in objects.Where(o => !o.IsMissing && o.StorageKey != null))
                {
                    BlackBoxObjectRef source = BlackBoxObjectRepository.RefOf(entry, obj);
                    byte[] bytes = await ReadVerifiedAsync(source, obj, token).ConfigureAwait(false);
                    await _Database.BlackBoxObjects.SetLocationAsync(obj.Id, null, bytes, token).ConfigureAwait(false);
                    copied += bytes.LongLength;
                    sources.Add(source);
                }
                await _Database.BlackBoxEntries.UpdateStorageAsync(entry.Id, BlackBoxStorageTargetEnum.Database, null, token).ConfigureAwait(false);
                if (job.DeleteSource) await DeleteSourcesAsync(entry.StorageTarget, sources, job, token).ConfigureAwait(false);
                return copied;
            }

            IBlackBoxStore target = _Stores.Get(to);
            string prefix = entry.StoragePrefix ?? BlackBoxKeyLayout.BuildEntryPrefix(_Settings.Storage.KeyPrefix, entry.TenantId, entry.VesselId, entry.CreatedUtc, entry.Id);
            BlackBoxStorageTargetEnum from = entry.StorageTarget;

            // 1. Copy every external object to the same key on the target and verify the copy. Rows are untouched, so
            //    readers keep reading the source until the switch.
            List<BlackBoxObjectRef> externalSources = new List<BlackBoxObjectRef>();
            if (from != to)
            {
                foreach (BlackBoxObject obj in objects.Where(o => !o.IsMissing && o.StorageKey != null))
                {
                    BlackBoxObjectRef source = BlackBoxObjectRepository.RefOf(entry, obj);
                    byte[] bytes = await ReadVerifiedAsync(source, obj, token).ConfigureAwait(false);
                    BlackBoxObjectRef dest = new BlackBoxObjectRef { Target = to, EntryId = entry.Id, ObjectId = obj.Id, Name = obj.Name, StorageKey = obj.StorageKey };
                    await CopyVerifiedAsync(target, dest, obj, bytes, token).ConfigureAwait(false);
                    copied += bytes.LongLength;
                    externalSources.Add(source);
                }
            }

            // 2. Copy and verify the objects that live in the database (a Database source, or record.json forced there
            //    by a failed external write) to keys under the entry's prefix. Still nothing is switched.
            List<BlackBoxObject> resident = objects.Where(o => !o.IsMissing && o.StorageKey == null).ToList();
            Dictionary<string, byte[]> residentBytes = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (BlackBoxObject obj in resident)
            {
                BlackBoxObjectRef source = new BlackBoxObjectRef { Target = BlackBoxStorageTargetEnum.Database, EntryId = entry.Id, ObjectId = obj.Id, Name = obj.Name };
                byte[] bytes = await ReadVerifiedAsync(source, obj, token).ConfigureAwait(false);
                BlackBoxObjectRef dest = new BlackBoxObjectRef { Target = to, EntryId = entry.Id, ObjectId = obj.Id, Name = obj.Name, StorageKey = BlackBoxKeyLayout.BuildKey(prefix, obj.Name) };
                await CopyVerifiedAsync(target, dest, obj, bytes, token).ConfigureAwait(false);
                residentBytes[obj.Id] = bytes;
                copied += bytes.LongLength;
            }

            // 3. Switch the entry: external objects now read from the target at their unchanged keys; objects without a
            //    key still read from the database until step 4 points each row at its copy.
            if (from != to || entry.StoragePrefix == null) await _Database.BlackBoxEntries.UpdateStorageAsync(entry.Id, to, prefix, token).ConfigureAwait(false);

            // 4. Point each database-resident row at its key. Clearing its bytes deletes the database copy; with
            //    DeleteSource off the bytes stay in the row (unused, but still in database backups).
            foreach (BlackBoxObject obj in resident)
            {
                await _Database.BlackBoxObjects.SetLocationAsync(obj.Id, BlackBoxKeyLayout.BuildKey(prefix, obj.Name), job.DeleteSource ? null : residentBytes[obj.Id], token).ConfigureAwait(false);
            }

            // 5. Delete the source copies of the external objects.
            if (job.DeleteSource && externalSources.Count > 0) await DeleteSourcesAsync(from, externalSources, job, token).ConfigureAwait(false);
            return copied;
        }

        #endregion

        #region Private-Methods

        private void EnsureRunning(string jobId)
        {
            lock (_Lock)
            {
                if (!_Active.IsCompleted) return;
                CancellationToken token = _Stop.Token;
                _Active = Task.Run(async () =>
                {
                    try
                    {
                        await RunAsync(jobId, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                    }
                    catch (Exception e)
                    {
                        _Logging.Warn(_Header + "migration " + jobId + " stopped: " + e.Message);
                        try
                        {
                            BlackBoxStorageMigrationJob? job = await _Database.BlackBoxMigrations.ReadAsync(jobId).ConfigureAwait(false);
                            if (job != null)
                            {
                                job.State = BlackBoxJobStateEnum.Failed;
                                job.LastError = BlackBoxLimits.Clip(e.Message, BlackBoxLimits.MaxShortTextChars);
                                job.FinishedUtc = DateTime.UtcNow;
                                await _Database.BlackBoxMigrations.UpdateAsync(job).ConfigureAwait(false);
                            }
                        }
                        catch (Exception)
                        {
                            // The job row keeps its last saved progress.
                        }
                    }
                });
            }
        }

        private static BlackBoxScanQuery ScanFor(BlackBoxStorageMigrationJob job, string? afterId, int pageSize)
        {
            BlackBoxScanQuery query = new BlackBoxScanQuery { AfterId = afterId, PageSize = pageSize, NeedsMigrationTo = job.To };
            if (job.From.HasValue) query.StorageTarget = job.From.Value;
            return query;
        }

        private async Task<List<BlackBoxObject>> ObjectsAsync(string entryId, CancellationToken token)
        {
            List<BlackBoxObject> all = new List<BlackBoxObject>();
            int page = 1;
            while (true)
            {
                EnumerationResult<BlackBoxObject> objects = await _Database.BlackBoxObjects.EnumerateAsync(entryId, new EnumerationQuery { PageNumber = page, PageSize = 100 }, token).ConfigureAwait(false);
                all.AddRange(objects.Objects);
                if (page >= objects.TotalPages) break;
                page++;
            }
            return all;
        }

        private async Task<byte[]> ReadVerifiedAsync(BlackBoxObjectRef reference, BlackBoxObject obj, CancellationToken token)
        {
            IBlackBoxStore store = _Stores.Get(reference.Target);
            byte[] bytes;
            using (Stream stream = await store.OpenReadAsync(reference, 0, null, token).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, token).ConfigureAwait(false);
                bytes = buffer.ToArray();
            }
            if (obj.Sha256 != null && !String.Equals(BlackBoxObjectCodec.Sha256Hex(bytes), obj.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new BlackBoxObjectCorruptException("The " + reference.Target + " copy of " + obj.Name + " does not match its SHA-256; the entry was not moved.");
            return bytes;
        }

        private async Task CopyVerifiedAsync(IBlackBoxStore target, BlackBoxObjectRef dest, BlackBoxObject obj, byte[] bytes, CancellationToken token)
        {
            await target.WriteAsync(new BlackBoxObjectWrite
            {
                Reference = dest,
                ContentType = obj.ContentType,
                ContentEncoding = obj.ContentEncoding,
                Data = bytes,
                Sha256 = obj.Sha256
            }, token).ConfigureAwait(false);
            byte[] readBack;
            using (Stream stream = await target.OpenReadAsync(dest, 0, null, token).ConfigureAwait(false))
            using (MemoryStream buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, token).ConfigureAwait(false);
                readBack = buffer.ToArray();
            }
            string expected = obj.Sha256 ?? BlackBoxObjectCodec.Sha256Hex(bytes);
            if (!String.Equals(BlackBoxObjectCodec.Sha256Hex(readBack), expected, StringComparison.OrdinalIgnoreCase))
                throw new BlackBoxObjectCorruptException("The " + target.Target + " copy of " + obj.Name + " did not verify after the write; the entry was not moved.");
        }

        private async Task DeleteSourcesAsync(BlackBoxStorageTargetEnum source, List<BlackBoxObjectRef> references, BlackBoxStorageMigrationJob job, CancellationToken token)
        {
            if (source == BlackBoxStorageTargetEnum.Database || references.Count == 0) return;
            try
            {
                IBlackBoxStore store = _Stores.Get(source);
                for (int start = 0; start < references.Count; start += Armada.Core.Services.CollectionLimits.MaxRequestItems)
                {
                    BlackBoxStoreDeleteResult deleted = await store.DeleteManyAsync(new BlackBoxObjectRefSet { References = references.Skip(start).Take(Armada.Core.Services.CollectionLimits.MaxRequestItems).ToList() }, token).ConfigureAwait(false);
                    if (deleted.Failed > 0) job.LastError = BlackBoxLimits.Clip("Source copies left on " + source + ": " + deleted.FirstError, BlackBoxLimits.MaxShortTextChars);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                job.LastError = BlackBoxLimits.Clip("Source copies left on " + source + ": " + e.Message, BlackBoxLimits.MaxShortTextChars);
            }
        }

        #endregion
    }
}
