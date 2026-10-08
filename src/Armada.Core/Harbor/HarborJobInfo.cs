namespace Armada.Core.Harbor
{
    using System;
    using System.Globalization;

    /// <summary>
    /// A job running on this Harbor, as the Harbor shows it: what it is (a mission, an Ask turn), which runtime runs it,
    /// and when it started.
    /// </summary>
    public class HarborJobInfo
    {
        #region Public-Members

        /// <summary>
        /// Harbor-scoped job identifier the Admiral assigned.
        /// </summary>
        public string JobId { get; set; } = String.Empty;

        /// <summary>
        /// What the job is for.
        /// </summary>
        public HarborJobKindEnum Kind { get; set; } = HarborJobKindEnum.Unknown;

        /// <summary>
        /// Mission identifier, for a mission.
        /// </summary>
        public string? MissionId { get; set; } = null;

        /// <summary>
        /// Captain identifier, when the Admiral sent it.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// Runtime name as the Admiral sent it (for example "ClaudeCode").
        /// </summary>
        public string Runtime { get; set; } = String.Empty;

        /// <summary>
        /// When the job started, UTC.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborJobInfo()
        {
        }

        /// <summary>
        /// Describe a launch request.
        /// </summary>
        /// <param name="request">Launch request.</param>
        /// <param name="startedUtc">When it started, UTC.</param>
        /// <returns>The job.</returns>
        public static HarborJobInfo FromLaunch(HarborLaunchRequest request, DateTime startedUtc)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            HarborJobInfo info = new HarborJobInfo();
            info.JobId = request.JobId ?? String.Empty;
            info.Kind = request.JobKindType;
            info.MissionId = String.IsNullOrWhiteSpace(request.MissionId) ? null : request.MissionId!.Trim();
            info.CaptainId = String.IsNullOrWhiteSpace(request.CaptainId) ? null : request.CaptainId!.Trim();
            info.Runtime = request.Runtime ?? String.Empty;
            info.StartedUtc = startedUtc;
            if (info.Kind == HarborJobKindEnum.Unknown && info.MissionId != null) info.Kind = HarborJobKindEnum.Mission;
            return info;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A plain-language title, for example "Mission msn_abc" or "Ask turn".
        /// </summary>
        /// <returns>Title.</returns>
        public string Title()
        {
            switch (Kind)
            {
                case HarborJobKindEnum.Mission: return MissionId != null ? "Mission " + MissionId : "Mission";
                case HarborJobKindEnum.AskTurn: return "Ask turn";
                case HarborJobKindEnum.Planning: return "Planning session";
                case HarborJobKindEnum.Refinement: return "Objective refinement";
                case HarborJobKindEnum.ContextBuild: return "Context build";
                default: return "Captain job";
            }
        }

        /// <summary>
        /// Elapsed time since <see cref="StartedUtc"/>, for example "45s", "3m 05s", or "1h 02m".
        /// </summary>
        /// <param name="nowUtc">Current time, UTC.</param>
        /// <returns>Text.</returns>
        public string Elapsed(DateTime nowUtc)
        {
            return FormatElapsed(nowUtc - StartedUtc);
        }

        /// <summary>
        /// Format a duration compactly: "45s", "3m 05s", or "1h 02m". Negative durations read as 0s.
        /// </summary>
        /// <param name="elapsed">Duration.</param>
        /// <returns>Text.</returns>
        public static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            long totalSeconds = (long)elapsed.TotalSeconds;
            long hours = totalSeconds / 3600;
            long minutes = (totalSeconds % 3600) / 60;
            long seconds = totalSeconds % 60;
            if (hours > 0) return hours.ToString(CultureInfo.InvariantCulture) + "h " + minutes.ToString("00", CultureInfo.InvariantCulture) + "m";
            if (minutes > 0) return minutes.ToString(CultureInfo.InvariantCulture) + "m " + seconds.ToString("00", CultureInfo.InvariantCulture) + "s";
            return seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        /// <summary>
        /// A copy.
        /// </summary>
        /// <returns>The copy.</returns>
        public HarborJobInfo Clone()
        {
            return new HarborJobInfo
            {
                JobId = JobId,
                Kind = Kind,
                MissionId = MissionId,
                CaptainId = CaptainId,
                Runtime = Runtime,
                StartedUtc = StartedUtc
            };
        }

        #endregion
    }
}
