namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Screens.Operations;
    using Armada.Tui.Services;

    /// <summary>
    /// Starts vessel health evaluations and tracks the background job until it finishes, ported from the dashboard's
    /// <c>useHealthEvaluation</c>: a 409 (already running) tracks the running job instead of failing, <see cref="Discover"/>
    /// finds a scheduled run in progress, and the job is polled every two seconds. Not thread-safe; poll results are
    /// posted to the UI loop by the owning screen.
    /// </summary>
    public class HealthEvaluation : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Job name the server gives vessel health evaluations.
        /// </summary>
        public const string JobName = "Vessel health evaluation";

        /// <summary>
        /// The tracked job id, or null when idle.
        /// </summary>
        public string? TrackedJobId { get; private set; } = null;

        /// <summary>
        /// The running job from the last poll, or null.
        /// </summary>
        public Job? ActiveJob { get; private set; } = null;

        /// <summary>
        /// The most recent finished evaluation job seen, or null.
        /// </summary>
        public Job? LastJob { get; private set; } = null;

        /// <summary>
        /// True while a job is tracked.
        /// </summary>
        public bool Running
        {
            get { return TrackedJobId != null; }
        }

        /// <summary>
        /// True while a start request is in flight.
        /// </summary>
        public bool Starting { get; private set; } = false;

        /// <summary>
        /// Poll interval in milliseconds.
        /// </summary>
        public int PollMilliseconds { get; set; } = 2000;

        /// <summary>
        /// Raised on the loop once when a tracked job finishes.
        /// </summary>
        public event EventHandler<Job>? Finished;

        #endregion

        #region Private-Members

        private readonly OpsScreen _Screen;
        private Timer? _Timer = null;
        private bool _Polling = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="screen">Owning screen (API calls are dropped once it closes).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="screen"/> is null.</exception>
        public HealthEvaluation(OpsScreen screen)
        {
            _Screen = screen ?? throw new ArgumentNullException(nameof(screen));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// True for a finished job status.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <returns>True when terminal.</returns>
        public static bool IsTerminal(Job? job)
        {
            return job != null && (job.Status == JobStatusEnum.Succeeded || job.Status == JobStatusEnum.Failed || job.Status == JobStatusEnum.Cancelled);
        }

        /// <summary>
        /// The dashboard's toast for an evaluate response: severity and translated text.
        /// </summary>
        /// <param name="start">Start result.</param>
        /// <param name="loc">Localizer.</param>
        /// <param name="severity">Toast severity.</param>
        /// <returns>Text.</returns>
        public static string Describe(VesselHealthEvaluationStart start, ITextLocalizer loc, out NotificationSeverityEnum severity)
        {
            if (start.AlreadyRunning)
            {
                severity = NotificationSeverityEnum.Warning;
                return loc.T("An evaluation is already running. Showing its progress instead.");
            }

            severity = NotificationSeverityEnum.Success;
            string key = start.VesselCount == 1 ? "Evaluation started for {{count}} vessel." : "Evaluation started for {{count}} vessels.";
            return loc.T(key, LocalizationArgs.Of("count", loc.FormatNumber(start.VesselCount)));
        }

        /// <summary>
        /// Start an evaluation (all active vessels, or the given ones; always forced, as the dashboard does) and toast
        /// the outcome.
        /// </summary>
        /// <param name="vesselIds">Vessel ids, or null for every vessel.</param>
        /// <param name="started">Called on the loop when the server accepted the request.</param>
        public void Start(List<string>? vesselIds, Action? started = null)
        {
            VesselHealthEvaluateRequest body = new VesselHealthEvaluateRequest();
            if (vesselIds != null && vesselIds.Count > 0) body.VesselIds = vesselIds.ToList();
            body.Force = true;
            Starting = true;
            _Screen.Call((c, t) => c.EvaluateVesselHealthAsync(body, t), result =>
            {
                Starting = false;
                if (result == null) return;
                if (!String.IsNullOrEmpty(result.JobId)) Track(result.JobId);
                string text = Describe(result, _Screen.Context.Loc, out NotificationSeverityEnum severity);
                _Screen.Toast(severity, text);
                started?.Invoke();
            }, null, ex =>
            {
                Starting = false;
                _Screen.Toast(NotificationSeverityEnum.Error, _Screen.Tr("Could not start the evaluation: {{message}}", LocalizationArgs.Of("message", ex.Message)));
            });
        }

        /// <summary>
        /// Find the most recent evaluation job: track it while it runs, or remember it as the last run.
        /// </summary>
        public void Discover()
        {
            _Screen.Call((c, t) => c.ListJobsAsync(t), jobs =>
            {
                Job? match = (jobs?.Objects ?? new List<Job>()).Where(j => j.Name == JobName).OrderByDescending(j => j.CreatedUtc).FirstOrDefault();
                if (match == null) return;
                if (IsTerminal(match)) LastJob = match;
                else Track(match.Id);
            }, null, ex => { });
        }

        /// <summary>
        /// Track a job id and poll it until it finishes.
        /// </summary>
        /// <param name="jobId">Job id.</param>
        public void Track(string jobId)
        {
            if (String.IsNullOrEmpty(jobId)) return;
            TrackedJobId = jobId;
            _Timer?.Dispose();
            _Timer = new Timer(_ => _Screen.Context.Dispatcher.Post(Poll), null, 0, Math.Max(100, PollMilliseconds));
        }

        /// <summary>
        /// Poll the tracked job once (also called by the timer).
        /// </summary>
        public void Poll()
        {
            string? id = TrackedJobId;
            if (id == null || _Polling || !_Screen.IsLive) return;
            _Polling = true;
            _Screen.Call((c, t) => c.GetJobAsync(id, t), job =>
            {
                _Polling = false;
                if (job == null || TrackedJobId != id) return;
                if (IsTerminal(job))
                {
                    ActiveJob = null;
                    LastJob = job;
                    TrackedJobId = null;
                    _Timer?.Dispose();
                    _Timer = null;
                    Finished?.Invoke(this, job);
                    return;
                }

                ActiveJob = job;
            }, null, ex => _Polling = false);
        }

        /// <summary>
        /// Progress text: "Evaluating... N%" while running, or "Last evaluation ..." once one finished.
        /// </summary>
        /// <param name="loc">Localizer.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Text, or empty.</returns>
        public string ProgressText(ITextLocalizer loc, DateTime nowUtc)
        {
            if (Running)
            {
                if (ActiveJob == null) return loc.T("Evaluating...");
                int pct = ActiveJob.Progress;
                int filled = Math.Clamp(pct, 0, 100) / 10;
                return loc.T("Evaluating... {{percent}}%", LocalizationArgs.Of("percent", loc.FormatNumber(pct))) + " [" + new string('#', filled) + new string('-', 10 - filled) + "]";
            }

            if (LastJob != null)
                return loc.T("Last evaluation {{when}}", LocalizationArgs.Of("when", loc.FormatRelative(LastJob.CompletedUtc ?? LastJob.LastUpdateUtc, nowUtc)));
            return "";
        }

        /// <summary>
        /// The dashboard's toast for a finished job.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <param name="loc">Localizer.</param>
        /// <param name="severity">Toast severity.</param>
        /// <returns>Text.</returns>
        public static string FinishedText(Job job, ITextLocalizer loc, out NotificationSeverityEnum severity)
        {
            VesselHealthJobResult? result = null;
            if (!String.IsNullOrEmpty(job.ResultJson))
            {
                try { result = Armada.Client.ArmadaJson.Deserialize<VesselHealthJobResult>(job.ResultJson!); }
                catch (Exception) { result = null; }
            }

            if (job.Status == JobStatusEnum.Succeeded)
            {
                severity = result != null && result.Failed > 0 ? NotificationSeverityEnum.Warning : NotificationSeverityEnum.Success;
                return result != null
                    ? loc.T("Evaluation finished: {{evaluated}} evaluated, {{failed}} failed.", LocalizationArgs.Of("evaluated", loc.FormatNumber(result.Evaluated), "failed", loc.FormatNumber(result.Failed)))
                    : loc.T("Evaluation finished.");
            }

            if (job.Status == JobStatusEnum.Failed)
            {
                severity = NotificationSeverityEnum.Error;
                return loc.T("Evaluation failed: {{reason}}", LocalizationArgs.Of("reason", job.ErrorReason ?? loc.T("Unknown error")));
            }

            severity = NotificationSeverityEnum.Warning;
            return loc.T("Evaluation was cancelled.");
        }

        /// <summary>
        /// Stop polling.
        /// </summary>
        public void Dispose()
        {
            _Timer?.Dispose();
            _Timer = null;
        }

        #endregion
    }
}
