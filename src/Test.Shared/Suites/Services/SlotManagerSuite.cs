namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Armada.Core.Services.Interfaces;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="SlotManager"/>: chronological slot-name composition, atomic pointer
    /// read/write, newest-first enumeration, retention-based pruning that never removes the active slot, and the
    /// rollback contract (the slot a rebuild replaced survives pruning so RollbackAsync can relaunch it).
    /// These underpin the Admiral self-rebuild A/B slot layout (docs/SERVER_REBUILD.md).
    /// </summary>
    public sealed class SlotManagerSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the SlotManager suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("compose_slot_name_is_chronological", "Slot names sort chronologically", TestTags.Positive, () =>
            {
                SlotManager manager = new SlotManager(NewTempDir());
                string earlier = manager.ComposeSlotName(new DateTime(2026, 9, 11, 10, 0, 0, DateTimeKind.Utc), "abc123def456");
                string later = manager.ComposeSlotName(new DateTime(2026, 9, 11, 12, 0, 0, DateTimeKind.Utc), "999888777666");
                AssertTrue(String.CompareOrdinal(earlier, later) < 0, "Expected the earlier build to sort before the later build.");
                AssertContains("abc123def456", earlier);
                return Task.CompletedTask;
            }));

            cases.Add(Case("write_then_read_current_pointer", "Pointer write is readable and atomic", TestTags.Positive, async () =>
            {
                string binRoot = NewTempDir();
                try
                {
                    SlotManager manager = new SlotManager(binRoot);
                    AssertNull(await manager.ReadCurrentAsync().ConfigureAwait(false), "Expected no pointer initially.");
                    await manager.WriteCurrentAsync("2026-09-11_120000_abc123def456").ConfigureAwait(false);
                    AssertEqual("2026-09-11_120000_abc123def456", await manager.ReadCurrentAsync().ConfigureAwait(false));
                    AssertTrue(File.Exists(manager.CurrentPointerPath), "Expected the pointer file to exist.");
                }
                finally { TryDeleteDir(binRoot); }
            }));

            cases.Add(Case("rollback_target_survives_prune_at_retention_one", "Rebuild prune keeps the rollback target at retention 1", TestTags.Reliability, async () =>
            {
                string binRoot = NewTempDir();
                try
                {
                    SlotManager manager = new SlotManager(binRoot, executableName: "Armada.Server", retentionCount: 1);
                    string older = "2026-09-01_100000_aaaaaaaaaaaa";
                    string previous = "2026-09-02_100000_bbbbbbbbbbbb";
                    string rebuilt = "2026-09-03_100000_cccccccccccc";
                    foreach (string slot in new string[] { older, previous, rebuilt })
                    {
                        Directory.CreateDirectory(manager.GetSlotDirectory(slot));
                        File.WriteAllText(manager.GetSlotExecutablePath(slot), "exe");
                    }

                    // Cutover: the rebuild flips the pointer to the new slot, then prunes, protecting the slot it replaced.
                    await manager.WriteCurrentAsync(rebuilt).ConfigureAwait(false);
                    int removed = await manager.PruneAsync(new List<string> { previous }).ConfigureAwait(false);
                    AssertEqual(1, removed, "only the slot older than the rollback target goes");
                    AssertTrue(File.Exists(manager.GetSlotExecutablePath(previous)), "rollback target must survive");
                    AssertTrue(Directory.Exists(manager.GetSlotDirectory(rebuilt)), "active slot must survive");
                    AssertFalse(Directory.Exists(manager.GetSlotDirectory(older)), "older slot pruned");

                    // Rollback: the pointer flips back and the previous executable is still there to launch.
                    await manager.WriteCurrentAsync(previous).ConfigureAwait(false);
                    AssertEqual(previous, await manager.ReadCurrentAsync().ConfigureAwait(false));
                    AssertTrue(File.Exists(manager.GetSlotExecutablePath(previous)));

                    // A later prune after rollback keeps the active (rolled-back) slot and the newest one.
                    AssertEqual(0, await manager.PruneAsync().ConfigureAwait(false));
                    AssertTrue(Directory.Exists(manager.GetSlotDirectory(previous)) && Directory.Exists(manager.GetSlotDirectory(rebuilt)));
                }
                finally { TryDeleteDir(binRoot); }
            }));

            cases.Add(Case("plain_prune_at_retention_one_drops_previous", "Without protection, retention 1 deletes the previous slot (why rebuild passes it)", TestTags.Negative, async () =>
            {
                string binRoot = NewTempDir();
                try
                {
                    SlotManager manager = new SlotManager(binRoot, retentionCount: 1);
                    string previous = "2026-09-02_100000_bbbbbbbbbbbb";
                    string rebuilt = "2026-09-03_100000_cccccccccccc";
                    Directory.CreateDirectory(manager.GetSlotDirectory(previous));
                    Directory.CreateDirectory(manager.GetSlotDirectory(rebuilt));
                    await manager.WriteCurrentAsync(rebuilt).ConfigureAwait(false);
                    AssertEqual(1, await manager.PruneAsync().ConfigureAwait(false));
                    AssertFalse(Directory.Exists(manager.GetSlotDirectory(previous)));
                }
                finally { TryDeleteDir(binRoot); }
            }));

            cases.Add(Case("concurrent_pointer_writes_never_tear", "Concurrent pointer writes leave a complete name and no temp files", TestTags.Reliability, async () =>
            {
                string binRoot = NewTempDir();
                try
                {
                    SlotManager manager = new SlotManager(binRoot);
                    List<string> names = new List<string>();
                    List<Task> writes = new List<Task>();
                    for (int i = 0; i < 24; i++)
                    {
                        string name = "2026-09-" + (10 + i).ToString("00") + "_100000_" + i.ToString("000000000000");
                        names.Add(name);
                        writes.Add(Task.Run(async () =>
                        {
                            for (int attempt = 0; attempt < 5; attempt++)
                            {
                                try
                                {
                                    await manager.WriteCurrentAsync(name).ConfigureAwait(false);
                                    return;
                                }
                                catch (IOException)
                                {
                                    // Windows can refuse a replace while another writer holds the target; retry.
                                    await Task.Delay(10).ConfigureAwait(false);
                                }
                                catch (UnauthorizedAccessException)
                                {
                                    await Task.Delay(10).ConfigureAwait(false);
                                }
                            }
                        }));
                    }
                    await Task.WhenAll(writes).ConfigureAwait(false);

                    string? current = await manager.ReadCurrentAsync().ConfigureAwait(false);
                    AssertNotNull(current, "pointer present");
                    AssertTrue(names.Contains(current!), "pointer holds one complete slot name, got: " + current);
                    AssertEqual(0, Directory.GetFiles(binRoot, "current.tmp-*").Length, "no temp pointer files left behind");
                }
                finally { TryDeleteDir(binRoot); }
            }));

            cases.Add(Case("prune_keeps_newest_and_active", "Prune keeps newest N and never the active slot", TestTags.Positive, async () =>
            {
                string binRoot = NewTempDir();
                try
                {
                    SlotManager manager = new SlotManager(binRoot, retentionCount: 2);
                    string[] slots = new string[]
                    {
                        "2026-09-01_100000_aaaaaaaaaaaa",
                        "2026-09-02_100000_bbbbbbbbbbbb",
                        "2026-09-03_100000_cccccccccccc",
                        "2026-09-04_100000_dddddddddddd"
                    };
                    foreach (string slot in slots) Directory.CreateDirectory(manager.GetSlotDirectory(slot));

                    // Make the OLDEST slot the active one so pruning must skip it despite being out of the window.
                    await manager.WriteCurrentAsync(slots[0]).ConfigureAwait(false);

                    int removed = await manager.PruneAsync().ConfigureAwait(false);
                    AssertTrue(removed >= 1, "Expected at least one slot to be pruned.");

                    AssertTrue(Directory.Exists(manager.GetSlotDirectory(slots[3])), "Newest slot must survive.");
                    AssertTrue(Directory.Exists(manager.GetSlotDirectory(slots[2])), "Second-newest slot must survive.");
                    AssertTrue(Directory.Exists(manager.GetSlotDirectory(slots[0])), "Active slot must survive even when outside the retention window.");
                }
                finally { TryDeleteDir(binRoot); }
            }));

            cases.Add(Case("enumerate_newest_first", "Enumerate returns slots newest first", TestTags.Positive, async () =>
            {
                string binRoot = NewTempDir();
                try
                {
                    SlotManager manager = new SlotManager(binRoot);
                    Directory.CreateDirectory(manager.GetSlotDirectory("2026-09-01_100000_aaaaaaaaaaaa"));
                    Directory.CreateDirectory(manager.GetSlotDirectory("2026-09-03_100000_cccccccccccc"));
                    Directory.CreateDirectory(manager.GetSlotDirectory("2026-09-02_100000_bbbbbbbbbbbb"));

                    IReadOnlyList<string> ordered = await manager.EnumerateSlotsAsync().ConfigureAwait(false);
                    AssertEqual(3, ordered.Count);
                    AssertEqual("2026-09-03_100000_cccccccccccc", ordered[0]);
                    AssertEqual("2026-09-01_100000_aaaaaaaaaaaa", ordered[2]);
                }
                finally { TryDeleteDir(binRoot); }
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.SlotManager",
                displayName: "Slot Manager",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static string NewTempDir()
        {
            string dir = Path.Combine(Path.GetTempPath(), "armada-slots-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void TryDeleteDir(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); }
            catch { }
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Func<Task> body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.SlotManager",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => body(),
                tags: new List<string> { tag });
        }

        #endregion
    }
}
