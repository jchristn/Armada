namespace Armada.Core.Services.Interfaces
{
    using System.Collections.Generic;

    /// <summary>
    /// Manages the A/B "slot" layout used by the Admiral self-rebuild feature. Each rebuild publishes into a
    /// fresh, versioned slot directory under the bin root while the running server keeps serving, then flips a
    /// small text pointer file (<see cref="CurrentPointerPath"/>) to name the active slot. See
    /// docs/SERVER_REBUILD.md.
    /// </summary>
    public interface ISlotManager
    {
        /// <summary>
        /// Number of slots to retain when pruning. Older slots beyond this count are removed after a successful
        /// cutover. Minimum 1; defaults to 3.
        /// </summary>
        int RetentionCount { get; set; }

        /// <summary>
        /// Absolute path to the directory that holds all slots (<c>&lt;binRoot&gt;/slots</c>).
        /// </summary>
        string SlotsRoot { get; }

        /// <summary>
        /// Absolute path to the pointer file naming the active slot (<c>&lt;binRoot&gt;/current</c>).
        /// </summary>
        string CurrentPointerPath { get; }

        /// <summary>
        /// Compose a slot name from a build timestamp and a source commit sha, in the form
        /// <c>yyyy-MM-dd_HHmmss_&lt;shortSha&gt;</c>. The leading timestamp makes plain string sort order match
        /// chronological order, which is what pruning relies on.
        /// </summary>
        /// <param name="timestampUtc">Build time (UTC).</param>
        /// <param name="commitSha">Source commit sha; may be null or empty.</param>
        /// <returns>A filesystem-safe slot name.</returns>
        string ComposeSlotName(System.DateTime timestampUtc, string? commitSha);

        /// <summary>
        /// Absolute path to the directory for a given slot name.
        /// </summary>
        /// <param name="slotName">Slot name.</param>
        /// <returns>Absolute slot directory path.</returns>
        string GetSlotDirectory(string slotName);

        /// <summary>
        /// Absolute path to the published server executable within a slot.
        /// </summary>
        /// <param name="slotName">Slot name.</param>
        /// <returns>Absolute path to the slot's Armada.Server executable.</returns>
        string GetSlotExecutablePath(string slotName);

        /// <summary>
        /// Read the active slot name from the pointer file.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The active slot name, or null when the pointer is absent or empty.</returns>
        Task<string?> ReadCurrentAsync(CancellationToken token = default);

        /// <summary>
        /// Atomically write the active slot name to the pointer file (write to a temp file, then replace).
        /// </summary>
        /// <param name="slotName">Slot name to record. Required.</param>
        /// <param name="token">Cancellation token.</param>
        Task WriteCurrentAsync(string slotName, CancellationToken token = default);

        /// <summary>
        /// Enumerate slot names present on disk, newest first (reverse name sort).
        /// </summary>
        /// <returns>Slot names, newest first.</returns>
        IEnumerable<string> EnumerateSlots();

        /// <summary>
        /// Enumerate slot names present on disk, newest first (reverse name sort).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Slot names, newest first.</returns>
        Task<IReadOnlyList<string>> EnumerateSlotsAsync(CancellationToken token = default);

        /// <summary>
        /// Remove slots beyond <see cref="RetentionCount"/>, keeping the newest. The active slot named by the
        /// pointer is never removed even if it would otherwise fall outside the retention window.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of slots removed.</returns>
        Task<int> PruneAsync(CancellationToken token = default);
    }
}
