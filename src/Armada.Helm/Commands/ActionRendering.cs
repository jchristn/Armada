namespace Armada.Helm.Commands
{
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Shared Spectre markup for fleet action statuses and counts.
    /// </summary>
    public static class ActionRendering
    {
        /// <summary>
        /// Colored markup for a run status.
        /// </summary>
        /// <param name="status">Run status.</param>
        /// <returns>Markup.</returns>
        public static string RunStatus(FleetActionRunStatusEnum status)
        {
            string color = status switch
            {
                FleetActionRunStatusEnum.Completed => "green",
                FleetActionRunStatusEnum.CompletedWithFailures => "orange1",
                FleetActionRunStatusEnum.Failed => "red",
                FleetActionRunStatusEnum.Cancelled => "grey",
                FleetActionRunStatusEnum.Running => "gold1",
                _ => "dodgerblue1"
            };
            return "[" + color + "]" + status + "[/]";
        }

        /// <summary>
        /// Colored markup for a target status.
        /// </summary>
        /// <param name="status">Target status.</param>
        /// <returns>Markup.</returns>
        public static string TargetStatus(FleetActionTargetStatusEnum status)
        {
            string color = status switch
            {
                FleetActionTargetStatusEnum.Succeeded => "green",
                FleetActionTargetStatusEnum.Failed => "red",
                FleetActionTargetStatusEnum.TimedOut => "red",
                FleetActionTargetStatusEnum.Skipped => "orange1",
                FleetActionTargetStatusEnum.Cancelled => "grey",
                FleetActionTargetStatusEnum.Running => "gold1",
                _ => "dodgerblue1"
            };
            return "[" + color + "]" + status + "[/]";
        }

        /// <summary>
        /// Compact count summary for a run.
        /// </summary>
        /// <param name="run">Run.</param>
        /// <returns>Plain text such as "3/5 ok, 1 failed, 1 skipped".</returns>
        public static string Counts(FleetActionRun run)
        {
            string text = run.SucceededCount + "/" + run.TargetCount + " ok";
            if (run.FailedCount > 0) text += ", " + run.FailedCount + " failed";
            if (run.SkippedCount > 0) text += ", " + run.SkippedCount + " skipped";
            if (run.CancelledCount > 0) text += ", " + run.CancelledCount + " cancelled";
            return text;
        }
    }
}
