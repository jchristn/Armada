namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Tui.Services;

    /// <summary>
    /// The dashboard's fleet action labels and helpers (<c>lib/fleetActionLabels.ts</c> and
    /// <c>lib/fleetActionTemplate.ts</c>): status, kind, and reason labels; template variables with unknown-variable
    /// detection and the client-side preview renderer; duration formatting; and the run progress summary and ASCII
    /// bar. English strings are catalog keys. Thread-safe (stateless).
    /// </summary>
    public static class FleetActionLabels
    {
        #region Public-Members

        /// <summary>
        /// Run statuses in filter order.
        /// </summary>
        public static readonly FleetActionRunStatusEnum[] RunStatuses = new FleetActionRunStatusEnum[]
        {
            FleetActionRunStatusEnum.Pending, FleetActionRunStatusEnum.Running, FleetActionRunStatusEnum.Completed,
            FleetActionRunStatusEnum.CompletedWithFailures, FleetActionRunStatusEnum.Cancelled, FleetActionRunStatusEnum.Failed,
        };

        /// <summary>
        /// Target statuses in filter order.
        /// </summary>
        public static readonly FleetActionTargetStatusEnum[] TargetStatuses = new FleetActionTargetStatusEnum[]
        {
            FleetActionTargetStatusEnum.Pending, FleetActionTargetStatusEnum.Skipped, FleetActionTargetStatusEnum.Running,
            FleetActionTargetStatusEnum.Succeeded, FleetActionTargetStatusEnum.Failed, FleetActionTargetStatusEnum.Cancelled,
            FleetActionTargetStatusEnum.TimedOut, FleetActionTargetStatusEnum.Findings,
        };

        /// <summary>
        /// Mission modes in picker order.
        /// </summary>
        public static readonly MissionModeEnum[] MissionModes = new MissionModeEnum[]
        {
            MissionModeEnum.Implementation, MissionModeEnum.Audit, MissionModeEnum.Research,
        };

        /// <summary>
        /// Target statuses a follow-up run selects by default (the server default: Findings and Failed).
        /// </summary>
        public static readonly FleetActionTargetStatusEnum[] DefaultFollowUpStatuses = new FleetActionTargetStatusEnum[]
        {
            FleetActionTargetStatusEnum.Findings, FleetActionTargetStatusEnum.Failed,
        };

        /// <summary>
        /// Runtime input types the server accepts.
        /// </summary>
        public static readonly string[] InputTypes = new string[] { "string", "semver", "enum", "package" };

        /// <summary>
        /// Template variable names (as written in templates).
        /// </summary>
        public static readonly string[] TemplateVariableNames = new string[]
        {
            "vessel.name", "vessel.id", "vessel.defaultBranch", "vessel.workingDirectory", "vessel.buildCommand", "vessel.ecosystems", "vessel.testCommand", "vessel.lintCommand", "vessel.auditCommand", "vessel.outdatedCommand", "vessel.licensesCommand", "scan.summary", "health.summary",
        };

        /// <summary>
        /// English descriptions of <see cref="TemplateVariableNames"/>, in the same order.
        /// </summary>
        public static readonly string[] TemplateVariableDescriptions = new string[]
        {
            "The vessel name",
            "The vessel ID (vsl_ prefix)",
            "The vessel default branch",
            "The vessel working directory, or empty",
            "The resolved build command (definition-of-done build command, repo script, then ecosystem default); vessels without one are skipped",
            "Detected ecosystems, comma-separated, or empty; rendered on the server",
            "The resolved test command (vessel override, repo script, then ecosystem default); rendered on the server",
            "The resolved lint command (vessel override, repo script, then ecosystem default); rendered on the server",
            "The resolved audit command (vessel override, repo script, ecosystem default, then osv-scanner); rendered on the server",
            "The resolved outdated-dependency report command (ecosystem default); rendered on the server",
            "The resolved license report command (ecosystem default); rendered on the server",
            "The vessel's latest result from the linked scan action (fix actions); rendered on the server",
            "Failing, warning, and unknown health findings; rendered on the server",
        };

        /// <summary>
        /// Largest target set the server accepts in one run.
        /// </summary>
        public const int MaxRunVessels = 500;

        #endregion

        #region Private-Members

        private static readonly Regex _Variable = new Regex(@"\{\{\s*([^{}]*?)\s*\}\}", RegexOptions.Compiled);
        private static readonly string[] _Known = new string[] { "vessel.name", "vessel.id", "vessel.defaultbranch", "vessel.workingdirectory", "vessel.buildcommand", "vessel.ecosystems", "vessel.testcommand", "vessel.lintcommand", "vessel.auditcommand", "vessel.outdatedcommand", "vessel.licensescommand", "scan.summary", "health.summary" };
        // The server's package identifier rule (FleetActionInputValidator): no shell metacharacters.
        private static readonly Regex _Package = new Regex(@"^[A-Za-z0-9@][A-Za-z0-9@/._+:-]{0,199}\z", RegexOptions.CultureInvariant);
        private static readonly Regex _Semver = new Regex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*))*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z", RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// English label of a run status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Label.</returns>
        public static string RunStatusLabel(FleetActionRunStatusEnum status)
        {
            return status == FleetActionRunStatusEnum.CompletedWithFailures ? "Completed with failures" : status.ToString();
        }

        /// <summary>
        /// English label of a target status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Label.</returns>
        public static string TargetStatusLabel(FleetActionTargetStatusEnum status)
        {
            return status == FleetActionTargetStatusEnum.TimedOut ? "Timed out" : status.ToString();
        }

        /// <summary>
        /// Status severity keyword for <see cref="Armada.Tui.Widgets.StatusBadge"/> styling.
        /// </summary>
        /// <param name="status">Run status.</param>
        /// <returns>A status word the badge maps to the right severity.</returns>
        public static string RunTone(FleetActionRunStatusEnum status)
        {
            switch (status)
            {
                case FleetActionRunStatusEnum.Completed: return "succeeded";
                case FleetActionRunStatusEnum.CompletedWithFailures: return "warning";
                case FleetActionRunStatusEnum.Failed: return "failed";
                case FleetActionRunStatusEnum.Cancelled: return "cancelled";
                case FleetActionRunStatusEnum.Running: return "running";
                default: return "pending";
            }
        }

        /// <summary>
        /// Status severity keyword for a target status.
        /// </summary>
        /// <param name="status">Target status.</param>
        /// <returns>Status word.</returns>
        public static string TargetTone(FleetActionTargetStatusEnum status)
        {
            switch (status)
            {
                case FleetActionTargetStatusEnum.Succeeded: return "succeeded";
                case FleetActionTargetStatusEnum.Failed: return "failed";
                case FleetActionTargetStatusEnum.TimedOut: return "failed";
                case FleetActionTargetStatusEnum.Cancelled: return "cancelled";
                case FleetActionTargetStatusEnum.Running: return "running";
                case FleetActionTargetStatusEnum.Skipped: return "info";
                case FleetActionTargetStatusEnum.Findings: return "warning";
                default: return "pending";
            }
        }

        /// <summary>
        /// English kind description.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <returns>Description.</returns>
        public static string KindDescription(FleetActionKindEnum kind)
        {
            return kind == FleetActionKindEnum.Command
                ? "Runs a shell command in each vessel working directory and captures the exit code and output."
                : "Dispatches one voyage per vessel with the rendered prompt as the mission description.";
        }

        /// <summary>
        /// English label of a mission mode.
        /// </summary>
        /// <param name="mode">Mode.</param>
        /// <returns>Label.</returns>
        public static string MissionModeLabel(MissionModeEnum mode)
        {
            switch (mode)
            {
                case MissionModeEnum.Audit: return "Audit (report-only)";
                case MissionModeEnum.Research: return "Research (report-only)";
                default: return "Implementation";
            }
        }

        /// <summary>
        /// English description of a mission mode.
        /// </summary>
        /// <param name="mode">Mode.</param>
        /// <returns>Description.</returns>
        public static string MissionModeDescription(MissionModeEnum mode)
        {
            return mode == MissionModeEnum.Implementation
                ? "The captain changes the repository and the result lands (or holds for review)."
                : "Report-only: each mission runs read-only on a Worker-only pipeline that never lands, and the captain's final report is stored on the run target.";
        }

        /// <summary>
        /// True for a report-only definition: a Mission whose mode is Audit or Research.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="mode">Mission mode.</param>
        /// <returns>True when report-only.</returns>
        public static bool IsReportOnly(FleetActionKindEnum kind, MissionModeEnum mode)
        {
            return kind == FleetActionKindEnum.Mission && mode != MissionModeEnum.Implementation;
        }

        /// <summary>
        /// English validation message for a runtime input value (the server's rules), or null when valid. A blank
        /// value is valid unless the input is required.
        /// </summary>
        /// <param name="input">Input definition.</param>
        /// <param name="value">Value.</param>
        /// <returns>Message or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="input"/> is null.</exception>
        public static string? InputError(FleetActionInput input, string? value)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            string v = value ?? "";
            if (v.Length == 0) return input.Required ? "This input is required." : null;
            switch (input.Type)
            {
                case "semver":
                    return _Semver.IsMatch(v) ? null : "Enter a semantic version, for example 1.2.3.";
                case "package":
                    return _Package.IsMatch(v) ? null : "Enter a package identifier: letters, digits, and @ / . _ + : - only (for example Newtonsoft.Json or @scope/name).";
                case "enum":
                    return input.AllowedValues.Contains(v, StringComparer.Ordinal) ? null : "Choose one of the allowed values.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// True while a run can still change (Pending or Running).
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>True when active.</returns>
        public static bool IsRunActive(FleetActionRunStatusEnum status)
        {
            return status == FleetActionRunStatusEnum.Pending || status == FleetActionRunStatusEnum.Running;
        }

        /// <summary>
        /// Translated label of a skip or failure reason code (unknown codes fall back to the code), or "".
        /// </summary>
        /// <param name="loc">Localizer.</param>
        /// <param name="skipReason">Skip reason code.</param>
        /// <param name="failureReason">Failure reason code.</param>
        /// <returns>Label.</returns>
        public static string ReasonLabel(ITextLocalizer loc, string? skipReason, string? failureReason)
        {
            if (!String.IsNullOrEmpty(skipReason))
            {
                string? label = SkipReason(skipReason!);
                if (label == null) return skipReason!;
                string tool = skipReason!.StartsWith(FleetActionReasonCodes.ToolMissing, StringComparison.Ordinal) ? skipReason.Substring(FleetActionReasonCodes.ToolMissing.Length) : "";
                return tool.Length > 0 ? loc.T(label) + ": " + tool : loc.T(label);
            }

            if (!String.IsNullOrEmpty(failureReason))
            {
                string? label = FailureReason(failureReason!);
                return label != null ? loc.T(label) : failureReason!;
            }

            return "";
        }

        /// <summary>
        /// Template variable names in a text that the server would reject (case-insensitive, de-duplicated).
        /// </summary>
        /// <param name="text">Text.</param>
        /// <param name="inputNames">Declared runtime input names (<c>{{input.name}}</c> is known for these), or null.</param>
        /// <returns>Unknown names.</returns>
        public static List<string> FindUnknownVariables(string? text, IEnumerable<string>? inputNames = null)
        {
            List<string> inputs = (inputNames ?? Enumerable.Empty<string>()).Select(n => (n ?? "").ToLowerInvariant()).ToList();
            List<string> unknown = new List<string>();
            if (String.IsNullOrEmpty(text)) return unknown;
            foreach (Match m in _Variable.Matches(text!))
            {
                string name = m.Groups[1].Value.Trim();
                string lower = name.ToLowerInvariant();
                bool input = lower.StartsWith("input.", StringComparison.Ordinal) && inputs.Contains(lower.Substring(6));
                if (!_Known.Contains(lower) && !input && !unknown.Contains(name)) unknown.Add(name);
            }

            return unknown;
        }

        /// <summary>
        /// The client-side preview of a template for one vessel (the server renders <c>{{health.summary}}</c>).
        /// </summary>
        /// <param name="text">Template.</param>
        /// <param name="vessel">Vessel.</param>
        /// <param name="healthPlaceholder">Text standing in for the health summary (translated).</param>
        /// <param name="usesHealthSummary">True when the template uses the health summary.</param>
        /// <returns>Rendered text. Variables the server resolves (ecosystem commands without a vessel override, the
        /// scan summary, and inputs) stay as written.</returns>
        public static string RenderPreview(string text, Vessel vessel, string healthPlaceholder, out bool usesHealthSummary)
        {
            bool health = false;
            string rendered = _Variable.Replace(text ?? "", m =>
            {
                switch (m.Groups[1].Value.Trim().ToLowerInvariant())
                {
                    case "vessel.name": return vessel.Name ?? "";
                    case "vessel.id": return vessel.Id ?? "";
                    case "vessel.defaultbranch": return String.IsNullOrEmpty(vessel.DefaultBranch) ? "main" : vessel.DefaultBranch;
                    case "vessel.workingdirectory": return vessel.WorkingDirectory ?? "";
                    case "vessel.buildcommand":
                        return FirstSet(vessel.DefinitionOfDoneBuildCommand) ?? m.Value;
                    case "vessel.testcommand":
                        return FirstSet(vessel.TestCommand, vessel.DefinitionOfDoneTestCommand) ?? m.Value;
                    case "vessel.lintcommand":
                        return FirstSet(vessel.LintCommand) ?? m.Value;
                    case "vessel.auditcommand":
                        return FirstSet(vessel.AuditCommand) ?? m.Value;
                    case "health.summary":
                        health = true;
                        return healthPlaceholder;
                    default:
                        return m.Value;
                }
            });
            usesHealthSummary = health;
            return rendered;
        }

        /// <summary>
        /// Compact duration ("850 ms", "4.2 s", "3 min 5 s", "1 h 4 min"), or "-".
        /// </summary>
        /// <param name="loc">Localizer.</param>
        /// <param name="ms">Milliseconds, or null.</param>
        /// <returns>Text.</returns>
        public static string FormatDuration(ITextLocalizer loc, long? ms)
        {
            if (!ms.HasValue || ms.Value < 0) return "-";
            long v = ms.Value;
            if (v < 1000) return loc.T("{{value}} ms", LocalizationArgs.Of("value", loc.FormatNumber(v)));
            double seconds = v / 1000.0;
            if (seconds < 60)
            {
                string s = seconds < 10 ? seconds.ToString("0.0", CultureInfo.InvariantCulture) : Math.Round(seconds).ToString("0", CultureInfo.InvariantCulture);
                return loc.T("{{value}} s", LocalizationArgs.Of("value", s));
            }

            long minutes = (long)Math.Floor(seconds / 60);
            long rest = (long)Math.Round(seconds % 60);
            if (minutes < 60) return loc.T("{{minutes}} min {{seconds}} s", LocalizationArgs.Of("minutes", loc.FormatNumber(minutes), "seconds", loc.FormatNumber(rest)));
            long hours = minutes / 60;
            return loc.T("{{hours}} h {{minutes}} min", LocalizationArgs.Of("hours", loc.FormatNumber(hours), "minutes", loc.FormatNumber(minutes % 60)));
        }

        /// <summary>
        /// Milliseconds between two times (until now when live and the end is missing), or null.
        /// </summary>
        /// <param name="start">Start.</param>
        /// <param name="end">End.</param>
        /// <param name="live">Count to now when there is no end.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Milliseconds or null.</returns>
        public static long? DurationBetween(DateTime? start, DateTime? end, bool live, DateTime nowUtc)
        {
            if (!start.HasValue) return null;
            DateTime? stop = end ?? (live ? nowUtc : (DateTime?)null);
            if (!stop.HasValue) return null;
            return Math.Max(0, (long)(stop.Value - start.Value).TotalMilliseconds);
        }

        /// <summary>
        /// The run progress summary ("3 succeeded, 1 failed, 0 skipped of 5", plus cancelled).
        /// </summary>
        /// <param name="loc">Localizer.</param>
        /// <param name="run">Run.</param>
        /// <param name="withPercent">Append the finished percentage.</param>
        /// <returns>Text.</returns>
        public static string ProgressText(ITextLocalizer loc, FleetActionRun run, bool withPercent)
        {
            int total = Math.Max(0, run.TargetCount);
            int done = run.SucceededCount + run.FailedCount + run.SkippedCount + run.CancelledCount;
            Dictionary<string, object?> args = new Dictionary<string, object?>(StringComparer.Ordinal);
            args["succeeded"] = loc.FormatNumber(run.SucceededCount);
            args["failed"] = loc.FormatNumber(run.FailedCount);
            args["skipped"] = loc.FormatNumber(run.SkippedCount);
            args["total"] = loc.FormatNumber(total);
            StringBuilder sb = new StringBuilder(loc.T("{{succeeded}} succeeded, {{failed}} failed, {{skipped}} skipped of {{total}}", args));
            if (withPercent)
            {
                int percent = total > 0 ? (int)Math.Round(done * 100.0 / total) : 0;
                sb.Append(" (").Append(loc.T("{{percent}}% finished", LocalizationArgs.Of("percent", loc.FormatNumber(percent)))).Append(')');
            }

            if (run.CancelledCount > 0) sb.Append(", ").Append(loc.T("{{count}} cancelled", LocalizationArgs.Of("count", loc.FormatNumber(run.CancelledCount))));
            return sb.ToString();
        }

        /// <summary>
        /// ASCII segmented bar: <c>#</c> succeeded, <c>x</c> failed, <c>-</c> skipped, <c>/</c> cancelled, <c>.</c> remaining.
        /// </summary>
        /// <param name="run">Run.</param>
        /// <param name="width">Inner width.</param>
        /// <returns>Bar including brackets.</returns>
        public static string ProgressBar(FleetActionRun run, int width)
        {
            int w = Math.Max(4, width);
            int total = Math.Max(0, run.TargetCount);
            if (total == 0) return "[" + new string('.', w) + "]";
            int s = run.SucceededCount * w / total;
            int f = run.FailedCount * w / total;
            int k = run.SkippedCount * w / total;
            int c = run.CancelledCount * w / total;
            int rest = Math.Max(0, w - s - f - k - c);
            return "[" + new string('#', s) + new string('x', f) + new string('-', k) + new string('/', c) + new string('.', rest) + "]";
        }

        #endregion

        #region Private-Methods

        private static string? FirstSet(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!String.IsNullOrWhiteSpace(value)) return value;
            }

            return null;
        }

        private static string? SkipReason(string code)
        {
            if (code.StartsWith(FleetActionReasonCodes.ToolMissing, StringComparison.Ordinal)) return "Required tool is missing";
            switch (code)
            {
                case FleetActionReasonCodes.NotApplicable: return "Action does not apply (applicability check did not pass)";
                case FleetActionReasonCodes.NoMatchingEcosystem: return "No matching ecosystem";
                case FleetActionReasonCodes.NotConfigured: return "Not configured";
                case FleetActionReasonCodes.NoForgeCredentials: return "No forge credentials";
                case FleetActionReasonCodes.NetworkUnavailable: return "Network unavailable";
                case FleetActionReasonCodes.DirtyTree: return "Working tree has uncommitted changes";
                case FleetActionReasonCodes.NoWorkingDirectory: return "Vessel has no working directory";
                case FleetActionReasonCodes.NoBuildCommand: return "Vessel has no build command";
                case FleetActionReasonCodes.DispatchRejected: return "Dispatch was rejected";
                case FleetActionReasonCodes.NotAuthorized: return "Not authorized";
                case FleetActionReasonCodes.VesselNotFound: return "Vessel no longer exists";
                case FleetActionReasonCodes.HarborUnavailable: return "Preferred harbor is not connected";
                default: return null;
            }
        }

        private static string? FailureReason(string code)
        {
            switch (code)
            {
                case FleetActionReasonCodes.Interrupted: return "Interrupted by an Admiral restart";
                case FleetActionReasonCodes.NonZeroExit: return "Command exited with a non-zero code";
                case FleetActionReasonCodes.Timeout: return "Command timed out";
                case FleetActionReasonCodes.GitStatusFailed: return "Could not check the working tree";
                case FleetActionReasonCodes.TemplateError: return "Template could not be rendered";
                case FleetActionReasonCodes.ExecutionError: return "Command could not be started";
                case FleetActionReasonCodes.DispatchFailed: return "Voyage dispatch failed";
                case FleetActionReasonCodes.DispatchUnavailable: return "Dispatch is unavailable";
                case FleetActionReasonCodes.VoyageFailed: return "Voyage failed or did not land";
                case FleetActionReasonCodes.VoyageMissing: return "Voyage no longer exists";
                default: return null;
            }
        }

        #endregion
    }
}
