namespace Armada.Tui.Screens.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Client;
    using Armada.Core.Models;
    using Armada.Core.Storage;
    using Armada.Tui.Routing;
    using Armada.Tui.Screens.Kit;
    using Armada.Tui.Widgets;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>Read-only global administrator storage inventory, with the black box's retention policy, usage, and storage target health; a black box GC dry run reports what garbage collection would remove. Refresh reads cached data; collection is explicit. UI-thread only.</summary>
    public class StorageScreen : GridScreen<StorageMetricRow>
    {
        /// <summary>Black box retention and storage status from the last read, or null before it.</summary>
        public BlackBoxAdminStatus? BlackBox { get; private set; } = null;

        /// <summary>The last black box GC dry run report, or null.</summary>
        public BlackBoxGcReport? GcDryRunReport { get; private set; } = null;

        private bool _Loading;
        private StorageSnapshot? _Snapshot = null;

        /// <summary>Construct the storage screen and read its cached inventory.</summary>
        public StorageScreen(RouteMatch route, TuiContext context)
            : base(route, context, "Storage", "Managed file bytes, separate database allocation, records, volumes and retention. Enter opens details.", "storage", r => r.Group + r.Host + r.Name)
        {
            Header.AddButton("Refresh view", Refresh, "r");
            Header.AddButton("Collect now", () => Load(true, false), "c");
            Header.AddButton("Count records exactly", () => Load(true, true), "e");
            Header.AddButton("Black box GC dry run", GcDryRun, "G");
            AddFixed(Header, w => Header.HeightFor(w));
            Grid.MultiSelect = false;
            Grid.AddColumn(new GridColumn<StorageMetricRow>("group", "Category", r => Context.Loc.T(r.Group)) { Width = 15 });
            Grid.AddColumn(new GridColumn<StorageMetricRow>("host", "Host", r => r.Host) { Width = 16 });
            Grid.AddColumn(new GridColumn<StorageMetricRow>("name", "Name", r => r.Name) { Weight = 3 });
            Grid.AddColumn(new GridColumn<StorageMetricRow>("amount", "Value", r => r.Amount) { Width = 22 });
            Grid.AddColumn(new GridColumn<StorageMetricRow>("status", "Status", r => Context.Loc.T(r.Status)) { Width = 14 });
            AddFill(Grid);
            Scope.Focus(Grid);
            Refresh();
        }

        /// <inheritdoc />
        public override void Refresh()
        {
            Load(false, false);
            LoadBlackBox();
        }

        /// <inheritdoc />
        public override IReadOnlyList<KeyValuePair<string, string>> Hints
        {
            get
            {
                List<KeyValuePair<string, string>> hints = new List<KeyValuePair<string, string>>(base.Hints);
                if (Context.Session.IsGlobalAdmin) hints.Add(new KeyValuePair<string, string>("G", "Black box GC dry run"));
                return hints;
            }
        }

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && key.Rune == 'G' && !(Scope.FocusedLeaf() is TextInput) && Context.Session.IsGlobalAdmin)
            {
                GcDryRun();
                return true;
            }

            return base.HandleKey(key);
        }

        /// <summary>Run a black box garbage collection dry run (nothing is deleted) and show what it would remove.</summary>
        public void GcDryRun()
        {
            if (!Context.Session.IsGlobalAdmin) return;
            _ = ScreenOps.Run(Context, () => Context.Client.RunBlackBoxGcAsync(true), report =>
            {
                GcDryRunReport = report;
                if (report == null) return;
                ScreenOps.ShowText(Context, "Black Box GC Dry Run", "Dry run: nothing was deleted.\n\nExpired: " + report.Expired + "\nEvicted: " + report.Evicted + "\nCascaded: " + report.Cascaded
                    + "\nOrphans: " + report.OrphansRemoved + "\nObjects missing: " + report.ObjectsMissing + "\nBytes freed: " + Bytes(report.BytesFreed) + "\nFailures: " + report.Failures);
            }, "Black box GC dry run failed");
        }

        /// <inheritdoc />
        protected override void OnActivate(StorageMetricRow row)
        {
            ScreenOps.ShowText(Context, row.Name, row.Name + "\n" + row.Amount + "\n" + Context.Loc.T(row.Status) + "\n\n" + row.Detail);
        }

        private void Load(bool collect, bool exact)
        {
            if (!Context.Session.IsGlobalAdmin)
            {
                Grid.EmptyText = "Storage is available to system administrators only.";
                return;
            }
            if (_Loading) return;
            _Loading = true;
            _ = ScreenOps.Run(Context, async () =>
            {
                StorageSnapshot? read = collect
                    ? await Context.Client.RefreshStorageAsync(new StorageRefreshRequest { ExactCounts = exact }).ConfigureAwait(false)
                    : await Context.Client.GetStorageAsync().ConfigureAwait(false);
                // The snapshot carries host totals only; the host reports are paged.
                if (read != null) read.Hosts = await Context.Client.ListAllStorageHostsAsync().ConfigureAwait(false);
                return read;
            }, snapshot =>
            {
                _Loading = false;
                if (snapshot != null)
                {
                    _Snapshot = snapshot;
                    Show(snapshot);
                }
            }, "Storage could not be loaded", _ => _Loading = false);
        }

        private void LoadBlackBox()
        {
            if (!Context.Session.IsGlobalAdmin) return;
            _ = ScreenOps.Quiet(Context, async () =>
            {
                BlackBoxAdminStatus status = new BlackBoxAdminStatus();
                try { status.Retention = await Context.Client.GetBlackBoxRetentionAsync().ConfigureAwait(false); }
                catch (Exception ex) when (ex is ArmadaApiException || ex is System.Text.Json.JsonException || ex is InvalidOperationException) { status.Error = ex.Message; }
                try { status.Storage = await Context.Client.GetBlackBoxStorageAsync().ConfigureAwait(false); }
                catch (Exception ex) when (ex is ArmadaApiException || ex is System.Text.Json.JsonException || ex is InvalidOperationException) { status.Error = ex.Message; }
                return status;
            }, status =>
            {
                BlackBox = status;
                if (_Snapshot != null) Show(_Snapshot);
            });
        }

        private void AddBlackBoxRows(List<StorageMetricRow> rows)
        {
            BlackBoxAdminStatus? bb = BlackBox;
            if (bb == null) return;
            const string group = "Black Box";
            if (bb.Storage != null)
            {
                BlackBoxStorageStatus st = bb.Storage;
                rows.Add(new StorageMetricRow { Group = group, Host = st.Target.ToString(), Name = "Storage target", Amount = st.Health.LatencyMs + " ms", Status = st.Health.Healthy ? "Healthy" : "Unhealthy",
                    Detail = "Target: " + st.Target + "\nHealthy: " + st.Health.Healthy + "\n" + (st.Health.Message ?? "") + "\nFallback to database: " + st.FallbackToDatabase + (st.ActiveMigrationId != null ? "\nActive migration: " + st.ActiveMigrationId : "") });
                foreach (BlackBoxStorageTargetUsage u in st.Usage)
                    rows.Add(new StorageMetricRow { Group = group, Host = u.Target.ToString(), Name = "Usage", Amount = Bytes(u.Bytes), Status = u.Entries.ToString("N0", CultureInfo.CurrentCulture) + " entries", Detail = "Entries: " + u.Entries + "\nBytes: " + Bytes(u.Bytes) });
            }

            if (bb.Retention != null)
            {
                BlackBoxRetentionStatus r = bb.Retention;
                rows.Add(new StorageMetricRow { Group = group, Name = "Retention (days)", Amount = r.Policy.RetentionDays.ToString(CultureInfo.CurrentCulture), Status = "Policy",
                    Detail = "Retention days: " + r.Policy.RetentionDays + "\nResolved retention days: " + r.Policy.ResolvedRetentionDays + "\nKeep latest per mission: " + r.Policy.KeepLatestPerMission
                        + "\nMax entries per vessel: " + r.Policy.MaxEntriesPerVessel + "\nMax total bytes: " + Bytes(r.Policy.MaxTotalBytes) + "\nDelete with mission: " + r.Policy.DeleteWithMission
                        + "\nDelete with vessel: " + r.Policy.DeleteWithVessel + "\nGC interval minutes: " + r.Policy.GcIntervalMinutes + "\nThe policy is edited in the server settings file (blackBox.retention)." });
                rows.Add(new StorageMetricRow { Group = group, Name = "Entries", Amount = r.TotalEntries.ToString("N0", CultureInfo.CurrentCulture), Status = Bytes(r.TotalBytes),
                    Detail = "Pinned: " + r.PinnedEntries + "\nExpired: " + r.ExpiredEntries + "\nDeleting: " + r.DeletingEntries });
                string last = r.LastRun == null ? "Never" : r.LastRun.StartedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                rows.Add(new StorageMetricRow { Group = group, Name = "Garbage collection", Amount = last, Status = r.NextRunUtc.HasValue ? "Next " + r.NextRunUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "Not scheduled",
                    Detail = r.LastRun == null ? "No garbage collection has run yet. G runs a dry run." : "Last run removed " + (r.LastRun.Expired + r.LastRun.Evicted + r.LastRun.Cascaded) + " entries and freed " + Bytes(r.LastRun.BytesFreed) + ". G runs a dry run." });
            }

            if (bb.Retention == null && bb.Storage == null)
                rows.Add(new StorageMetricRow { Group = group, Name = "Status", Amount = "Unavailable", Status = "Unavailable", Detail = bb.Error ?? "The server did not report black box status." });
        }

        private void Show(StorageSnapshot snapshot)
        {
            List<StorageMetricRow> rows = new List<StorageMetricRow>();
            foreach (StorageHostReport host in snapshot.Hosts)
            {
                string measured = host.MeasuredUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
                foreach (StorageLocation location in host.Locations)
                {
                    string detail = "Measured: " + measured + "\nFiles: " + location.FileCount + "\nEntity: " + location.EntityId + "\n" + String.Join("\n", location.Warnings);
                    foreach (StorageFile file in location.LargestFiles) detail += "\n" + file.Path + ": " + Bytes(file.Bytes);
                    rows.Add(new StorageMetricRow { Group = location.Category, Host = host.Name, Name = location.Path, Amount = Bytes(location.Bytes), Status = host.Stale ? "Stale" : location.Status, Detail = detail });
                }
                foreach (StorageVolume volume in host.Volumes) rows.Add(new StorageMetricRow { Group = "Volumes", Host = host.Name, Name = volume.Path, Amount = Bytes(volume.AvailableBytes) + " free", Status = volume.Status, Detail = "Capacity including unrelated files: " + Bytes(volume.TotalBytes) });
            }
            rows.Add(new StorageMetricRow { Group = "Database", Host = snapshot.Database.Provider, Name = "Allocation (separate from file bytes)", Amount = Bytes(snapshot.Database.AllocatedBytes), Status = snapshot.Database.Status, Detail = String.Join("\n", snapshot.Database.Warnings) });
            foreach (StorageTable table in snapshot.Database.Tables) rows.Add(new StorageMetricRow { Group = "Records", Host = snapshot.Database.Provider, Name = table.Schema + "." + table.Name, Amount = table.Rows?.ToString("N0", CultureInfo.CurrentCulture) ?? "Unavailable", Status = table.CountKind, Detail = "Logical data: " + Bytes(table.LogicalBytes) + "\nData allocation: " + Bytes(table.DataBytes) + "\nIndexes: " + Bytes(table.IndexBytes) + "\n" + table.Warning });
            foreach (KeyValuePair<string, int> policy in snapshot.Retention) rows.Add(new StorageMetricRow { Group = "Retention", Name = policy.Key, Amount = policy.Value.ToString(CultureInfo.CurrentCulture), Detail = "Days, with zero disabling expiry. BuildSlots counts retained builds. No cleanup actions." });
            foreach (StorageHistoryPoint point in snapshot.History) rows.Add(new StorageMetricRow { Group = "History", Name = point.MeasuredUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture), Amount = Bytes(point.ObservedFileBytes), Detail = "Process-local history resets on restart. Database allocation: " + Bytes(point.DatabaseBytes) });
            AddBlackBoxRows(rows);
            Grid.EmptyText = snapshot.Collecting ? "Collecting... Refresh view to read the result." : "No storage measurements yet.";
            Grid.SetLocalRows(rows);
        }

        private static string Bytes(long? value)
        {
            return value.HasValue ? value.Value.ToString("N0", CultureInfo.CurrentCulture) + " B" : "Unavailable";
        }
    }
}
