namespace Armada.Core.Harbor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;

    /// <summary>
    /// One row of Harbor's Running now list, as text: what the job is (a mission's title, an Ask turn's question), its
    /// runtime and elapsed time, where it belongs (vessel and voyage), who runs it (captain, model, stage), its branch and
    /// identifiers, its latest activity, its last output lines, and where "Open in Dashboard" and "View output" go.
    /// Built from a <see cref="HarborJobInfo"/> without Avalonia, so it can be tested.
    /// </summary>
    public class HarborRunningJobView
    {
        #region Public-Members

        /// <summary>
        /// Job identifier.
        /// </summary>
        public string JobId { get; set; } = String.Empty;

        /// <summary>
        /// What the job is kind of.
        /// </summary>
        public HarborJobKindEnum Kind { get; set; } = HarborJobKindEnum.Unknown;

        /// <summary>
        /// The first line: the mission's title, "Ask turn: " and the question in quotes, or the kind of job.
        /// </summary>
        public string Title { get; set; } = String.Empty;

        /// <summary>
        /// The runtime (for example "ClaudeCode"), or "Unknown runtime".
        /// </summary>
        public string Runtime { get; set; } = String.Empty;

        /// <summary>
        /// How long the job has run, for example "12m 34s".
        /// </summary>
        public string Elapsed { get; set; } = String.Empty;

        /// <summary>
        /// Where a mission belongs: its vessel, then its voyage with its position, for example
        /// <c>PrettyId  &gt;  Voyage "API hardening" (2 of 3)</c>; null when unknown.
        /// </summary>
        public string? Context { get; set; } = null;

        /// <summary>
        /// Who runs it: "Captain " and the captain's name, its model in parentheses, and the stage, for example
        /// "Captain ada (claude-sonnet-4)  -  Implement stage"; null when the captain's name is unknown.
        /// </summary>
        public string? Captain { get; set; } = null;

        /// <summary>
        /// Label shown before <see cref="ConversationId"/> on an Ask turn's captain line ("conversation").
        /// </summary>
        public string? ConversationLabel { get; set; } = null;

        /// <summary>
        /// The Ask conversation an Ask turn belongs to (shown copyable), or null.
        /// </summary>
        public string? ConversationId { get; set; } = null;

        /// <summary>
        /// The branch a mission works on, or null.
        /// </summary>
        public string? Branch { get; set; } = null;

        /// <summary>
        /// The mission (shown copyable), or null.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// The captain's ID (shown copyable when the captain's name is unknown), or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// The latest activity as "&gt; " and its summary, or null when the job has reported none.
        /// </summary>
        public string? Activity { get; set; } = null;

        /// <summary>
        /// The job's last output lines, oldest first (shown when the row is expanded).
        /// </summary>
        public List<string> RecentLines { get; set; } = new List<string>();

        /// <summary>
        /// The dashboard page of the mission or the Ask conversation, or null when there is none or the dashboard address
        /// is not an http(s) URL.
        /// </summary>
        public string? DashboardLink { get; set; } = null;

        /// <summary>
        /// The job's log on this machine (opened by "View output"), or null.
        /// </summary>
        public string? LogPath { get; set; } = null;

        #endregion

        #region Private-Members

        private const string _Separator = "  -  ";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Describe a running job.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <param name="nowUtc">Now, UTC, for the elapsed time.</param>
        /// <param name="dashboardUrl">The dashboard address from Harbor's settings (for example
        /// http://127.0.0.1:7890/dashboard), or null.</param>
        /// <returns>The row.</returns>
        public static HarborRunningJobView From(HarborJobInfo job, DateTime nowUtc, string? dashboardUrl)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            HarborLaunchDisplay display = job.Display ?? new HarborLaunchDisplay();
            HarborRunningJobView view = new HarborRunningJobView
            {
                JobId = job.JobId,
                Kind = job.Kind,
                Runtime = String.IsNullOrWhiteSpace(job.Runtime) ? "Unknown runtime" : job.Runtime,
                Elapsed = job.Elapsed(nowUtc),
                MissionId = job.MissionId,
                CaptainId = job.CaptainId,
                Branch = Blank(display.BranchName),
                Activity = job.Activity != null && !String.IsNullOrWhiteSpace(job.Activity.Summary) ? "> " + job.Activity.Summary : null,
                RecentLines = new List<string>(job.RecentLines),
                LogPath = job.LogPath
            };

            view.Title = TitleOf(job, display);
            view.Context = ContextOf(display);
            view.Captain = CaptainOf(display, job.Model, job.Kind);
            if (job.Kind == HarborJobKindEnum.AskTurn && !String.IsNullOrWhiteSpace(display.AskThreadId))
            {
                view.ConversationLabel = "conversation";
                view.ConversationId = display.AskThreadId!.Trim();
            }

            view.DashboardLink = DashboardLinkOf(dashboardUrl, job, display);
            return view;
        }

        /// <summary>
        /// A dashboard page under a dashboard address: the address without a trailing slash, then the path. Null when the
        /// address is not an absolute http or https URL.
        /// </summary>
        /// <param name="dashboardUrl">Dashboard address.</param>
        /// <param name="path">Path below it, starting with a slash.</param>
        /// <returns>The link, or null.</returns>
        public static string? DashboardPage(string? dashboardUrl, string path)
        {
            if (String.IsNullOrWhiteSpace(dashboardUrl) || String.IsNullOrEmpty(path)) return null;
            if (!Uri.TryCreate(dashboardUrl!.Trim(), UriKind.Absolute, out Uri? baseUri)) return null;
            if (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) return null;
            string root = baseUri.GetLeftPart(UriPartial.Path).TrimEnd('/');
            return root + path;
        }

        #endregion

        #region Private-Methods

        private static string TitleOf(HarborJobInfo job, HarborLaunchDisplay display)
        {
            if (job.Kind == HarborJobKindEnum.Mission && !String.IsNullOrWhiteSpace(display.MissionTitle)) return display.MissionTitle!.Trim();
            if (job.Kind == HarborJobKindEnum.AskTurn && !String.IsNullOrWhiteSpace(display.AskQuestion)) return "Ask turn: \"" + display.AskQuestion!.Trim() + "\"";
            return job.KindName();
        }

        private static string? ContextOf(HarborLaunchDisplay display)
        {
            List<string> parts = new List<string>();
            if (!String.IsNullOrWhiteSpace(display.VesselName)) parts.Add(display.VesselName!.Trim());
            if (!String.IsNullOrWhiteSpace(display.VoyageTitle) || !String.IsNullOrWhiteSpace(display.VoyageId))
            {
                string voyage = !String.IsNullOrWhiteSpace(display.VoyageTitle) ? "Voyage \"" + display.VoyageTitle!.Trim() + "\"" : "Voyage " + display.VoyageId!.Trim();
                if (display.VoyagePosition.HasValue && display.VoyageMissionCount.HasValue && display.VoyagePosition.Value > 0)
                    voyage += " (" + display.VoyagePosition.Value.ToString(CultureInfo.InvariantCulture) + " of " + display.VoyageMissionCount.Value.ToString(CultureInfo.InvariantCulture) + ")";
                parts.Add(voyage);
            }

            return parts.Count == 0 ? null : String.Join("  >  ", parts);
        }

        private static string? CaptainOf(HarborLaunchDisplay display, string? launchModel, HarborJobKindEnum kind)
        {
            if (String.IsNullOrWhiteSpace(display.CaptainName)) return null;
            string text = "Captain " + display.CaptainName!.Trim();
            string? model = Blank(display.CaptainModel) ?? Blank(launchModel);
            if (model != null) text += " (" + model + ")";
            if (kind == HarborJobKindEnum.Mission && !String.IsNullOrWhiteSpace(display.Stage)) text += _Separator + display.Stage!.Trim() + " stage";
            return text;
        }

        private static string? DashboardLinkOf(string? dashboardUrl, HarborJobInfo job, HarborLaunchDisplay display)
        {
            if (!String.IsNullOrWhiteSpace(job.MissionId)) return DashboardPage(dashboardUrl, "/missions/" + Uri.EscapeDataString(job.MissionId!.Trim()));
            if (!String.IsNullOrWhiteSpace(display.AskThreadId)) return DashboardPage(dashboardUrl, "/ask/" + Uri.EscapeDataString(display.AskThreadId!.Trim()));
            return null;
        }

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        #endregion
    }
}
