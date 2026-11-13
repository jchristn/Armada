namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Directory roll-up of the conflicted files (including any found during enrichment): counts at depths 1 to 3
    /// (Conflict.ConflictedDirectories, largest first, deepest on a tie), each file's parent directory with counts
    /// (Conflict.ConflictedParentDirectories, any depth), and the deepest directory holding at least two conflicted files.
    /// </summary>
    public class BlackBoxDirectoryRollupEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "DirectoryRollup";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when logging is null.</exception>
        public BlackBoxDirectoryRollupEnricher(LoggingModule logging) : base(logging)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Roll files up to directories at depths 1 to 3, largest first, deepest on a tie, then by name.
        /// </summary>
        /// <param name="files">Repository-relative files.</param>
        /// <returns>The directory counts.</returns>
        public static List<BlackBoxDirectoryCount> RollUp(IEnumerable<string> files)
        {
            Dictionary<string, BlackBoxDirectoryCount> dirs = new Dictionary<string, BlackBoxDirectoryCount>(StringComparer.Ordinal);
            foreach (string file in Distinct(files))
            {
                string[] parts = file.Split('/');
                for (int depth = 1; depth <= 3 && depth < parts.Length; depth++) Count(dirs, String.Join("/", parts.Take(depth)), depth);
            }
            return Order(dirs.Values);
        }

        /// <summary>
        /// Each file's parent directory with counts (any depth; the root is the empty string), largest first.
        /// </summary>
        /// <param name="files">Repository-relative files.</param>
        /// <returns>The parent directory counts.</returns>
        public static List<BlackBoxDirectoryCount> Parents(IEnumerable<string> files)
        {
            Dictionary<string, BlackBoxDirectoryCount> dirs = new Dictionary<string, BlackBoxDirectoryCount>(StringComparer.Ordinal);
            foreach (string file in Distinct(files))
            {
                string parent = BlackBoxForensics.DirectoryOf(file);
                Count(dirs, parent, parent.Length == 0 ? 0 : parent.Split('/').Length);
            }
            return Order(dirs.Values);
        }

        /// <summary>
        /// The deepest directory (any depth) holding at least two of the files, more files first on a tie, or null.
        /// </summary>
        /// <param name="files">Repository-relative files.</param>
        /// <returns>The directory and its count, or null.</returns>
        public static BlackBoxDirectoryCount? Deepest(IEnumerable<string> files)
        {
            Dictionary<string, BlackBoxDirectoryCount> dirs = new Dictionary<string, BlackBoxDirectoryCount>(StringComparer.Ordinal);
            foreach (string file in Distinct(files))
            {
                string[] parts = file.Split('/');
                for (int depth = 1; depth < parts.Length; depth++) Count(dirs, String.Join("/", parts.Take(depth)), depth);
            }
            return dirs.Values.Where(d => d.FileCount >= 2)
                .OrderByDescending(d => d.Depth).ThenByDescending(d => d.FileCount).ThenBy(d => d.Directory, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            BlackBoxConflict? c = context.Record.Conflict;
            if (c == null || c.ConflictedFiles.Count == 0) return Task.CompletedTask;
            List<string> files = c.ConflictedFiles;

            List<BlackBoxDirectoryCount> rollup = RollUp(files);
            if (rollup.Count > BlackBoxLimits.MaxConflictedDirectories)
                BlackBoxRecordBounder.AddField(context.Record.Truncation, "Conflict.ConflictedDirectories", rollup.Count, BlackBoxLimits.MaxConflictedDirectories, "items");
            c.ConflictedDirectories = rollup.Take(BlackBoxLimits.MaxConflictedDirectories).ToList();

            List<BlackBoxDirectoryCount> parents = Parents(files);
            if (parents.Count > BlackBoxLimits.MaxConflictedDirectories)
                BlackBoxRecordBounder.AddField(context.Record.Truncation, "Conflict.ConflictedParentDirectories", parents.Count, BlackBoxLimits.MaxConflictedDirectories, "items");
            c.ConflictedParentDirectories = parents.Take(BlackBoxLimits.MaxConflictedDirectories).ToList();

            BlackBoxDirectoryCount? deepest = Deepest(files);
            c.DeepestConflictedDirectory = deepest?.Directory;
            c.DeepestConflictedDirectoryFileCount = deepest?.FileCount;
            if (c.ConflictedFilesTruncated)
                BlackBoxRecordBounder.AddNote(context.Record.Truncation, "The directory roll-up counts the first " + files.Count + " of " + c.ConflictedFileTotal + " conflicted files.");
            return Task.CompletedTask;
        }

        private static IEnumerable<string> Distinct(IEnumerable<string> files)
        {
            return (files ?? Enumerable.Empty<string>()).Select(BlackBoxForensics.NormalizePath).Where(f => f.Length > 0).Distinct(StringComparer.Ordinal);
        }

        private static void Count(Dictionary<string, BlackBoxDirectoryCount> dirs, string dir, int depth)
        {
            if (!dirs.TryGetValue(dir, out BlackBoxDirectoryCount? count))
            {
                count = new BlackBoxDirectoryCount { Directory = dir, Depth = depth };
                dirs[dir] = count;
            }
            count.FileCount++;
        }

        private static List<BlackBoxDirectoryCount> Order(IEnumerable<BlackBoxDirectoryCount> dirs)
        {
            return dirs.OrderByDescending(d => d.FileCount).ThenByDescending(d => d.Depth).ThenBy(d => d.Directory, StringComparer.Ordinal).ToList();
        }

        #endregion
    }
}
