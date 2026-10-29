namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the black box section of a backup manifest and restore report from the black box index. Thread-safe.
    /// </summary>
    public static class BlackBoxBackupInventoryBuilder
    {
        /// <summary>
        /// Build the inventory.
        /// </summary>
        /// <param name="database">Database driver.</param>
        /// <param name="maxListed">Most external entries to list (1 to 10000).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The inventory.</returns>
        /// <exception cref="ArgumentNullException">Thrown when database is null.</exception>
        public static async Task<BlackBoxBackupInventory> BuildAsync(DatabaseDriver database, int maxListed = BlackBoxBackupInventory.MaxListedEntries, CancellationToken token = default)
        {
            if (database == null) throw new ArgumentNullException(nameof(database));
            int cap = Math.Clamp(maxListed, 1, BlackBoxBackupInventory.MaxListedEntries);
            BlackBoxBackupInventory inventory = new BlackBoxBackupInventory();
            List<BlackBoxStorageTargetUsage> usage = await database.BlackBoxEntries.SumBytesByTargetAsync(null, token).ConfigureAwait(false);
            foreach (BlackBoxStorageTargetUsage row in usage)
            {
                if (row.Target == BlackBoxStorageTargetEnum.Database)
                {
                    inventory.DatabaseEntries += row.Entries;
                    inventory.DatabaseBytes += row.Bytes;
                }
                else
                {
                    inventory.ExternalEntries += row.Entries;
                    if (inventory.ExternalTargets.Count < 7) inventory.ExternalTargets.Add(row);
                }
            }

            string? cursor = null;
            while (inventory.ExternalEntryRefs.Count < cap)
            {
                EnumerationResult<BlackBoxEntry> page = await database.BlackBoxEntries.ScanAsync(new BlackBoxScanQuery
                {
                    AfterId = cursor,
                    StorageTarget = BlackBoxStorageTargetEnum.Database,
                    ExcludeStorageTarget = true,
                    PageSize = Math.Min(500, cap - inventory.ExternalEntryRefs.Count)
                }, token).ConfigureAwait(false);
                if (page.Objects.Count == 0) break;
                foreach (BlackBoxEntry entry in page.Objects)
                {
                    inventory.ExternalEntryRefs.Add(new BlackBoxExternalEntryRef
                    {
                        EntryId = entry.Id,
                        Target = entry.StorageTarget,
                        StoragePrefix = entry.StoragePrefix,
                        ObjectCount = entry.ObjectCount,
                        TotalBytes = entry.TotalBytes
                    });
                }
                cursor = page.Objects[page.Objects.Count - 1].Id;
                if (page.TotalRecords <= page.Objects.Count) break;
            }
            inventory.ExternalEntryRefsTruncated = inventory.ExternalEntries > inventory.ExternalEntryRefs.Count;
            inventory.Message = inventory.ExternalEntries == 0
                ? inventory.DatabaseEntries + " black box entr" + (inventory.DatabaseEntries == 1 ? "y is" : "ies are") + " stored in the database and included."
                : inventory.DatabaseEntries + " black box entr" + (inventory.DatabaseEntries == 1 ? "y is" : "ies are") + " in the database and included; "
                    + inventory.ExternalEntries + " entr" + (inventory.ExternalEntries == 1 ? "y points" : "ies point") + " at objects in "
                    + String.Join(", ", inventory.ExternalTargets.Select(t => t.Target.ToString())) + " that this backup does not contain (back those stores up separately).";
            return inventory;
        }
    }
}
