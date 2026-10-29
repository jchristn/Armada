namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Models;

    /// <summary>
    /// The files, commits and missions a black box entry names: what a grounded Ask answer may cite.
    /// </summary>
    public sealed class AskBlackBoxFacts
    {
        /// <summary>
        /// Files and directories (repository paths and the Armada source file of the failure site).
        /// </summary>
        public HashSet<string> Files { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Commit hashes (full or abbreviated forms match by prefix).
        /// </summary>
        public HashSet<string> Commits { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Mission ids.
        /// </summary>
        public HashSet<string> Missions { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Collect the facts of a record.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="missionId">The entry's mission, or null.</param>
        /// <returns>The facts.</returns>
        public static AskBlackBoxFacts FromRecord(BlackBoxRecord record, string? missionId)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            AskBlackBoxFacts facts = new AskBlackBoxFacts();
            facts.AddMission(missionId);
            facts.AddMission(record.Subject?.MissionId);
            facts.AddMission(record.Subject?.ParentMissionId);
            facts.AddFile(record.Site?.SourceFile);
            facts.AddCommit(record.Site?.ArmadaCommit);
            if (record.Workspace != null)
            {
                foreach (string path in record.Workspace.DirtyPaths) facts.AddFile(path);
                facts.AddCommit(record.Workspace.BranchTip);
                facts.AddCommit(record.Workspace.MissionCommit);
                facts.AddCommit(record.Workspace.StartCommit);
                facts.AddCommit(record.Workspace.TargetTipAtStart);
                facts.AddCommit(record.Workspace.TargetTipAtFailure);
            }

            if (record.Conflict != null)
            {
                foreach (string path in record.Conflict.ConflictedFiles) facts.AddFile(path);
                foreach (BlackBoxDirectoryCount directory in record.Conflict.ConflictedDirectories) facts.AddFile(directory.Directory);
                foreach (BlackBoxConflictingMission mission in record.Conflict.ConflictingMissions)
                {
                    facts.AddMission(mission.MissionId);
                    facts.AddCommit(mission.CommitHash);
                    foreach (string path in mission.OverlappingFiles) facts.AddFile(path);
                }

                foreach (BlackBoxCommitRef commit in record.Conflict.TargetCommitsSinceStart)
                {
                    facts.AddCommit(commit.CommitHash);
                    facts.AddMission(commit.MissionId);
                }
            }

            if (record.Scope != null)
            {
                foreach (string path in record.Scope.ScopedFiles) facts.AddFile(path);
                foreach (string path in record.Scope.OutOfScopeFiles) facts.AddFile(path);
            }

            if (record.Related != null)
            {
                foreach (BlackBoxSimilarEntry similar in record.Related.SimilarEntries) facts.AddMission(similar.MissionId);
                foreach (string downstream in record.Related.DownstreamMissionIds) facts.AddMission(downstream);
            }

            return facts;
        }

        /// <summary>
        /// Add a file.
        /// </summary>
        /// <param name="path">Path, or null.</param>
        public void AddFile(string? path)
        {
            if (!String.IsNullOrWhiteSpace(path)) Files.Add(path.Trim().Replace('\\', '/'));
        }

        /// <summary>
        /// Add a commit.
        /// </summary>
        /// <param name="hash">Hash, or null.</param>
        public void AddCommit(string? hash)
        {
            if (!String.IsNullOrWhiteSpace(hash)) Commits.Add(hash.Trim());
        }

        /// <summary>
        /// Add a mission.
        /// </summary>
        /// <param name="missionId">Mission id, or null.</param>
        public void AddMission(string? missionId)
        {
            if (!String.IsNullOrWhiteSpace(missionId)) Missions.Add(missionId.Trim());
        }
    }
}
