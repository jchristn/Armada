namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// Vessel import wording, ported from the dashboard's <c>lib/vesselImportLabels.ts</c>: candidate statuses, item
    /// outcomes, batch and categorization statuses (label and description), outcome reasons, discovery hints, and
    /// import error codes, plus which candidates can be imported and whether a batch is still busy.
    /// </summary>
    public static class ImportText
    {
        #region Public-Members

        /// <summary>
        /// Candidate statuses in display order.
        /// </summary>
        public static readonly VesselImportCandidateStatusEnum[] CandidateStatuses = new VesselImportCandidateStatusEnum[]
        {
            VesselImportCandidateStatusEnum.New, VesselImportCandidateStatusEnum.AlreadyOnboarded, VesselImportCandidateStatusEnum.Worktree,
            VesselImportCandidateStatusEnum.ArmadaManaged, VesselImportCandidateStatusEnum.NotFound, VesselImportCandidateStatusEnum.NotGit,
            VesselImportCandidateStatusEnum.AccessDenied,
        };

        /// <summary>
        /// Outcomes in display order.
        /// </summary>
        public static readonly VesselImportOutcomeEnum[] Outcomes = new VesselImportOutcomeEnum[]
        {
            VesselImportOutcomeEnum.Pending, VesselImportOutcomeEnum.Created, VesselImportOutcomeEnum.SkippedExisting,
            VesselImportOutcomeEnum.SkippedNotSelected, VesselImportOutcomeEnum.Failed,
        };

        /// <summary>
        /// Batch statuses in display order.
        /// </summary>
        public static readonly VesselImportBatchStatusEnum[] BatchStatuses = new VesselImportBatchStatusEnum[]
        {
            VesselImportBatchStatusEnum.Discovering, VesselImportBatchStatusEnum.Discovered, VesselImportBatchStatusEnum.Importing,
            VesselImportBatchStatusEnum.Completed, VesselImportBatchStatusEnum.CompletedWithFailures, VesselImportBatchStatusEnum.Failed,
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// True for candidate statuses the import creates a vessel for when selected (New and Worktree).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>True when importable.</returns>
        public static bool IsImportable(VesselImportCandidateStatusEnum status)
        {
            return status == VesselImportCandidateStatusEnum.New || status == VesselImportCandidateStatusEnum.Worktree;
        }

        /// <summary>
        /// True while discovery, the import, or fleet categorization of a batch runs.
        /// </summary>
        /// <param name="batch">Batch.</param>
        /// <returns>True when busy.</returns>
        public static bool IsBusy(VesselImportBatch? batch)
        {
            if (batch == null) return false;
            return batch.Status == VesselImportBatchStatusEnum.Discovering || batch.Status == VesselImportBatchStatusEnum.Importing || IsCategorizing(batch);
        }

        /// <summary>
        /// True while fleet categorization is queued or running.
        /// </summary>
        /// <param name="batch">Batch.</param>
        /// <returns>True when categorizing.</returns>
        public static bool IsCategorizing(VesselImportBatch? batch)
        {
            return batch != null && (batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Pending || batch.CategorizationStatus == VesselImportCategorizationStatusEnum.Running);
        }

        /// <summary>
        /// English label of a candidate status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Label.</returns>
        public static string CandidateLabel(VesselImportCandidateStatusEnum status)
        {
            switch (status)
            {
                case VesselImportCandidateStatusEnum.New: return "New";
                case VesselImportCandidateStatusEnum.AlreadyOnboarded: return "Already a vessel";
                case VesselImportCandidateStatusEnum.Worktree: return "Worktree";
                case VesselImportCandidateStatusEnum.ArmadaManaged: return "Managed by Armada";
                case VesselImportCandidateStatusEnum.NotFound: return "Not found";
                case VesselImportCandidateStatusEnum.NotGit: return "Not a repository";
                default: return "Access denied";
            }
        }

        /// <summary>
        /// English description of a candidate status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Description.</returns>
        public static string CandidateDescription(VesselImportCandidateStatusEnum status)
        {
            switch (status)
            {
                case VesselImportCandidateStatusEnum.New: return "A git repository that is not a vessel yet. Selected by default.";
                case VesselImportCandidateStatusEnum.AlreadyOnboarded: return "A vessel with this working directory or remote already exists.";
                case VesselImportCandidateStatusEnum.Worktree: return "The .git entry is a file, so this is a worktree or submodule. Not selected by default.";
                case VesselImportCandidateStatusEnum.ArmadaManaged: return "Armada repos, docks, or data directory. Never imported.";
                case VesselImportCandidateStatusEnum.NotFound: return "The path does not exist on the Admiral host.";
                case VesselImportCandidateStatusEnum.NotGit: return "The directory exists but contains no git repository within the search depth.";
                default: return "The directory could not be read by the Admiral.";
            }
        }

        /// <summary>
        /// Style for a candidate status (success, info, warning, failed, or muted).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="theme">Theme.</param>
        /// <returns>Style.</returns>
        public static CellStyle CandidateStyle(VesselImportCandidateStatusEnum status, ArmadaTheme theme)
        {
            switch (status)
            {
                case VesselImportCandidateStatusEnum.New: return theme.Success;
                case VesselImportCandidateStatusEnum.AlreadyOnboarded: return theme.Info;
                case VesselImportCandidateStatusEnum.Worktree: return theme.Warning;
                case VesselImportCandidateStatusEnum.NotFound:
                case VesselImportCandidateStatusEnum.AccessDenied: return theme.Error;
                default: return theme.Muted;
            }
        }

        /// <summary>
        /// English label of an outcome.
        /// </summary>
        /// <param name="outcome">Outcome.</param>
        /// <returns>Label.</returns>
        public static string OutcomeLabel(VesselImportOutcomeEnum outcome)
        {
            switch (outcome)
            {
                case VesselImportOutcomeEnum.Pending: return "Pending";
                case VesselImportOutcomeEnum.Created: return "Created";
                case VesselImportOutcomeEnum.SkippedExisting: return "Skipped (exists)";
                case VesselImportOutcomeEnum.SkippedNotSelected: return "Not selected";
                default: return "Failed";
            }
        }

        /// <summary>
        /// Style for an outcome.
        /// </summary>
        /// <param name="outcome">Outcome.</param>
        /// <param name="theme">Theme.</param>
        /// <returns>Style.</returns>
        public static CellStyle OutcomeStyle(VesselImportOutcomeEnum outcome, ArmadaTheme theme)
        {
            switch (outcome)
            {
                case VesselImportOutcomeEnum.Created: return theme.Success;
                case VesselImportOutcomeEnum.SkippedExisting: return theme.Info;
                case VesselImportOutcomeEnum.Failed: return theme.Error;
                default: return theme.Muted;
            }
        }

        /// <summary>
        /// English label of a batch status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Label.</returns>
        public static string BatchLabel(VesselImportBatchStatusEnum status)
        {
            switch (status)
            {
                case VesselImportBatchStatusEnum.Discovering: return "Discovering";
                case VesselImportBatchStatusEnum.Discovered: return "Discovered";
                case VesselImportBatchStatusEnum.Importing: return "Importing";
                case VesselImportBatchStatusEnum.Completed: return "Completed";
                case VesselImportBatchStatusEnum.CompletedWithFailures: return "Completed with failures";
                default: return "Failed";
            }
        }

        /// <summary>
        /// Style for a batch status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="theme">Theme.</param>
        /// <returns>Style.</returns>
        public static CellStyle BatchStyle(VesselImportBatchStatusEnum status, ArmadaTheme theme)
        {
            switch (status)
            {
                case VesselImportBatchStatusEnum.Completed: return theme.Success;
                case VesselImportBatchStatusEnum.CompletedWithFailures: return theme.Warning;
                case VesselImportBatchStatusEnum.Failed: return theme.Error;
                case VesselImportBatchStatusEnum.Discovered: return theme.Text;
                default: return theme.Info;
            }
        }

        /// <summary>
        /// English label of a categorization status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Label.</returns>
        public static string CategorizationLabel(VesselImportCategorizationStatusEnum status)
        {
            switch (status)
            {
                case VesselImportCategorizationStatusEnum.Pending: return "Fleet recommendations queued";
                case VesselImportCategorizationStatusEnum.Running: return "Recommending fleets";
                case VesselImportCategorizationStatusEnum.Completed: return "Fleets recommended";
                case VesselImportCategorizationStatusEnum.Failed: return "Fleet recommendation failed";
                case VesselImportCategorizationStatusEnum.Applied: return "Fleets applied";
                default: return "No fleet recommendations";
            }
        }

        /// <summary>
        /// English label of an outcome reason code, or the code itself.
        /// </summary>
        /// <param name="code">Reason code.</param>
        /// <returns>Label, or empty.</returns>
        public static string ReasonLabel(string? code)
        {
            if (String.IsNullOrEmpty(code)) return "";
            switch (code)
            {
                case "VesselAlreadyExists": return "A matching vessel already exists";
                case "NotSelected": return "Not selected for import";
                case "NotImportable": return "This candidate cannot be imported";
                case "PathMissing": return "The directory no longer exists";
                case "CreateFailed": return "Vessel creation failed";
                case "Cancelled": return "The import was cancelled first";
                default: return code!;
            }
        }

        /// <summary>
        /// English explanation of a discovery hint, or the fallback.
        /// </summary>
        /// <param name="code">Hint code.</param>
        /// <param name="fallback">Server message.</param>
        /// <returns>Explanation.</returns>
        public static string HintLabel(string? code, string? fallback)
        {
            switch (code)
            {
                case "PathNotVisibleToAdmiral": return "None of the requested paths exist on the Admiral host. If the Admiral runs in a container it cannot see your host directories: mount them into the container, or run discovery through a Harbor on that machine.";
                case "CandidateLimitReached": return "Discovery stopped at the candidate limit, so the list is truncated. Narrow the roots or lower the max depth and discover again to see the rest.";
                default: return fallback ?? "";
            }
        }

        /// <summary>
        /// English explanation of an import error code, or null when unknown.
        /// </summary>
        /// <param name="code">Error code.</param>
        /// <returns>Explanation or null.</returns>
        public static string? ErrorLabel(string? code)
        {
            switch (code)
            {
                case "InvalidRequest": return "The request was not valid. Check the paths and try again.";
                case "HarborNotSupported": return "Discovery through a Harbor is not supported yet.";
                case "PathNotAllowed": return "This path is outside the allowed import roots. An administrator can add roots under Settings > Import.";
                case "DirectoryNotFound": return "That directory does not exist on the Admiral host.";
                case "BatchNotFound": return "This import batch no longer exists. Run discovery again.";
                case "BatchBusy": return "This batch is already being imported. Wait for it to finish or open it from the import history.";
                default: return null;
            }
        }

        /// <summary>
        /// Split pasted text into trimmed, unquoted, non-empty, de-duplicated lines (<c>parsePastedPaths</c>).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>Paths.</returns>
        public static List<string> ParsePaths(string? text)
        {
            List<string> result = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length > 0 && (line[0] == '"' || line[0] == '\'')) line = line.Substring(1);
                if (line.Length > 0 && (line[line.Length - 1] == '"' || line[line.Length - 1] == '\'')) line = line.Substring(0, line.Length - 1);
                if (line.Length == 0 || seen.Contains(line)) continue;
                seen.Add(line);
                result.Add(line);
            }

            return result;
        }

        #endregion
    }
}
