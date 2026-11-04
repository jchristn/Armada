namespace Armada.Helm.Rendering
{
    using Spectre.Console;
    using Armada.Core.Enums;

    /// <summary>
    /// Shared rendering helpers for Armada CLI table output.
    /// </summary>
    public static class TableRenderer
    {
        /// <summary>
        /// Create a styled table with Armada branding.
        /// </summary>
        /// <param name="title">Table title (will be wrapped in bold dodgerblue1 markup).</param>
        /// <param name="emoji">Optional emoji prefix for the title.</param>
        /// <returns>A configured Table instance.</returns>
        public static Table CreateTable(string title, string? emoji = null)
        {
            string titleMarkup = emoji != null
                ? $"{emoji} [bold dodgerblue1]{Markup.Escape(title)}[/]"
                : $"[bold dodgerblue1]{Markup.Escape(title)}[/]";
            AnsiConsole.MarkupLine(titleMarkup);

            Table table = new Table();
            table.Border(TableBorder.Rounded);
            table.BorderColor(Color.DodgerBlue1);

            return table;
        }

        /// <summary>
        /// Get the display color for a captain state.
        /// </summary>
        public static string CaptainStateColor(CaptainStateEnum state)
        {
            return state switch
            {
                CaptainStateEnum.Idle => "dodgerblue1",
                CaptainStateEnum.Working => "green",
                CaptainStateEnum.Planning => "green",
                CaptainStateEnum.Refining => "green",
                CaptainStateEnum.Analyzing => "green",
                CaptainStateEnum.Stalled => "red",
                CaptainStateEnum.Quarantined => "red",
                CaptainStateEnum.Stopping => "gold1",
                _ => "default"
            };
        }

        /// <summary>
        /// Get the emoji for a captain state.
        /// </summary>
        public static string CaptainStateEmoji(CaptainStateEnum state)
        {
            return state switch
            {
                CaptainStateEnum.Idle => "💤",
                CaptainStateEnum.Working => "⚙️",
                CaptainStateEnum.Stalled => "🔴",
                CaptainStateEnum.Stopping => "🛑",
                _ => "❓"
            };
        }

        /// <summary>
        /// Get the display color for a mission status.
        /// </summary>
        public static string MissionStatusColor(string status)
        {
            return status switch
            {
                "Complete" => "green",
                "InProgress" => "gold1",
                "Assigned" => "deepskyblue1",
                "Testing" => "mediumpurple2",
                "Review" => "darkorange",
                "PullRequestOpen" => "dodgerblue1",
                "WorkProduced" => "deepskyblue1",
                "Verifying" => "mediumpurple2",
                "Failed" => "red",
                "LandingFailed" => "red",
                "Blocked" => "darkorange",
                "Cancelled" => "grey",
                "Pending" => "dim",
                _ => "default"
            };
        }

        /// <summary>
        /// Get the emoji for a mission status.
        /// </summary>
        public static string MissionStatusEmoji(string status)
        {
            return status switch
            {
                "Complete" => "✅",
                "InProgress" => "⚙️",
                "Assigned" => "📌",
                "Testing" => "🧪",
                "Review" => "👀",
                "PullRequestOpen" => "🔀",
                "WorkProduced" => "📦",
                "Verifying" => "🧪",
                "Failed" => "❌",
                "LandingFailed" => "❌",
                "Blocked" => "⛔",
                "Cancelled" => "🚫",
                "Pending" => "📋",
                _ => "📋"
            };
        }

        /// <summary>
        /// Get the display color for a voyage status.
        /// </summary>
        public static string VoyageStatusColor(VoyageStatusEnum status)
        {
            return status switch
            {
                VoyageStatusEnum.Open => "dodgerblue1",
                VoyageStatusEnum.InProgress => "green",
                VoyageStatusEnum.Complete => "dim",
                VoyageStatusEnum.Failed => "red",
                VoyageStatusEnum.Cancelled => "grey",
                _ => "default"
            };
        }

        /// <summary>
        /// Get the emoji for a signal type.
        /// </summary>
        public static string SignalTypeEmoji(SignalTypeEnum type)
        {
            return type switch
            {
                SignalTypeEnum.Error => "❌",
                SignalTypeEnum.Completion => "✅",
                SignalTypeEnum.Progress => "📊",
                SignalTypeEnum.Assignment => "📌",
                SignalTypeEnum.Heartbeat => "💓",
                SignalTypeEnum.Nudge => "👋",
                SignalTypeEnum.Mail => "📧",
                _ => "📡"
            };
        }

        /// <summary>
        /// Get the display color for a signal type.
        /// </summary>
        public static string SignalTypeColor(SignalTypeEnum type)
        {
            return type switch
            {
                SignalTypeEnum.Error => "red",
                SignalTypeEnum.Completion => "green",
                SignalTypeEnum.Progress => "gold1",
                SignalTypeEnum.Assignment => "deepskyblue1",
                _ => "dim"
            };
        }

        /// <summary>
        /// Render pagination info above a table.
        /// </summary>
        public static void RenderPaginationHeader(int pageNumber, int totalPages, long totalRecords, double totalMs)
        {
            AnsiConsole.MarkupLine(
                $"[dim]Page {pageNumber} of {totalPages} · {totalRecords} total records · {totalMs:F1}ms[/]");
        }

        /// <summary>
        /// Get the emoji for an agent runtime.
        /// </summary>
        public static string RuntimeEmoji(AgentRuntimeEnum runtime)
        {
            return runtime switch
            {
                AgentRuntimeEnum.ClaudeCode => "🤖",
                AgentRuntimeEnum.Codex => "📦",
                _ => "🔧"
            };
        }
    }
}
