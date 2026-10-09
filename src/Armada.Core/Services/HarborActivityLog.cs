namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The Harbor app's activity log, kept two ways from the same entries, each bounded:
    /// <list type="bullet">
    /// <item>The summary view (the default) tells the story at a glance. Summary-level entries get a line without request
    /// IDs and with short paths; a request's line turns into the request and its result. Routine git and file work is not
    /// shown one command at a time: a run of it for the same dock or checkout is one line that counts the commands in
    /// place ("DocConverter msn_mv092791: prepared dock (14 git commands, 2 file writes)"). A failure always gets its own
    /// line and ends the run it interrupts.</item>
    /// <item>The detail view shows every entry as logged, with request IDs and full paths.</item>
    /// </list>
    /// In both, consecutive heartbeats collapse into one line with a count. Classification uses only the entries' typed
    /// fields, never their messages.
    /// </summary>
    public class HarborActivityLog
    {
        #region Public-Members

        /// <summary>
        /// True to show the detail view (every entry); false (the default) for the summary view.
        /// </summary>
        public bool ShowDetails { get; set; } = false;

        /// <summary>
        /// Number of lines in the view being shown.
        /// </summary>
        public int Count
        {
            get { return ShowDetails ? _DetailLines.Count : _Rows.Count; }
        }

        #endregion

        #region Private-Members

        private readonly int _MaxLines;

        private readonly List<string> _DetailLines = new List<string>();
        private int _DetailHeartbeatRun = 0;
        private DateTime _DetailHeartbeatRunStartUtc = DateTime.MinValue;

        private readonly List<HarborActivityRow> _Rows = new List<HarborActivityRow>();
        private readonly Dictionary<string, HarborActivityRow> _PendingRequests = new Dictionary<string, HarborActivityRow>(StringComparer.Ordinal);
        private readonly Dictionary<string, HarborActivityRow> _OpenRuns = new Dictionary<string, HarborActivityRow>(StringComparer.Ordinal);
        private HarborActivityRow? _HeartbeatRow = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="maxLines">Lines kept in each view; older lines are dropped. Minimum 1.</param>
        public HarborActivityLog(int maxLines)
        {
            _MaxLines = maxLines < 1 ? 1 : maxLines;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add an entry to both views.
        /// </summary>
        /// <param name="entry">Entry.</param>
        public void Add(HarborLogEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            AddDetail(entry);
            AddSummary(entry);
        }

        /// <summary>
        /// The most recent lines of the view being shown, oldest first.
        /// </summary>
        /// <param name="maxLines">Maximum number of lines.</param>
        /// <returns>Copy of the lines.</returns>
        public List<string> Recent(int maxLines)
        {
            return Recent(maxLines, ShowDetails);
        }

        /// <summary>
        /// The most recent lines of a view, oldest first.
        /// </summary>
        /// <param name="maxLines">Maximum number of lines.</param>
        /// <param name="details">True for the detail view, false for the summary view.</param>
        /// <returns>Copy of the lines.</returns>
        public List<string> Recent(int maxLines, bool details)
        {
            List<string> lines = details ? _DetailLines : RenderRows();
            int count = Math.Min(Math.Max(maxLines, 0), lines.Count);
            return lines.GetRange(lines.Count - count, count);
        }

        /// <summary>
        /// The view being shown, its lines joined with newlines.
        /// </summary>
        /// <returns>Text.</returns>
        public string ToText()
        {
            return String.Join("\n", ShowDetails ? _DetailLines : RenderRows());
        }

        /// <summary>
        /// Remove every line from both views.
        /// </summary>
        public void Clear()
        {
            _DetailLines.Clear();
            _DetailHeartbeatRun = 0;
            _Rows.Clear();
            _PendingRequests.Clear();
            _OpenRuns.Clear();
            _HeartbeatRow = null;
        }

        #endregion

        #region Private-Methods

        private void AddDetail(HarborLogEntry entry)
        {
            if (entry.IsHeartbeat && _DetailHeartbeatRun > 0 && _DetailLines.Count > 0)
            {
                _DetailHeartbeatRun++;
                _DetailLines[_DetailLines.Count - 1] = entry.ToString() + " (" + _DetailHeartbeatRun + " since "
                    + _DetailHeartbeatRunStartUtc.ToLocalTime().ToString("HH:mm:ss") + ")";
                return;
            }

            if (entry.IsHeartbeat)
            {
                _DetailHeartbeatRun = 1;
                _DetailHeartbeatRunStartUtc = entry.TimestampUtc;
            }
            else
            {
                _DetailHeartbeatRun = 0;
            }

            _DetailLines.Add(entry.ToString());
            if (_DetailLines.Count > _MaxLines) _DetailLines.RemoveRange(0, _DetailLines.Count - _MaxLines);
        }

        private void AddSummary(HarborLogEntry entry)
        {
            if (entry.IsHeartbeat)
            {
                AddHeartbeat(entry);
                return;
            }

            bool failed = entry.Outcome == HarborLogOutcomeEnum.Failed;
            string? key = RunKey(entry);

            if (entry.Level == HarborLogLevelEnum.Detail && !failed)
            {
                if (key != null && entry.Phase == HarborLogPhaseEnum.Result && entry.Outcome == HarborLogOutcomeEnum.Ok
                    && (entry.Category == HarborLogCategoryEnum.Git || entry.Category == HarborLogCategoryEnum.File))
                    CountInRun(entry, key);
                return;
            }

            // A summary line (or a failure) ends the run of work for what it is about: work after it starts a new run below.
            if (key != null) _OpenRuns.Remove(key);

            if (entry.Phase == HarborLogPhaseEnum.Result && !String.IsNullOrEmpty(entry.RequestId)
                && _PendingRequests.TryGetValue(entry.RequestId!, out HarborActivityRow? pending))
            {
                _PendingRequests.Remove(entry.RequestId!);
                pending.PendingRequestId = null;
                pending.Text = entry.Summary;
                return;
            }

            HarborActivityRow row = new HarborActivityRow { TimestampUtc = entry.TimestampUtc, Text = entry.Summary };
            if (entry.Phase == HarborLogPhaseEnum.Request && !String.IsNullOrEmpty(entry.RequestId))
            {
                row.PendingRequestId = entry.RequestId;
                _PendingRequests[entry.RequestId!] = row;
            }

            Append(row);
        }

        private void AddHeartbeat(HarborLogEntry entry)
        {
            if (_HeartbeatRow != null && _Rows.Count > 0 && ReferenceEquals(_Rows[_Rows.Count - 1], _HeartbeatRow))
            {
                _HeartbeatRow.Heartbeats++;
                _HeartbeatRow.TimestampUtc = entry.TimestampUtc;
                _HeartbeatRow.Text = entry.Summary + " (" + _HeartbeatRow.Heartbeats + " since "
                    + _HeartbeatRow.HeartbeatRunStartUtc.ToLocalTime().ToString("HH:mm:ss") + ")";
                return;
            }

            HarborActivityRow row = new HarborActivityRow
            {
                TimestampUtc = entry.TimestampUtc,
                Text = entry.Summary,
                Heartbeats = 1,
                HeartbeatRunStartUtc = entry.TimestampUtc
            };
            Append(row);
            _HeartbeatRow = row;
        }

        private void CountInRun(HarborLogEntry entry, string key)
        {
            if (!_OpenRuns.TryGetValue(key, out HarborActivityRow? run) || run.RunStage != entry.Stage || run.RunPathKind != entry.PathKind)
            {
                run = new HarborActivityRow
                {
                    TimestampUtc = entry.TimestampUtc,
                    RunKey = key,
                    RunLabel = RunLabel(entry),
                    RunStage = entry.Stage,
                    RunPathKind = entry.PathKind
                };
                _OpenRuns[key] = run;
                Append(run);
            }

            if (entry.Category == HarborLogCategoryEnum.Git) run.GitCommands++;
            else run.FileWrites++;
            run.Text = RunText(run);
        }

        private void Append(HarborActivityRow row)
        {
            _Rows.Add(row);
            if (_Rows.Count <= _MaxLines) return;

            int drop = _Rows.Count - _MaxLines;
            for (int i = 0; i < drop; i++)
            {
                HarborActivityRow dropped = _Rows[i];
                if (dropped.PendingRequestId != null
                    && _PendingRequests.TryGetValue(dropped.PendingRequestId, out HarborActivityRow? pending)
                    && ReferenceEquals(pending, dropped))
                    _PendingRequests.Remove(dropped.PendingRequestId);
                if (dropped.RunKey != null
                    && _OpenRuns.TryGetValue(dropped.RunKey, out HarborActivityRow? run)
                    && ReferenceEquals(run, dropped))
                    _OpenRuns.Remove(dropped.RunKey);
                if (ReferenceEquals(dropped, _HeartbeatRow)) _HeartbeatRow = null;
            }

            _Rows.RemoveRange(0, drop);
        }

        private List<string> RenderRows()
        {
            List<string> lines = new List<string>(_Rows.Count);
            foreach (HarborActivityRow row in _Rows) lines.Add(row.ToString());
            return lines;
        }

        /// <summary>
        /// What a run of work is about: a mission (its dock), a vessel's checkout, or a directory; null when the entry
        /// names none of them.
        /// </summary>
        private static string? RunKey(HarborLogEntry entry)
        {
            if (!String.IsNullOrWhiteSpace(entry.MissionId)) return "mission:" + entry.MissionId;
            if (entry.PathKind == HarborLogPathKindEnum.Checkout && !String.IsNullOrWhiteSpace(entry.VesselName)) return "vessel:" + entry.VesselName;
            if (!String.IsNullOrWhiteSpace(entry.Path)) return "path:" + entry.Path;
            if (!String.IsNullOrWhiteSpace(entry.VesselName)) return "vessel:" + entry.VesselName;
            return null;
        }

        private static string RunLabel(HarborLogEntry entry)
        {
            string? label = HarborLogFormat.SubjectLabel(entry.VesselName, entry.MissionId);
            if (label != null) return label;
            string path = HarborLogFormat.ShortPath(entry.Path, entry.PathKind, entry.VesselName, entry.MissionId);
            return path.Length > 0 ? path : "Harbor";
        }

        private static string RunText(HarborActivityRow run)
        {
            string verb;
            switch (run.RunStage)
            {
                case HarborLogStageEnum.Preparing:
                    verb = "prepared dock";
                    break;
                case HarborLogStageEnum.Running:
                    verb = "worked in dock";
                    break;
                case HarborLogStageEnum.Finishing:
                    verb = "landed and cleaned up";
                    break;
                default:
                    if (run.RunPathKind == HarborLogPathKindEnum.Checkout) verb = "worked in checkout";
                    else if (run.FileWrites == 0) verb = "git work";
                    else if (run.GitCommands == 0) verb = "file work";
                    else verb = "git and file work";
                    break;
            }

            List<string> counts = new List<string>();
            if (run.GitCommands > 0) counts.Add(HarborLogFormat.Count(run.GitCommands, "git command", "git commands"));
            if (run.FileWrites > 0) counts.Add(HarborLogFormat.Count(run.FileWrites, "file write", "file writes"));
            return run.RunLabel + ": " + verb + " (" + String.Join(", ", counts) + ")";
        }

        #endregion
    }
}
