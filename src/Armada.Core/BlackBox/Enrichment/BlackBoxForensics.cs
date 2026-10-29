namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Shared helpers of the built-in black box enrichers: resolving the dock, host and repository once per entry, path
    /// normalization, diff parsing, bounded tails, and file and link rows.
    /// </summary>
    public static class BlackBoxForensics
    {
        #region Public-Members

        /// <summary>
        /// Bytes kept from the end of a tail evidence object (agent output, mission log) before the writer's own cap.
        /// </summary>
        public const int TailBytes = 262144;

        /// <summary>
        /// Most changed paths remembered for the failed mission.
        /// </summary>
        public const int MaxMissionPaths = 2000;

        /// <summary>
        /// Most rows per file role an entry gets.
        /// </summary>
        public const int MaxFileRowsPerRole = 200;

        /// <summary>
        /// Name of the Admiral host in host labels.
        /// </summary>
        public const string AdmiralHost = "Admiral";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the entry's dock, owning host, repository, worktree existence, branch, target and start commit once;
        /// later calls return the same state. Never throws for an unreachable host (HostUnavailableReason is set).
        /// </summary>
        /// <param name="context">Enrichment context.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="inspector">Git inspector.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The state.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static async Task<BlackBoxForensicState> EnsureStateAsync(BlackBoxEnrichmentContext context, DatabaseDriver database, VesselGitInspector inspector, CancellationToken token = default)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (inspector == null) throw new ArgumentNullException(nameof(inspector));
            BlackBoxForensicState state = context.Forensics;
            if (state.Resolved) return state;
            state.Resolved = true;

            Mission? mission = context.Mission;
            BlackBoxWorkspace? w = context.Record.Workspace;
            string? dockId = mission?.DockId ?? w?.DockId ?? context.Entry.DockId;
            if (!String.IsNullOrEmpty(dockId)) state.Dock = await database.Docks.ReadAsync(dockId!, token).ConfigureAwait(false);

            state.Branch = FirstNonEmpty(w?.BranchName, mission?.BranchName, state.Dock?.BranchName);
            state.TargetBranch = FirstNonEmpty(w?.TargetBranch, context.Vessel?.DefaultBranch);
            state.StartCommit = FirstNonEmpty(w?.StartCommit, mission?.StartCommit, AnchorStartCommit(state.Dock));

            if (!String.IsNullOrEmpty(mission?.DiffSnapshot))
            {
                foreach (string path in DiffPaths(mission!.DiffSnapshot)) AddMissionPath(state, path);
                state.MissionChangedPathsKnown = true;
            }

            if (context.Vessel == null && state.Dock == null)
            {
                state.HostUnavailableReason = "No vessel or dock is recorded for this entry, so there is no repository to inspect.";
                return state;
            }

            VesselRepositoryState repo = inspector.ResolveRepository(context.Vessel, state.Dock);
            state.Repository = repo;
            state.Host = repo.Host;
            state.HostLabel = repo.HarborId == null ? AdmiralHost : "Harbor:" + repo.HarborId;
            if (repo.Host == null)
            {
                state.HostUnavailableReason = "Harbor " + repo.HarborId + " is not connected; the worktree, branch and target state on it are unknown.";
                return state;
            }

            string? worktree = state.Dock?.WorktreePath;
            if (!String.IsNullOrWhiteSpace(worktree))
            {
                try
                {
                    state.WorktreeExists = await repo.Host.Files.DirectoryExistsAsync(worktree!, token).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException && token.IsCancellationRequested))
                {
                    state.WorktreeExists = null;
                    state.HostUnavailableReason = "The worktree on " + state.HostLabel + " could not be checked (" + e.Message + ").";
                    state.Host = null;
                    return state;
                }
            }

            if (state.WorktreeExists == true) state.GitPath = worktree;
            else if (!String.IsNullOrWhiteSpace(repo.Path) && (repo.HarborId != null || Directory.Exists(repo.Path))) state.GitPath = repo.Path;
            return state;
        }

        /// <summary>
        /// Normalize a repository-relative path: forward slashes, trimmed, no leading slash.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>The normalized path (empty for null).</returns>
        public static string NormalizePath(string? path)
        {
            return (path ?? String.Empty).Trim().Replace('\\', '/').TrimStart('/');
        }

        /// <summary>
        /// The parent directory of a repository-relative path (empty for a top-level file).
        /// </summary>
        /// <param name="path">Normalized path.</param>
        /// <returns>The directory.</returns>
        public static string DirectoryOf(string path)
        {
            int index = (path ?? String.Empty).LastIndexOf('/');
            return index < 0 ? String.Empty : path!.Substring(0, index);
        }

        /// <summary>
        /// The files a unified diff touches (from its "diff --git a/X b/Y" headers, the b side; deletions keep the a side).
        /// </summary>
        /// <param name="diff">Diff text, or null.</param>
        /// <returns>Distinct paths in order.</returns>
        public static List<string> DiffPaths(string? diff)
        {
            List<string> paths = new List<string>();
            foreach (BlackBoxDiffFileStat stat in DiffFileStats(diff)) paths.Add(stat.Path);
            return paths;
        }

        /// <summary>
        /// Per-file insertions and deletions of a unified diff, in order.
        /// </summary>
        /// <param name="diff">Diff text, or null.</param>
        /// <returns>One stat per file.</returns>
        public static List<BlackBoxDiffFileStat> DiffFileStats(string? diff)
        {
            List<BlackBoxDiffFileStat> stats = new List<BlackBoxDiffFileStat>();
            if (String.IsNullOrEmpty(diff)) return stats;
            Dictionary<string, BlackBoxDiffFileStat> byPath = new Dictionary<string, BlackBoxDiffFileStat>(StringComparer.Ordinal);
            BlackBoxDiffFileStat? current = null;
            foreach (string raw in diff!.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    string? path = PathFromDiffHeader(line);
                    current = null;
                    if (String.IsNullOrEmpty(path)) continue;
                    if (!byPath.TryGetValue(path!, out current))
                    {
                        current = new BlackBoxDiffFileStat { Path = path! };
                        byPath[path!] = current;
                        stats.Add(current);
                    }
                    continue;
                }
                if (current == null) continue;
                if (line.StartsWith("+++ ", StringComparison.Ordinal) || line.StartsWith("--- ", StringComparison.Ordinal)) continue;
                if (line.StartsWith("+", StringComparison.Ordinal)) current.Insertions++;
                else if (line.StartsWith("-", StringComparison.Ordinal)) current.Deletions++;
            }
            return stats;
        }

        /// <summary>
        /// The last <paramref name="maxBytes"/> bytes of a UTF-8 text, cut at a line boundary when one is near, with the
        /// original size when it was cut.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="maxBytes">Bytes to keep.</param>
        /// <param name="originalBytes">Original size in bytes.</param>
        /// <returns>The kept bytes.</returns>
        public static byte[] TailUtf8(string text, int maxBytes, out long originalBytes)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? String.Empty);
            originalBytes = bytes.LongLength;
            return TailBytesOf(bytes, maxBytes);
        }

        /// <summary>
        /// The last <paramref name="maxBytes"/> bytes of a file (read without loading the rest), or null when the file
        /// does not exist or cannot be read.
        /// </summary>
        /// <param name="path">File path.</param>
        /// <param name="maxBytes">Bytes to keep.</param>
        /// <param name="originalBytes">File size in bytes.</param>
        /// <returns>The bytes, or null.</returns>
        public static byte[]? TailFile(string path, int maxBytes, out long originalBytes)
        {
            originalBytes = 0;
            try
            {
                if (String.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    originalBytes = stream.Length;
                    long start = Math.Max(0, stream.Length - maxBytes);
                    stream.Seek(start, SeekOrigin.Begin);
                    byte[] buffer = new byte[stream.Length - start];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = stream.Read(buffer, read, buffer.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read < buffer.Length) Array.Resize(ref buffer, read);
                    return start > 0 ? TrimToLineStart(buffer) : buffer;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A text evidence object.
        /// </summary>
        /// <param name="name">Object name.</param>
        /// <param name="text">Text.</param>
        /// <returns>The pending object.</returns>
        public static BlackBoxPendingObject TextObject(string name, string text)
        {
            return new BlackBoxPendingObject { Name = name, ContentType = BlackBoxObjectNames.TextContentType, Data = Encoding.UTF8.GetBytes(text ?? String.Empty), IsText = true };
        }

        /// <summary>
        /// Add or replace a pending object by name.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="obj">Object.</param>
        public static void PutObject(BlackBoxEnrichmentContext context, BlackBoxPendingObject obj)
        {
            if (context == null || obj == null) return;
            context.Objects.RemoveAll(o => o.Name == obj.Name);
            if (context.Objects.Count < BlackBoxLimits.MaxEvidenceItems) context.Objects.Add(obj);
        }

        /// <summary>
        /// Whether the record's evidence index already lists an object.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="name">Object name.</param>
        /// <returns>True when listed.</returns>
        public static bool HasEvidence(BlackBoxRecord record, string name)
        {
            return record?.Evidence != null && record.Evidence.Objects.Any(o => o.Name == name);
        }

        /// <summary>
        /// A file row for the entry.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="path">Path.</param>
        /// <param name="role">Role.</param>
        /// <param name="conflictingMissionId">Conflicting mission, or null.</param>
        /// <returns>The row.</returns>
        public static BlackBoxFile FileRow(BlackBoxEntry entry, string path, BlackBoxFileRoleEnum role, string? conflictingMissionId = null)
        {
            string normalized = NormalizePath(path);
            return new BlackBoxFile
            {
                EntryId = entry.Id,
                TenantId = entry.TenantId,
                VesselId = entry.VesselId,
                Path = BlackBoxLimits.Clip(normalized, BlackBoxLimits.MaxPathChars) ?? String.Empty,
                Directory = BlackBoxLimits.Clip(DirectoryOf(normalized), BlackBoxLimits.MaxPathChars) ?? String.Empty,
                Role = role,
                ConflictingMissionId = conflictingMissionId,
                CreatedUtc = entry.CreatedUtc
            };
        }

        /// <summary>
        /// A link row for the entry.
        /// </summary>
        /// <param name="entry">Entry.</param>
        /// <param name="missionId">Linked mission.</param>
        /// <param name="relation">Relation.</param>
        /// <param name="commitHash">Commit, or null.</param>
        /// <param name="overlapFileCount">Overlapping files.</param>
        /// <returns>The row.</returns>
        public static BlackBoxLink LinkRow(BlackBoxEntry entry, string missionId, BlackBoxLinkRelationEnum relation, string? commitHash = null, int overlapFileCount = 0)
        {
            return new BlackBoxLink
            {
                EntryId = entry.Id,
                TenantId = entry.TenantId,
                MissionId = missionId,
                Relation = relation,
                CommitHash = commitHash,
                OverlapFileCount = overlapFileCount,
                CreatedUtc = entry.CreatedUtc
            };
        }

        /// <summary>
        /// "Name &lt;email&gt;" for a commit author.
        /// </summary>
        /// <param name="commit">Commit.</param>
        /// <returns>The author.</returns>
        public static string AuthorOf(GitCommitInfo commit)
        {
            if (commit == null) return String.Empty;
            if (String.IsNullOrWhiteSpace(commit.AuthorEmail)) return commit.AuthorName;
            return (commit.AuthorName + " <" + commit.AuthorEmail + ">").Trim();
        }

        /// <summary>
        /// Paths that are both in <paramref name="paths"/> and in either set.
        /// </summary>
        /// <param name="paths">Candidate paths.</param>
        /// <param name="first">First set.</param>
        /// <param name="second">Second set.</param>
        /// <returns>Distinct overlapping paths, in candidate order.</returns>
        public static List<string> Overlap(IEnumerable<string> paths, ICollection<string> first, ICollection<string> second)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in paths ?? Enumerable.Empty<string>())
            {
                string p = NormalizePath(raw);
                if (p.Length == 0 || !seen.Add(p)) continue;
                if ((first != null && first.Contains(p)) || (second != null && second.Contains(p))) result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// The first value that is not null or whitespace.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <returns>The value, or null.</returns>
        public static string? FirstNonEmpty(params string?[] values)
        {
            foreach (string? v in values)
            {
                if (!String.IsNullOrWhiteSpace(v)) return v!.Trim();
            }
            return null;
        }

        /// <summary>
        /// Add a path the failed mission changed (bounded).
        /// </summary>
        /// <param name="state">State.</param>
        /// <param name="path">Path.</param>
        public static void AddMissionPath(BlackBoxForensicState state, string path)
        {
            string p = NormalizePath(path);
            if (p.Length == 0 || state.MissionChangedPaths.Count >= MaxMissionPaths) return;
            state.MissionChangedPaths.Add(p);
        }

        /// <summary>
        /// Run a read and return its result, or the fallback when it throws (anything but cancellation): forensic reads
        /// on a host are best effort.
        /// </summary>
        /// <typeparam name="T">Result type.</typeparam>
        /// <param name="call">The read.</param>
        /// <param name="fallback">Value when it fails.</param>
        /// <returns>The result or the fallback.</returns>
        public static async Task<T> TryAsync<T>(Func<Task<T>> call, T fallback)
        {
            if (call == null) return fallback;
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                return fallback;
            }
        }

        #endregion

        #region Private-Methods

        private static string? AnchorStartCommit(Dock? dock)
        {
            if (dock == null || String.IsNullOrWhiteSpace(dock.GitAnchorsJson)) return null;
            try
            {
                GitAnchorsSnapshot? anchors = JsonSerializer.Deserialize<GitAnchorsSnapshot>(dock.GitAnchorsJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return anchors?.StartCommit;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? PathFromDiffHeader(string line)
        {
            // diff --git a/<path> b/<path>; paths with spaces are ambiguous, so prefer the b side after " b/".
            string rest = line.Substring("diff --git ".Length);
            int b = rest.LastIndexOf(" b/", StringComparison.Ordinal);
            if (b >= 0) return NormalizePath(Unquote(rest.Substring(b + 3)));
            if (rest.StartsWith("\"", StringComparison.Ordinal))
            {
                int quoteB = rest.LastIndexOf(" \"b/", StringComparison.Ordinal);
                if (quoteB >= 0) return NormalizePath(Unquote(rest.Substring(quoteB + 1)).Substring(2));
            }
            return null;
        }

        private static string Unquote(string value)
        {
            string v = value.Trim();
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2);
            return v;
        }

        private static byte[] TailBytesOf(byte[] bytes, int maxBytes)
        {
            if (bytes.Length <= maxBytes) return bytes;
            byte[] tail = new byte[maxBytes];
            Array.Copy(bytes, bytes.Length - maxBytes, tail, 0, maxBytes);
            return TrimToLineStart(tail);
        }

        private static byte[] TrimToLineStart(byte[] tail)
        {
            // Start at the first full line when one begins within the first 4 KB; otherwise skip to a UTF-8 boundary.
            int limit = Math.Min(tail.Length, 4096);
            for (int i = 0; i < limit; i++)
            {
                if (tail[i] == (byte)'\n' && i + 1 < tail.Length)
                {
                    byte[] trimmed = new byte[tail.Length - i - 1];
                    Array.Copy(tail, i + 1, trimmed, 0, trimmed.Length);
                    return trimmed;
                }
            }
            int start = 0;
            while (start < tail.Length && (tail[start] & 0xC0) == 0x80) start++;
            if (start == 0) return tail;
            byte[] aligned = new byte[tail.Length - start];
            Array.Copy(tail, start, aligned, 0, aligned.Length);
            return aligned;
        }

        #endregion
    }
}
