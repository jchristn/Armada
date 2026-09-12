namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Filesystem implementation of <see cref="ISlotManager"/>. Owns the <c>&lt;binRoot&gt;/slots</c> directory and
    /// the <c>&lt;binRoot&gt;/current</c> pointer file used by the Admiral self-rebuild feature. Pure file
    /// operations only, so it is unit-testable without a server. See docs/SERVER_REBUILD.md.
    /// </summary>
    public class SlotManager : ISlotManager
    {
        #region Public-Members

        /// <inheritdoc />
        public int RetentionCount
        {
            get => _RetentionCount;
            set => _RetentionCount = value < 1 ? 1 : value;
        }

        /// <inheritdoc />
        public string SlotsRoot => _SlotsRoot;

        /// <inheritdoc />
        public string CurrentPointerPath => _CurrentPointerPath;

        #endregion

        #region Private-Members

        private readonly string _BinRoot;
        private readonly string _SlotsRoot;
        private readonly string _CurrentPointerPath;
        private readonly string _ExecutableName;
        private int _RetentionCount = 3;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="binRoot">The bin root directory that holds the <c>slots</c> folder and the
        /// <c>current</c> pointer (for the native Windows install this is <c>%USERPROFILE%\.armada\bin</c>).
        /// Required.</param>
        /// <param name="executableName">Name of the published server executable within a slot; defaults to
        /// <c>Armada.Server.exe</c>.</param>
        /// <param name="retentionCount">Number of slots to retain when pruning; clamped to a minimum of 1;
        /// defaults to 3.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="binRoot"/> is null or empty.</exception>
        public SlotManager(string binRoot, string executableName = "Armada.Server.exe", int retentionCount = 3)
        {
            if (String.IsNullOrWhiteSpace(binRoot)) throw new ArgumentNullException(nameof(binRoot));

            _BinRoot = binRoot;
            _SlotsRoot = Path.Combine(_BinRoot, "slots");
            _CurrentPointerPath = Path.Combine(_BinRoot, "current");
            _ExecutableName = String.IsNullOrWhiteSpace(executableName) ? "Armada.Server.exe" : executableName;
            RetentionCount = retentionCount;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public string ComposeSlotName(DateTime timestampUtc, string? commitSha)
        {
            string shortSha = String.IsNullOrWhiteSpace(commitSha)
                ? "nosha"
                : new string(commitSha.Trim().Take(12).Where(Uri.IsHexDigit).ToArray());
            if (String.IsNullOrEmpty(shortSha)) shortSha = "nosha";
            return timestampUtc.ToString("yyyy-MM-dd_HHmmss") + "_" + shortSha;
        }

        /// <inheritdoc />
        public string GetSlotDirectory(string slotName)
        {
            if (String.IsNullOrWhiteSpace(slotName)) throw new ArgumentNullException(nameof(slotName));
            return Path.Combine(_SlotsRoot, slotName);
        }

        /// <inheritdoc />
        public string GetSlotExecutablePath(string slotName)
        {
            return Path.Combine(GetSlotDirectory(slotName), _ExecutableName);
        }

        /// <inheritdoc />
        public async Task<string?> ReadCurrentAsync(CancellationToken token = default)
        {
            if (!File.Exists(_CurrentPointerPath)) return null;
            string content = await File.ReadAllTextAsync(_CurrentPointerPath, token).ConfigureAwait(false);
            content = content.Trim();
            return String.IsNullOrEmpty(content) ? null : content;
        }

        /// <inheritdoc />
        public async Task WriteCurrentAsync(string slotName, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(slotName)) throw new ArgumentNullException(nameof(slotName));

            Directory.CreateDirectory(_BinRoot);

            // Write to a temp file first, then atomically replace the pointer so a reader never observes a
            // half-written pointer.
            string tempPath = _CurrentPointerPath + ".tmp-" + Guid.NewGuid().ToString("N");
            await File.WriteAllTextAsync(tempPath, slotName.Trim(), token).ConfigureAwait(false);

            try
            {
                File.Move(tempPath, _CurrentPointerPath, true);
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        /// <inheritdoc />
        public IEnumerable<string> EnumerateSlots()
        {
            if (!Directory.Exists(_SlotsRoot)) return Array.Empty<string>();

            return Directory.GetDirectories(_SlotsRoot)
                .Select(Path.GetFileName)
                .Where(name => !String.IsNullOrEmpty(name))
                .Select(name => name!)
                .OrderByDescending(name => name, StringComparer.Ordinal)
                .ToList();
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> EnumerateSlotsAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            IReadOnlyList<string> slots = EnumerateSlots().ToList();
            return Task.FromResult(slots);
        }

        /// <inheritdoc />
        public async Task<int> PruneAsync(CancellationToken token = default)
        {
            List<string> slots = EnumerateSlots().ToList();
            if (slots.Count <= RetentionCount) return 0;

            string? active = await ReadCurrentAsync(token).ConfigureAwait(false);

            int removed = 0;
            for (int i = RetentionCount; i < slots.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                string slotName = slots[i];
                if (!String.IsNullOrEmpty(active) && String.Equals(slotName, active, StringComparison.Ordinal))
                    continue; // never remove the active slot

                string dir = GetSlotDirectory(slotName);
                if (TryDeleteDirectory(dir)) removed++;
            }

            return removed;
        }

        #endregion

        #region Private-Methods

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static bool TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
