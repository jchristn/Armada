namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// Values for the placeholders black box rule text uses. Each name maps to one field of the record (or a fact derived
    /// from it in <see cref="BlackBoxRuleFacts"/>); <c>config:Setting</c> reads an effective configuration value. An
    /// unknown name or a missing value resolves to null. Pure and thread-safe.
    /// </summary>
    public static class BlackBoxRulePlaceholders
    {
        #region Public-Members

        /// <summary>
        /// Every placeholder name this class resolves (besides <c>config:Setting</c>).
        /// </summary>
        public static IReadOnlyList<string> Names { get; } = new List<string>
        {
            "entryId", "missionId", "voyageId", "vesselId", "vesselName", "dockId", "harborId", "harborName", "captainId",
            "captainName", "captainRuntime", "captainModel", "persona", "title", "branchName", "targetBranch", "checkoutPath",
            "worktreePath", "missionCommit", "startCommit", "dirtyCount", "conflictCount", "conflictedFiles",
            "conflictMission", "conflictMissionTitle", "conflictCommit", "conflictLandedUtc", "conflictOverlapCount",
            "failingCommand", "commandExitCode", "exitCode", "phase", "step", "kind", "reason", "exceptionMessage",
            "gitStderr", "outOfScopeCount", "outOfScopeFiles", "blankCount", "prerequisite", "sourceFile", "method", "site",
            "armadaVersion", "landingMode", "stallThresholdMinutes", "maxMissionRuntimeMinutes", "runtimeFailureKind",
            "cascadeRootMission", "haltReason", "harborLinkState"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve a placeholder.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="name">Placeholder name.</param>
        /// <returns>The value, or null when unknown or missing.</returns>
        public static string? Resolve(BlackBoxRecord record, string name)
        {
            if (record == null || String.IsNullOrEmpty(name)) return null;
            BlackBoxSubject? s = record.Subject;
            BlackBoxWorkspace? w = record.Workspace;
            BlackBoxMechanism? m = record.Mechanism;
            if (name.StartsWith("config:", StringComparison.Ordinal)) return Blank(BlackBoxRuleFacts.Config(record, name.Substring(7))?.Value);
            switch (name)
            {
                case "entryId": return Blank(record.EntryId);
                case "missionId": return Blank(s?.MissionId);
                case "voyageId": return Blank(s?.VoyageId);
                case "vesselId": return Blank(s?.VesselId);
                case "vesselName": return Blank(s?.VesselName);
                case "dockId": return Blank(w?.DockId ?? s?.DockId);
                case "harborId": return Blank(s?.HarborId);
                case "harborName": return Blank(s?.HarborName ?? s?.HarborId);
                case "captainId": return Blank(s?.CaptainId);
                case "captainName": return Blank(s?.CaptainName ?? s?.CaptainId);
                case "captainRuntime": return Blank(s?.CaptainRuntime);
                case "captainModel": return Blank(s?.CaptainModel);
                case "persona": return Blank(s?.Persona);
                case "title": return Blank(s?.Title);
                case "branchName": return Blank(w?.BranchName ?? s?.BranchName);
                case "targetBranch": return Blank(w?.TargetBranch ?? s?.VesselDefaultBranch);
                case "checkoutPath": return Blank(w?.CheckoutPath);
                case "worktreePath": return Blank(w?.WorktreePath);
                case "missionCommit": return Blank(w?.MissionCommit);
                case "startCommit": return Blank(w?.StartCommit);
                case "dirtyCount": return Count(BlackBoxRuleFacts.DirtyCount(record));
                case "conflictCount": return Count(BlackBoxRuleFacts.ConflictCount(record));
                case "conflictedFiles": return List(record.Conflict?.ConflictedFiles, BlackBoxRuleFacts.ConflictCount(record));
                case "conflictMission": return Blank(BlackBoxRuleFacts.LandedConflict(record)?.MissionId);
                case "conflictMissionTitle": return Blank(BlackBoxRuleFacts.LandedConflict(record)?.Title);
                case "conflictCommit": return Blank(BlackBoxRuleFacts.LandedConflict(record)?.CommitHash);
                case "conflictLandedUtc":
                    DateTime? landed = BlackBoxRuleFacts.LandedConflict(record)?.LandedUtc;
                    return landed.HasValue ? landed.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z" : null;
                case "conflictOverlapCount":
                    BlackBoxConflictingMission? cm = BlackBoxRuleFacts.LandedConflict(record);
                    return cm == null ? null : Count(Math.Max(cm.OverlappingFileTotal, cm.OverlappingFiles.Count));
                case "failingCommand": return Blank(m?.FailingCommand);
                case "commandExitCode": return m?.CommandExitCode?.ToString(CultureInfo.InvariantCulture);
                case "exitCode": return m?.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? m?.CommandExitCode?.ToString(CultureInfo.InvariantCulture);
                case "phase": return BlackBoxRuleFacts.Phase(record).ToString();
                case "step": return BlackBoxRuleFacts.Step(record).ToString();
                case "kind": return BlackBoxRuleFacts.Kind(record)?.ToString();
                case "reason": return Blank(BlackBoxLimits.Clip(OneLine(record.Classification?.RawReason), 300));
                case "exceptionMessage": return Blank(BlackBoxLimits.Clip(OneLine(m?.ExceptionMessage), 300));
                case "gitStderr": return Blank(BlackBoxLimits.Clip(OneLine(m?.GitStderrTail), 300));
                case "outOfScopeCount":
                    List<string> entries = BlackBoxRuleFacts.OutOfScopeEntries(record);
                    return entries.Count == 0 ? null : entries.Count.ToString(CultureInfo.InvariantCulture);
                case "outOfScopeFiles":
                    List<string> listed = BlackBoxRuleFacts.OutOfScopeEntries(record);
                    return listed.Count == 0 ? null : String.Join(", ", listed.Take(5).Select(e => e.Length == 0 ? "(blank)" : e)) + (listed.Count > 5 ? " and " + (listed.Count - 5).ToString(CultureInfo.InvariantCulture) + " more" : String.Empty);
                case "blankCount": return BlackBoxRuleFacts.OutOfScopeEntries(record).Count(e => e.Length == 0).ToString(CultureInfo.InvariantCulture);
                case "prerequisite": return Blank(BlackBoxRuleFacts.Prerequisite(record));
                case "sourceFile": return Blank(record.Site?.SourceFile);
                case "method": return Blank(record.Site?.Method);
                case "site":
                    if (record.Site == null || String.IsNullOrEmpty(record.Site.SourceFile)) return null;
                    return record.Site.SourceFile + (String.IsNullOrEmpty(record.Site.Method) ? String.Empty : ":" + record.Site.Method);
                case "armadaVersion": return Blank(record.Site?.ArmadaVersion);
                case "landingMode": return Blank(BlackBoxRuleFacts.Config(record, "LandingMode")?.Value);
                case "stallThresholdMinutes": return Blank(BlackBoxRuleFacts.Config(record, "StallThresholdMinutes")?.Value);
                case "maxMissionRuntimeMinutes": return Blank(BlackBoxRuleFacts.Config(record, "MaxMissionRuntimeMinutes")?.Value);
                case "runtimeFailureKind": return m?.RuntimeFailureKind?.ToString();
                case "cascadeRootMission": return Blank(record.Voyage?.CascadeRootMissionId);
                case "haltReason": return Blank(BlackBoxLimits.Clip(OneLine(record.Voyage?.HaltReason), 300));
                case "harborLinkState": return Blank(w?.HarborLinkState);
                default: return null;
            }
        }

        #endregion

        #region Private-Methods

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        private static string? Count(int value)
        {
            return value > 0 ? value.ToString(CultureInfo.InvariantCulture) : null;
        }

        private static string? OneLine(string? value)
        {
            return value?.Replace("\r", " ").Replace("\n", " ");
        }

        private static string? List(List<string>? values, int total)
        {
            if (values == null || values.Count == 0) return null;
            string shown = String.Join(", ", values.Take(5));
            return total > 5 ? shown + " and " + (total - 5).ToString(CultureInfo.InvariantCulture) + " more" : shown;
        }

        #endregion
    }
}
