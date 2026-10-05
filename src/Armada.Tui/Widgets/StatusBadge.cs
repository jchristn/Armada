namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Tui.Theming;
    using TUIKit;

    /// <summary>
    /// Maps a status to a severity style and an ASCII marker so status is never conveyed by color alone (the
    /// dashboard's StatusBadge and its severity mapping). Typed status enums use a per-enum map; plain strings are
    /// matched by exact name only (never by substring, so a composed display string such as
    /// "Succeeded / Failed" is not classified by whichever word happens to match first). Thread-safe (stateless).
    /// </summary>
    public static class StatusBadge
    {
        #region Private-Members

        private static readonly Dictionary<string, StatusSeverityEnum> _ByName = BuildNameMap();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Severity for a typed status value. Known entity status enums use their own map; other enums fall back to
        /// the exact-name table.
        /// </summary>
        /// <param name="status">Status value, or null.</param>
        /// <returns>Severity.</returns>
        public static StatusSeverityEnum Severity(Enum? status)
        {
            switch (status)
            {
                case null: return StatusSeverityEnum.Info;
                case MissionStatusEnum mission: return For(mission);
                case VoyageStatusEnum voyage: return For(voyage);
                case CaptainStateEnum captain: return For(captain);
                case DeploymentStatusEnum deployment: return For(deployment);
                case DeploymentVerificationStatusEnum verification: return For(verification);
                case ObjectiveStatusEnum objective: return For(objective);
                case IncidentStatusEnum incident: return For(incident);
                default: return Severity(status.ToString());
            }
        }

        /// <summary>
        /// Severity for a status name, by exact (case-insensitive) name. Unknown or composed text is Info.
        /// </summary>
        /// <param name="status">Status name.</param>
        /// <returns>Severity.</returns>
        public static StatusSeverityEnum Severity(string? status)
        {
            if (String.IsNullOrWhiteSpace(status)) return StatusSeverityEnum.Info;
            return _ByName.TryGetValue(status!.Trim(), out StatusSeverityEnum severity) ? severity : StatusSeverityEnum.Info;
        }

        /// <summary>
        /// Combined severity of a deployment and its verification: a failed verification is an error even when the
        /// deployment itself succeeded.
        /// </summary>
        /// <param name="status">Deployment status.</param>
        /// <param name="verification">Verification status, or null.</param>
        /// <returns>Severity.</returns>
        public static StatusSeverityEnum Severity(DeploymentStatusEnum status, DeploymentVerificationStatusEnum? verification)
        {
            StatusSeverityEnum deployment = For(status);
            if (verification == null) return deployment;
            return Worst(deployment, For(verification.Value));
        }

        /// <summary>
        /// The more severe of two severities (Error, then Warning, then Running, then Success, then Info).
        /// </summary>
        /// <param name="a">First.</param>
        /// <param name="b">Second.</param>
        /// <returns>The more severe one.</returns>
        public static StatusSeverityEnum Worst(StatusSeverityEnum a, StatusSeverityEnum b)
        {
            return Rank(a) >= Rank(b) ? a : b;
        }

        /// <summary>
        /// Style for a severity.
        /// </summary>
        /// <param name="severity">Severity.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>Style.</returns>
        public static CellStyle Style(StatusSeverityEnum severity, ArmadaTheme theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            switch (severity)
            {
                case StatusSeverityEnum.Success: return theme.Success;
                case StatusSeverityEnum.Error: return theme.Error;
                case StatusSeverityEnum.Warning: return theme.Warning;
                case StatusSeverityEnum.Running: return theme.Info;
                default: return theme.Text;
            }
        }

        /// <summary>
        /// Style for a typed status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>Style.</returns>
        public static CellStyle Style(Enum? status, ArmadaTheme theme)
        {
            return Style(Severity(status), theme);
        }

        /// <summary>
        /// Style for a status name.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="theme">Palette.</param>
        /// <returns>Style.</returns>
        public static CellStyle Style(string? status, ArmadaTheme theme)
        {
            return Style(Severity(status), theme);
        }

        /// <summary>
        /// ASCII marker for a severity: <c>+</c> success, <c>x</c> error, <c>!</c> warning, <c>~</c> running, <c>-</c> other.
        /// </summary>
        /// <param name="severity">Severity.</param>
        /// <returns>Marker.</returns>
        public static string Marker(StatusSeverityEnum severity)
        {
            switch (severity)
            {
                case StatusSeverityEnum.Success: return "+";
                case StatusSeverityEnum.Error: return "x";
                case StatusSeverityEnum.Warning: return "!";
                case StatusSeverityEnum.Running: return "~";
                default: return "-";
            }
        }

        /// <summary>
        /// ASCII marker for a typed status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Marker.</returns>
        public static string Marker(Enum? status)
        {
            return Marker(Severity(status));
        }

        /// <summary>
        /// ASCII marker for a status name.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Marker.</returns>
        public static string Marker(string? status)
        {
            return Marker(Severity(status));
        }

        /// <summary>
        /// Marker plus status text, for example <c>+ Complete</c>.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Label.</returns>
        public static string Label(Enum? status)
        {
            return Marker(status) + " " + (status?.ToString() ?? "");
        }

        /// <summary>
        /// Marker plus status text, for example <c>+ Complete</c>.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <returns>Label.</returns>
        public static string Label(string? status)
        {
            return Marker(status) + " " + (status ?? "");
        }

        /// <summary>
        /// Marker for the status plus separate display text (for example a localized rendering of the status). The
        /// severity comes from <paramref name="status"/>, never from <paramref name="display"/>.
        /// </summary>
        /// <param name="status">Status name used for the severity.</param>
        /// <param name="display">Text shown after the marker.</param>
        /// <returns>Label.</returns>
        public static string Label(string? status, string? display)
        {
            return Marker(status) + " " + (display ?? "");
        }

        #endregion

        #region Private-Methods

        private static int Rank(StatusSeverityEnum severity)
        {
            switch (severity)
            {
                case StatusSeverityEnum.Error: return 4;
                case StatusSeverityEnum.Warning: return 3;
                case StatusSeverityEnum.Running: return 2;
                case StatusSeverityEnum.Success: return 1;
                default: return 0;
            }
        }

        private static StatusSeverityEnum For(MissionStatusEnum status)
        {
            switch (status)
            {
                case MissionStatusEnum.Complete: return StatusSeverityEnum.Success;
                case MissionStatusEnum.Failed:
                case MissionStatusEnum.LandingFailed: return StatusSeverityEnum.Error;
                case MissionStatusEnum.Cancelled:
                case MissionStatusEnum.Review: return StatusSeverityEnum.Warning;
                case MissionStatusEnum.Assigned:
                case MissionStatusEnum.InProgress:
                case MissionStatusEnum.Testing: return StatusSeverityEnum.Running;
                case MissionStatusEnum.Pending: return StatusSeverityEnum.Warning;
                default: return StatusSeverityEnum.Info;
            }
        }

        private static StatusSeverityEnum For(VoyageStatusEnum status)
        {
            switch (status)
            {
                case VoyageStatusEnum.Complete: return StatusSeverityEnum.Success;
                case VoyageStatusEnum.Failed: return StatusSeverityEnum.Error;
                case VoyageStatusEnum.Cancelled: return StatusSeverityEnum.Warning;
                case VoyageStatusEnum.InProgress: return StatusSeverityEnum.Running;
                default: return StatusSeverityEnum.Info;
            }
        }

        private static StatusSeverityEnum For(CaptainStateEnum state)
        {
            switch (state)
            {
                case CaptainStateEnum.Idle: return StatusSeverityEnum.Success;
                case CaptainStateEnum.Stalled:
                case CaptainStateEnum.Stopping: return StatusSeverityEnum.Warning;
                case CaptainStateEnum.Working: return StatusSeverityEnum.Running;
                default: return StatusSeverityEnum.Info;
            }
        }

        private static StatusSeverityEnum For(DeploymentStatusEnum status)
        {
            switch (status)
            {
                case DeploymentStatusEnum.Succeeded: return StatusSeverityEnum.Success;
                case DeploymentStatusEnum.Failed:
                case DeploymentStatusEnum.VerificationFailed: return StatusSeverityEnum.Error;
                case DeploymentStatusEnum.PendingApproval:
                case DeploymentStatusEnum.Denied:
                case DeploymentStatusEnum.RollingBack:
                case DeploymentStatusEnum.RolledBack: return StatusSeverityEnum.Warning;
                case DeploymentStatusEnum.Running: return StatusSeverityEnum.Running;
                default: return StatusSeverityEnum.Info;
            }
        }

        private static StatusSeverityEnum For(DeploymentVerificationStatusEnum status)
        {
            switch (status)
            {
                case DeploymentVerificationStatusEnum.Passed: return StatusSeverityEnum.Success;
                case DeploymentVerificationStatusEnum.Failed: return StatusSeverityEnum.Error;
                case DeploymentVerificationStatusEnum.Partial: return StatusSeverityEnum.Warning;
                case DeploymentVerificationStatusEnum.Running: return StatusSeverityEnum.Running;
                default: return StatusSeverityEnum.Info;
            }
        }

        private static StatusSeverityEnum For(ObjectiveStatusEnum status)
        {
            switch (status)
            {
                case ObjectiveStatusEnum.Completed: return StatusSeverityEnum.Success;
                case ObjectiveStatusEnum.Cancelled: return StatusSeverityEnum.Warning;
                case ObjectiveStatusEnum.InProgress: return StatusSeverityEnum.Running;
                default: return StatusSeverityEnum.Info;
            }
        }

        private static StatusSeverityEnum For(IncidentStatusEnum status)
        {
            switch (status)
            {
                case IncidentStatusEnum.RolledBack: return StatusSeverityEnum.Warning;
                default: return StatusSeverityEnum.Info;
            }
        }

        private static Dictionary<string, StatusSeverityEnum> BuildNameMap()
        {
            Dictionary<string, StatusSeverityEnum> map = new Dictionary<string, StatusSeverityEnum>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in new string[] { "Completed", "Complete", "Landed", "Passed", "Pass", "Succeeded", "Healthy", "Idle", "Shipped" })
                map[name] = StatusSeverityEnum.Success;
            foreach (string name in new string[] { "Failed", "Fail", "Error", "Unhealthy", "LandingFailed", "VerificationFailed", "DependencyFailed", "MissionFailed", "TimedOut" })
                map[name] = StatusSeverityEnum.Error;
            foreach (string name in new string[] { "Cancelled", "Canceled", "Stalled", "Stopping", "RolledBack", "RollingBack", "Denied", "ReviewDenied", "AccessDenied", "Warn", "Warning", "Review", "Pending", "PendingApproval", "CompletedWithFailures", "Partial" })
                map[name] = StatusSeverityEnum.Warning;
            foreach (string name in new string[] { "InProgress", "Running", "Working", "Active", "Assigned", "Testing", "Queued" })
                map[name] = StatusSeverityEnum.Running;
            return map;
        }

        #endregion
    }
}
