namespace Armada.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>Single-flight background inventory. Published snapshots are detached copies. History resets on restart.</summary>
    public class StorageInventoryService : IDisposable
    {
        private readonly DatabaseDriver _Database;
        private readonly ArmadaSettings _Settings;
        private readonly Func<string, bool> _Connected;
        private readonly object _Sync = new object();
        private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
        private readonly Dictionary<string, StorageHostReport> _Remote = new Dictionary<string, StorageHostReport>();
        private StorageSnapshot _Snapshot = new StorageSnapshot();
        private Task _Collection = Task.CompletedTask;
        private Task _Loop = Task.CompletedTask;
        private bool _Disposed;
        private static readonly Meter _Meter = new Meter(ArmadaMetrics.MeterName);
        private static readonly ActivitySource _Trace = new ActivitySource(ArmadaMetrics.MeterName);
        private static readonly Counter<long> _Runs = _Meter.CreateCounter<long>("armada.storage.collections");
        private static readonly Histogram<double> _StageDuration = _Meter.CreateHistogram<double>("armada.storage.stage_duration", "s");
        private static readonly Histogram<double> _Duration = _Meter.CreateHistogram<double>("armada.storage.collection_duration", "s");
        private static Dictionary<string, long> _LatestCategories = new Dictionary<string, long>();
        private static readonly ObservableGauge<long> _Bytes = _Meter.CreateObservableGauge<long>("armada.storage.observed_bytes", ObserveBytes, "By");

        /// <summary>Construct an inventory using the active database and host connectivity resolver.</summary>
        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        public StorageInventoryService(DatabaseDriver database, ArmadaSettings settings, Func<string, bool> connected)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Connected = connected ?? throw new ArgumentNullException(nameof(connected));
        }

        /// <summary>Starts periodic collection once. Call during server startup.</summary>
        public void Start()
        {
            lock (_Sync)
            {
                if (_Disposed) throw new ObjectDisposedException(nameof(StorageInventoryService));
                if (!_Loop.IsCompleted) return;
                _Loop = Task.Run(LoopAsync);
            }
        }

        /// <summary>Returns a bounded cached copy. Reading does not perform filesystem or database work.</summary>
        public StorageSnapshot Read()
        {
            lock (_Sync)
            {
                StorageSnapshot result = Clone(_Snapshot);
                // Hosts are not serialized with the snapshot (they are paged through EnumerateHosts); copy them apart.
                result.Hosts = Clone(_Snapshot.Hosts);
                result.Collecting = !_Collection.IsCompleted;
                result.IntervalSeconds = _Settings.Storage.IntervalSeconds;
                result.HistoryCapacity = _Settings.Storage.HistoryCapacity;
                result.Retention = Retention();
                foreach (StorageHostReport host in result.Hosts)
                {
                    if (host.HostId != "admiral" && _Remote.TryGetValue(host.HostId, out StorageHostReport? recent))
                    {
                        host.Locations = Clone(recent.Locations);
                        host.Volumes = Clone(recent.Volumes);
                        host.Warnings = Clone(recent.Warnings);
                        host.MeasuredUtc = recent.MeasuredUtc;
                        host.Status = recent.Status;
                        host.IntervalSeconds = recent.IntervalSeconds;
                    }
                    host.Stale = DateTime.UtcNow - host.MeasuredUtc > TimeSpan.FromSeconds(Math.Max(_Settings.Storage.IntervalSeconds, host.IntervalSeconds) * 2);
                    if (host.HostId != "admiral" && !_Connected(host.HostId)) host.Stale = true;
                }
                result.HostCount = result.Hosts.Count;
                result.FreshHostCount = result.Hosts.Count(h => !h.Stale && h.Status == "Complete");
                result.ObservedFileBytes = result.Hosts.Sum(h => h.Locations.Sum(l => l.Bytes ?? 0));
                return result;
            }
        }

        /// <summary>
        /// One page of the cached host reports: the Admiral first, then each Harbor by name and id. Reading does not
        /// perform filesystem or database work. The host list is held in memory (one report per registered Harbor,
        /// rebuilt on each collection), so the page is cut from it; TotalRecords is exact.
        /// </summary>
        /// <param name="query">Paging (page size 1 to 1000); null is the first page of 10.</param>
        /// <returns>The page.</returns>
        public EnumerationResult<StorageHostReport> EnumerateHosts(EnumerationQuery? query)
        {
            if (query == null) query = new EnumerationQuery();
            StorageSnapshot snapshot = Read();
            List<StorageHostReport> ordered = snapshot.Hosts
                .OrderBy(h => h.HostId == "admiral" ? 0 : 1)
                .ThenBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(h => h.HostId, StringComparer.Ordinal)
                .ToList();
            return Armada.Core.Services.CollectionLimits.Page(query, ordered);
        }

        /// <summary>Requests collection without waiting. Concurrent requests coalesce and return false.</summary>
        public bool RequestRefresh(bool exactCounts = false)
        {
            lock (_Sync)
            {
                if (_Disposed || !_Collection.IsCompleted) return false;
                _Collection = Task.Run(() => CollectAsync(exactCounts, _Stop.Token));
                return true;
            }
        }

        /// <summary>Accepts bounded remote measurements under the authenticated link identity, never the claimed host ID.</summary>
        public void Receive(string harborId, StorageHostReport report)
        {
            if (String.IsNullOrWhiteSpace(harborId) || report == null) return;
            if (report.IntervalSeconds < 30 || report.IntervalSeconds > 86400) return;
            if (report.Status is not ("Complete" or "Partial" or "Unavailable" or "Pending")) return;
            if (report.Locations == null || report.Volumes == null || report.Warnings == null) return;
            if (report.Locations.Count > _Settings.Storage.MaxLocations || report.Volumes.Count > StorageHostReport.MaxVolumes || report.Warnings.Count > StorageHostReport.MaxWarnings) return;
            if (report.MeasuredUtc > DateTime.UtcNow.AddMinutes(5) || report.MeasuredUtc < DateTime.UtcNow.AddDays(-30)) return;
            foreach (StorageLocation location in report.Locations)
            {
                if (location == null || location.Path == null || location.Path.Length > 4096 || location.Category == null || location.Category.Length > 64 || location.Bytes < 0 || location.FileCount < 0) return;
                if (location.LargestFiles == null || location.Warnings == null || location.LargestFiles.Count > 100 || location.Warnings.Count > 10) return;
                if (location.Warnings.Any(w => w == null || w.Length > 128)) return;
                if (location.LargestFiles.Any(f => f == null || f.Path == null || f.Path.Length > 4096 || f.Bytes < 0)) return;
            }
            if (report.Volumes.Any(v => v == null || v.Path == null || v.Path.Length > 4096 || v.TotalBytes < 0 || v.AvailableBytes < 0)) return;
            if (report.Warnings.Any(w => w == null || w.Length > 128)) return;
            StorageHostReport copy = Clone(report);
            copy.HostId = harborId;
            lock (_Sync)
            {
                if (_Disposed) return;
                // Only one cached report per currently known Harbor; prune removed Harbors during collection.
                if (_Remote.TryGetValue(harborId, out StorageHostReport? previous) && previous.MeasuredUtc > copy.MeasuredUtc) return;
                _Remote[harborId] = copy;
            }
        }

        /// <summary>Stops collection and waits for owned workers before the database is disposed.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Disposes owned workers and cancellation source.</summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;
            Task collection;
            Task loop;
            lock (_Sync)
            {
                if (_Disposed) return;
                _Disposed = true;
                _Stop.Cancel();
                collection = _Collection;
                loop = _Loop;
            }
            try { Task.WhenAll(collection, loop).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
            _Stop.Dispose();
        }

        private async Task LoopAsync()
        {
            while (!_Stop.IsCancellationRequested)
            {
                RequestRefresh();
                try { await Task.Delay(TimeSpan.FromSeconds(_Settings.Storage.IntervalSeconds), _Stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        private async Task CollectAsync(bool exactCounts, CancellationToken token)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            using (Activity? activity = _Trace.StartActivity("storage.collect"))
            using (CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                budget.CancelAfter(TimeSpan.FromSeconds(_Settings.Storage.TimeoutSeconds));
                try
                {
                    StorageSnapshot next;
                    lock (_Sync) next = Clone(_Snapshot);
                    List<StorageLocation> roots = await StageAsync("discovery", RootsAsync, budget.Token).ConfigureAwait(false);
                    budget.CancelAfter(Timeout.InfiniteTimeSpan);
                    StorageHostReport local = await StageAsync("filesystem", ct => new StorageFileCollector().CollectAsync(roots, _Settings.Storage, ct), token).ConfigureAwait(false);
                    local.HostId = "admiral";
                    await AddBlackBoxLocationsAsync(local, token).ConfigureAwait(false);
                    next.Hosts = new List<StorageHostReport> { local };
                    budget.CancelAfter(TimeSpan.FromSeconds(_Settings.Storage.TimeoutSeconds));
                    try { next.Database = await StageAsync("database", ct => _Database.Storage.MeasureAsync(exactCounts, ct), budget.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        next.Database.Warnings = new List<string> { "CollectionTimedOut" };
                        next.Database.Status = "Partial";
                    }
                    catch (System.Data.Common.DbException)
                    {
                        next.Database.Status = "Unavailable";
                        next.Database.Warnings = new List<string> { "DatabaseStatisticsUnavailable" };
                    }
                    // Remote connectivity is checked on every read, including between local scans.
                    List<Harbor> harbors = await _Database.Harbors.EnumerateAsync(token).ConfigureAwait(false);
                    lock (_Sync)
                    {
                        HashSet<string> known = new HashSet<string>(harbors.Select(h => h.Id));
                        foreach (string removed in _Remote.Keys.Where(id => !known.Contains(id)).ToList()) _Remote.Remove(removed);
                        foreach (Harbor harbor in harbors)
                        {
                            StorageHostReport remote = _Remote.TryGetValue(harbor.Id, out StorageHostReport? cached)
                                ? Clone(cached) : new StorageHostReport { HostId = harbor.Id, Status = "Unavailable", Warnings = new List<string> { "HarborReportUnavailable" } };
                            remote.Name = harbor.Name;
                            remote.Stale = !_Connected(harbor.Id) || DateTime.UtcNow - remote.MeasuredUtc > TimeSpan.FromSeconds(Math.Max(_Settings.Storage.IntervalSeconds, remote.IntervalSeconds) * 2);
                            next.Hosts.Add(remote);
                        }
                    }
                    next.MeasuredUtc = DateTime.UtcNow;
                    next.LastError = null;
                    StorageHistoryPoint point = new StorageHistoryPoint { MeasuredUtc = next.MeasuredUtc.Value, DatabaseBytes = next.Database.AllocatedBytes };
                    foreach (StorageHostReport host in next.Hosts)
                    {
                        if (host.Locations.Any(l => l.Bytes.HasValue)) point.Hosts[host.HostId] = host.Locations.Sum(l => l.Bytes ?? 0);
                        foreach (StorageLocation location in host.Locations)
                        {
                            if (!location.Bytes.HasValue) continue;
                            string category = location.Category;
                            point.Categories[category] = point.Categories.GetValueOrDefault(category) + (location.Bytes ?? 0);
                        }
                    }
                    point.ObservedFileBytes = point.Hosts.Values.Sum();
                    foreach (StorageTable table in next.Database.Tables.Where(t => t.DataBytes.HasValue || t.IndexBytes.HasValue)) point.Tables[table.Schema + "." + table.Name] = (table.DataBytes ?? 0) + (table.IndexBytes ?? 0);
                    // Bounded breakdowns: the largest entries plus one folded (other) entry, so totals still add up.
                    point.Hosts = StorageHistoryPoint.Fold(point.Hosts);
                    point.Categories = StorageHistoryPoint.Fold(point.Categories);
                    point.Tables = StorageHistoryPoint.Fold(point.Tables);
                    next.History.Add(point);
                    next.History = next.History.TakeLast(_Settings.Storage.HistoryCapacity).ToList();
                    lock (_Sync) _Snapshot = next;
                    Record("Succeeded", elapsed.Elapsed.TotalSeconds, point);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    lock (_Sync) _Snapshot.LastError = "CollectionTimedOut";
                    activity?.SetStatus(ActivityStatusCode.Error);
                    Record("TimedOut", elapsed.Elapsed.TotalSeconds, null);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception)
                {
                    // Keep the previous snapshot and do not expose provider exceptions or connection credentials.
                    lock (_Sync) _Snapshot.LastError = "CollectionFailed";
                    activity?.SetStatus(ActivityStatusCode.Error);
                    Record("Failed", elapsed.Elapsed.TotalSeconds, null);
                }
            }
        }

        private async Task<List<StorageLocation>> RootsAsync(CancellationToken token)
        {
            List<StorageLocation> roots = new List<StorageLocation>();
            foreach (Dock dock in await _Database.Docks.EnumerateAsync(token).ConfigureAwait(false))
            {
                if (!String.IsNullOrEmpty(dock.HarborId)) continue;
                if (!String.IsNullOrWhiteSpace(dock.WorktreePath)) roots.Add(new StorageLocation { Category = "Docks", Path = dock.WorktreePath!, EntityId = dock.Id, Activity = dock.State.ToString() });
                if (!String.IsNullOrWhiteSpace(dock.RepositoryPath)) roots.Add(new StorageLocation { Category = "Repositories", Path = dock.RepositoryPath!, EntityId = dock.VesselId });
            }
            // Local paths are explicit registrations, never scan arbitrary parent directories looking for repositories.
            foreach (Vessel vessel in await _Database.Vessels.EnumerateAsync(token).ConfigureAwait(false))
            {
                if (!String.IsNullOrWhiteSpace(vessel.LocalPath)) roots.Add(new StorageLocation { Category = "Repositories", Path = vessel.LocalPath!, EntityId = vessel.Id });
                if (!String.IsNullOrWhiteSpace(vessel.WorkingDirectory)) roots.Add(new StorageLocation { Category = "Repositories", Path = vessel.WorkingDirectory!, EntityId = vessel.Id });
            }
            AddChildren(roots, _Settings.ReposDirectory, "Repositories", token);
            AddChildren(roots, _Settings.DocksDirectory, "Docks", token);
            foreach (string category in new[] { "missions", "captains", "planning-sessions", "diffs", "instructions", "final-messages", "docks" })
                roots.Add(new StorageLocation { Category = "Logs/" + category, Path = Path.Combine(_Settings.LogDirectory, category) });
            roots.Add(new StorageLocation { Category = "Logs/server", Path = _Settings.LogDirectory });
            roots.Add(new StorageLocation { Category = "Docks", Path = _Settings.DocksDirectory });
            roots.Add(new StorageLocation { Category = "Repositories", Path = _Settings.ReposDirectory });
            roots.Add(new StorageLocation { Category = "Backups", Path = Path.Combine(_Settings.DataDirectory, "backups") });
            roots.Add(new StorageLocation { Category = "Builds", Path = Path.Combine(_Settings.DataDirectory, "bin") });
            if (_Settings.Database.Type == DatabaseTypeEnum.Sqlite)
            {
                foreach (string suffix in new[] { "", "-wal", "-shm" }) roots.Add(new StorageLocation { Category = "DatabaseFiles", Path = _Settings.Database.Filename + suffix });
            }
            roots.Add(new StorageLocation { Category = "Other", Path = _Settings.DataDirectory });
            return roots;
        }

        private async Task AddBlackBoxLocationsAsync(StorageHostReport local, CancellationToken token)
        {
            // Black box bytes per storage target, from the index (the Database figure is also inside the database files).
            try
            {
                List<BlackBoxStorageTargetUsage> usage = await _Database.BlackBoxEntries.SumBytesByTargetAsync(null, token).ConfigureAwait(false);
                foreach (BlackBoxStorageTargetUsage target in usage.Take(7))
                {
                    if (local.Locations.Count >= _Settings.Storage.MaxLocations) break;
                    local.Locations.Add(new StorageLocation
                    {
                        Category = "BlackBox",
                        Path = BlackBoxLocationPath(target.Target),
                        EntityId = target.Target.ToString(),
                        Activity = target.Entries + " entr" + (target.Entries == 1 ? "y" : "ies"),
                        Bytes = target.Bytes,
                        FileCount = target.Entries,
                        Status = "Complete"
                    });
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                local.Warnings.Add("BlackBoxUsageUnavailable");
            }
        }

        private string BlackBoxLocationPath(BlackBoxStorageTargetEnum target)
        {
            Armada.Core.Settings.BlackBoxStorageSettings s = _Settings.BlackBox.Storage;
            string prefix = s.KeyPrefix.Trim('/');
            switch (target)
            {
                case BlackBoxStorageTargetEnum.Database: return "database:blackbox_objects";
                case BlackBoxStorageTargetEnum.Disk:
                    try { return Armada.Core.BlackBox.Storage.BlackBoxBlobClientFactory.ResolveDiskDirectory(s, _Settings.DataDirectory); }
                    catch (ArgumentException) { return "disk:" + prefix; }
                case BlackBoxStorageTargetEnum.S3: return "s3://" + (s.S3.Bucket ?? "_") + "/" + prefix;
                case BlackBoxStorageTargetEnum.AzureBlob: return "azure://" + (s.AzureBlob.AccountName ?? "_") + "/" + (s.AzureBlob.Container ?? "_") + "/" + prefix;
                case BlackBoxStorageTargetEnum.GoogleCloud: return "gs://" + (s.GoogleCloud.Bucket ?? "_") + "/" + prefix;
                case BlackBoxStorageTargetEnum.Cifs: return "smb://" + (s.Cifs.Hostname ?? "_") + "/" + (s.Cifs.Share ?? "_") + "/" + prefix;
                case BlackBoxStorageTargetEnum.Nfs: return "nfs://" + (s.Nfs.Hostname ?? "_") + (s.Nfs.ExportPath ?? "/_") + "/" + prefix;
                default: return "blackbox:" + target;
            }
        }

        private void AddChildren(List<StorageLocation> roots, string path, string category, CancellationToken token)
        {
            try
            {
                if (StorageFileCollector.HasLinkedAncestor(Path.GetFullPath(path))) return;
                foreach (string child in Directory.EnumerateDirectories(path))
                {
                    token.ThrowIfCancellationRequested();
                    if (roots.Count >= _Settings.Storage.MaxLocations - 20) break;
                    roots.Add(new StorageLocation { Category = category, Path = child });
                }
            }
            catch (IOException) { /* The collector reports inaccessible and missing roots. */ }
            catch (UnauthorizedAccessException) { /* The collector reports inaccessible roots. */ }
        }

        private async Task<T> StageAsync<T>(string stage, Func<CancellationToken, Task<T>> operation, CancellationToken token)
        {
            Stopwatch elapsed = Stopwatch.StartNew();
            using (Activity? activity = _Trace.StartActivity("storage." + stage))
            {
                try { return await operation(token).ConfigureAwait(false); }
                catch (Exception) { activity?.SetStatus(ActivityStatusCode.Error); throw; }
                finally
                {
                    try { _StageDuration.Record(elapsed.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("stage", stage)); }
                    catch (Exception) { /* Telemetry is best effort. */ }
                }
            }
        }

        private Dictionary<string, int> Retention()
        {
            return new Dictionary<string, int>
            {
                ["DataDays"] = _Settings.DataRetentionDays,
                ["RequestHistoryDays"] = _Settings.RequestHistoryRetentionDays,
                ["PlanningSessionDays"] = _Settings.PlanningSessionRetentionDays,
                ["AskArchiveDays"] = _Settings.Retention.AskThreadArchiveAfterDays,
                ["AskDeleteDays"] = _Settings.Retention.AskThreadDeleteAfterDays,
                ["JobDays"] = _Settings.Retention.JobRetentionDays,
                ["ImportDays"] = _Settings.Retention.ImportBatchRetentionDays,
                ["CliPermissionDays"] = _Settings.Retention.CliPermissionRequestRetentionDays,
                ["FleetActionDays"] = _Settings.FleetActions.RunRetentionDays,
                ["BuildSlots"] = _Settings.RebuildSlotRetentionCount
            };
        }

        private static T Clone<T>(T value)
        {
            return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
        }

        private static Measurement<long>[] ObserveBytes()
        {
            return Volatile.Read(ref _LatestCategories).Select(c => new Measurement<long>(c.Value, new KeyValuePair<string, object?>("category", c.Key))).ToArray();
        }

        private static void Record(string outcome, double seconds, StorageHistoryPoint? point)
        {
            try
            {
                _Runs.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
                _Duration.Record(seconds);
                if (point != null)
                {
                    Dictionary<string, long> categories = new Dictionary<string, long>();
                    foreach (KeyValuePair<string, long> category in point.Categories)
                    {
                        string key = category.Key.StartsWith("Logs/", StringComparison.Ordinal) ? "Logs" : category.Key;
                        if (key is not ("Logs" or "DatabaseFiles" or "Repositories" or "Docks" or "Backups" or "Builds" or "Other")) continue;
                        categories[key] = categories.GetValueOrDefault(key) + category.Value;
                    }
                    if (point.DatabaseBytes.HasValue) categories["DatabaseAllocation"] = point.DatabaseBytes.Value;
                    Volatile.Write(ref _LatestCategories, categories);
                }
            }
            catch (Exception) { /* Instrumentation cannot interrupt collection. */ }
        }
    }
}
