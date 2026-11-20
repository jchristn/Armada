namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// Picks the check-run artifacts the current run wrote by comparing a snapshot of the expected artifact files taken
    /// just before the command with the files collected after it. Both lists come from the same host (the Admiral, or the
    /// Harbor that ran the command), so only that host's own size and write time are compared, never one machine's clock
    /// against another's.
    /// </summary>
    internal static class CheckRunArtifactChanges
    {
        /// <summary>
        /// The artifacts in <paramref name="after"/> that did not exist before the run, or whose size or last-write time
        /// changed. A file the host reported without a write time counts as changed only when its size changed.
        /// </summary>
        /// <param name="before">Artifacts found before the command ran.</param>
        /// <param name="after">Artifacts found after the command ran.</param>
        /// <returns>The artifacts written by this run, in the order of <paramref name="after"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when before or after is null.</exception>
        internal static List<CheckRunArtifact> SelectWrittenDuringRun(IEnumerable<CheckRunArtifact> before, IEnumerable<CheckRunArtifact> after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));

            Dictionary<string, CheckRunArtifact> prior = new Dictionary<string, CheckRunArtifact>(StringComparer.Ordinal);
            foreach (CheckRunArtifact artifact in before)
            {
                prior[artifact.Path] = artifact;
            }

            return after.Where(artifact => !prior.TryGetValue(artifact.Path, out CheckRunArtifact? earlier) || Changed(earlier, artifact)).ToList();
        }

        private static bool Changed(CheckRunArtifact earlier, CheckRunArtifact later)
        {
            if (earlier.SizeBytes != later.SizeBytes) return true;
            return earlier.LastWriteUtc != later.LastWriteUtc;
        }
    }
}
