namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Mission path ownership across a voyage (CONCURRENCY_IMPROVEMENTS.md 10.2 and 10.4): which logical missions can run
    /// alongside a mission and what they own, and whether a mission's changed files stayed inside its owned paths.
    /// </summary>
    public static class MissionOwnership
    {
        #region Private-Members

        private static readonly MissionStatusEnum[] _Active = new[]
        {
            MissionStatusEnum.Pending, MissionStatusEnum.Assigned, MissionStatusEnum.InProgress, MissionStatusEnum.WorkProduced,
            MissionStatusEnum.PullRequestOpen, MissionStatusEnum.Testing, MissionStatusEnum.Review, MissionStatusEnum.Verifying
        };

        private static readonly Regex _DiffHeader = new Regex(@"^diff --git a/(?<a>.+?) b/(?<b>.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        #endregion

        #region Public-Methods

        /// <summary>
        /// Active logical missions of the voyage that can run alongside <paramref name="mission"/>: not its ancestors or
        /// descendants, and not an integration mission waiting for all others. Only those that own paths are returned.
        /// </summary>
        /// <param name="mission">The mission (any stage of its logical mission).</param>
        /// <param name="voyage">All missions of its voyage.</param>
        /// <returns>The concurrent owners, in tree order.</returns>
        /// <exception cref="ArgumentNullException">When an argument is null.</exception>
        public static List<ConcurrentOwnership> ConcurrentOwners(Mission mission, List<Mission> voyage)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (voyage == null) throw new ArgumentNullException(nameof(voyage));
            List<ConcurrentOwnership> owners = new List<ConcurrentOwnership>();
            if (mission.WaitForVoyageWorkers) return owners;
            Dictionary<string, Mission> byId = voyage.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            List<MissionTreeNode> roots = MissionTreeBuilder.Build(voyage.Select(Summarize));
            List<MissionTreeNode> path = new List<MissionTreeNode>();
            if (!FindPath(roots, mission.Id, path)) return owners;
            HashSet<MissionTreeNode> related = new HashSet<MissionTreeNode>(path);
            Stack<MissionTreeNode> below = new Stack<MissionTreeNode>(path[path.Count - 1].Children);
            while (below.Count > 0)
            {
                MissionTreeNode node = below.Pop();
                related.Add(node);
                foreach (MissionTreeNode child in node.Children) below.Push(child);
            }
            foreach (MissionTreeNode node in Flatten(roots))
            {
                if (related.Contains(node)) continue;
                List<Mission> members = new List<Mission>();
                if (byId.TryGetValue(node.Mission.Id, out Mission? root)) members.Add(root);
                foreach (MissionSummary stage in node.Stages) if (byId.TryGetValue(stage.Id, out Mission? m)) members.Add(m);
                if (!members.Any(m => _Active.Contains(m.Status)) || members.Any(m => m.WaitForVoyageWorkers)) continue;
                List<string> owned = members.SelectMany(m => Paths(m.OwnedPathsJson)).Distinct(StringComparer.Ordinal).ToList();
                if (owned.Count == 0) continue;
                owners.Add(new ConcurrentOwnership { MissionId = node.Mission.Id, Title = node.Mission.Title, Paths = owned });
            }
            return owners;
        }

        /// <summary>Files named by a unified diff's <c>diff --git</c> headers (the new path), in order.</summary>
        /// <param name="diff">Unified diff text.</param>
        /// <returns>Distinct changed files.</returns>
        public static List<string> ChangedFiles(string? diff)
        {
            List<string> files = new List<string>();
            if (String.IsNullOrEmpty(diff)) return files;
            foreach (Match match in _DiffHeader.Matches(diff))
            {
                string file = OwnedPathMatcher.Normalize(match.Groups["b"].Value);
                if (file.Length > 0 && !files.Contains(file)) files.Add(file);
            }
            return files;
        }

        /// <summary>
        /// Compare changed files with the mission's owned paths and with the paths of missions running alongside it.
        /// A mission with no owned paths is only checked against the concurrent owners.
        /// </summary>
        /// <param name="mission">The mission.</param>
        /// <param name="voyage">All missions of its voyage.</param>
        /// <param name="changedFiles">Files the mission changed.</param>
        /// <returns>The finding; <see cref="OwnershipFinding.Clean"/> when nothing stood out.</returns>
        /// <exception cref="ArgumentNullException">When an argument is null.</exception>
        public static OwnershipFinding Check(Mission mission, List<Mission> voyage, IEnumerable<string> changedFiles)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (changedFiles == null) throw new ArgumentNullException(nameof(changedFiles));
            OwnershipFinding finding = new OwnershipFinding();
            List<string> owned = Paths(mission.OwnedPathsJson);
            List<ConcurrentOwnership> concurrent = ConcurrentOwners(mission, voyage ?? new List<Mission>());
            foreach (string file in changedFiles)
            {
                if (owned.Count > 0 && !owned.Any(p => OwnedPathMatcher.Overlaps(p, file))) finding.OutsideOwned.Add(file);
                ConcurrentOwnership? other = concurrent.FirstOrDefault(c => c.Paths.Any(p => OwnedPathMatcher.Overlaps(p, file)));
                if (other != null) finding.InConcurrentPaths.Add(file + " (" + other.Title + ")");
            }
            return finding;
        }

        /// <summary>Parse stored owned or read paths (a JSON string array); malformed or empty JSON yields an empty list.</summary>
        /// <param name="pathsJson">Stored JSON.</param>
        /// <returns>Normalized paths.</returns>
        public static List<string> Paths(string? pathsJson)
        {
            if (String.IsNullOrWhiteSpace(pathsJson)) return new List<string>();
            try
            {
                List<string>? paths = System.Text.Json.JsonSerializer.Deserialize<List<string>>(pathsJson);
                return (paths ?? new List<string>()).Select(OwnedPathMatcher.Normalize).Where(p => p.Length > 0).ToList();
            }
            catch (System.Text.Json.JsonException) { return new List<string>(); }
        }

        #endregion

        #region Private-Methods

        private static MissionSummary Summarize(Mission m) => new MissionSummary
        {
            Id = m.Id, Title = m.Title, Status = m.Status, ParentMissionId = m.ParentMissionId, DependsOnMissionId = m.DependsOnMissionId,
            CreatedUtc = m.CreatedUtc, VoyageId = m.VoyageId, VesselId = m.VesselId
        };

        private static bool FindPath(List<MissionTreeNode> nodes, string missionId, List<MissionTreeNode> path)
        {
            foreach (MissionTreeNode node in nodes)
            {
                path.Add(node);
                if (node.Mission.Id == missionId || node.Stages.Any(s => s.Id == missionId)) return true;
                if (FindPath(node.Children, missionId, path)) return true;
                path.RemoveAt(path.Count - 1);
            }
            return false;
        }

        private static IEnumerable<MissionTreeNode> Flatten(List<MissionTreeNode> nodes)
        {
            foreach (MissionTreeNode node in nodes)
            {
                yield return node;
                foreach (MissionTreeNode child in Flatten(node.Children)) yield return child;
            }
        }

        #endregion
    }
}
