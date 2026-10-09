namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Harbor;

    /// <summary>
    /// Builds the Harbor link's log entries with their typed fields: level, category, outcome, request and result
    /// pairing, and what each is about (vessel, mission, job, directory, and a dock's stage). It learns what directories
    /// are from the Admiral's dock and launch requests, so git and file work in a mission dock is attributed to its
    /// vessel and mission without reading a path or a message. One instance should outlive link sessions (a dock made in
    /// one session can be reclaimed in the next). Thread-safe.
    /// </summary>
    public class HarborLogClassifier
    {
        #region Private-Members

        private const int _MaxTracked = 2000;

        private readonly object _Lock = new object();
        private readonly Dictionary<string, HarborLogSubject> _Places = new Dictionary<string, HarborLogSubject>(StringComparer.Ordinal);
        private readonly Dictionary<string, HarborJobInfo> _Jobs = new Dictionary<string, HarborJobInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, HarborLogSubject> _JobPlaces = new Dictionary<string, HarborLogSubject>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborLogClassifier()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// An entry about the link itself (a handshake, a deferred launch).
        /// </summary>
        /// <param name="direction">Direction.</param>
        /// <param name="message">Message.</param>
        /// <param name="outcome">Outcome.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry Link(HarborLogDirection direction, string message, HarborLogOutcomeEnum outcome = HarborLogOutcomeEnum.None)
        {
            return new HarborLogEntry(direction, message) { Category = HarborLogCategoryEnum.Link, Outcome = outcome };
        }

        /// <summary>
        /// A heartbeat entry; the activity log collapses a run of them into one line.
        /// </summary>
        /// <returns>The entry.</returns>
        public HarborLogEntry Heartbeat()
        {
            return new HarborLogEntry(HarborLogDirection.Out, "Heartbeat") { Category = HarborLogCategoryEnum.Link, IsHeartbeat = true };
        }

        /// <summary>
        /// A delegated command received. Routine git work is Detail; a check run or an operator's command is Summary.
        /// </summary>
        /// <param name="git">The request.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry GitRequest(HarborGitRequest git)
        {
            if (git == null) throw new ArgumentNullException(nameof(git));
            HostCommandKindEnum kind = git.Kind ?? HostCommandKindEnum.Routine;
            HarborLogEntry entry = new HarborLogEntry(HarborLogDirection.In, "Work: " + git.Executable + " " + String.Join(" ", git.Arguments)
                + (String.IsNullOrEmpty(git.WorkingDirectory) ? " (in the Harbor's current directory)" : " (in " + git.WorkingDirectory + ")")
                + " [req " + git.RequestId + "]");
            entry.Category = CategoryFor(kind);
            entry.Level = kind == HostCommandKindEnum.Routine ? HarborLogLevelEnum.Detail : HarborLogLevelEnum.Summary;
            entry.Phase = HarborLogPhaseEnum.Request;
            entry.RequestId = git.RequestId;
            ApplyPlace(entry, git.WorkingDirectory, FindPlace(git.WorkingDirectory));
            entry.Summary = DescribeCommand(git, kind, entry) + " (running)";
            return entry;
        }

        /// <summary>
        /// A delegated command's result. A non-zero exit is a failure unless the request declared it expected; a timeout
        /// is always a failure. Routine work that succeeded is Detail; everything else is Summary.
        /// </summary>
        /// <param name="git">The request.</param>
        /// <param name="result">The result.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry GitResult(HarborGitRequest git, HostCommandResult result)
        {
            if (git == null) throw new ArgumentNullException(nameof(git));
            if (result == null) throw new ArgumentNullException(nameof(result));
            HostCommandKindEnum kind = git.Kind ?? HostCommandKindEnum.Routine;
            bool expected = result.ExitCode != 0 && git.ExpectedExitCodes != null && git.ExpectedExitCodes.Contains(result.ExitCode);
            bool failed = result.TimedOut || (result.ExitCode != 0 && !expected);

            HarborLogEntry entry = new HarborLogEntry(HarborLogDirection.Out, "Result: exit " + result.ExitCode + (result.TimedOut ? " (timed out)" : "") + " [req " + git.RequestId + "]");
            entry.Category = CategoryFor(kind);
            entry.Outcome = failed ? HarborLogOutcomeEnum.Failed : HarborLogOutcomeEnum.Ok;
            entry.Level = kind == HostCommandKindEnum.Routine && !failed ? HarborLogLevelEnum.Detail : HarborLogLevelEnum.Summary;
            entry.Phase = HarborLogPhaseEnum.Result;
            entry.RequestId = git.RequestId;
            ApplyPlace(entry, git.WorkingDirectory, FindPlace(git.WorkingDirectory));

            string outcome;
            if (result.TimedOut) outcome = "failed (timed out)";
            else if (failed) outcome = "failed (exit " + result.ExitCode + ")";
            else if (expected) outcome = "exit " + result.ExitCode + " (expected)";
            else outcome = "exit 0";
            entry.Summary = DescribeCommand(git, kind, entry) + " -> " + outcome;
            return entry;
        }

        /// <summary>
        /// A dock request received (Detail: its result is the line worth reading).
        /// </summary>
        /// <param name="dock">The request.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry DockRequest(HarborDockRequest dock)
        {
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            HarborLogEntry entry = new HarborLogEntry(HarborLogDirection.In, "Dock " + dock.Operation + " for vessel " + dock.VesselName
                + (String.IsNullOrEmpty(dock.BranchName) ? "" : " branch " + dock.BranchName)
                + (String.IsNullOrEmpty(dock.WorktreePath) ? "" : " at " + dock.WorktreePath)
                + " [req " + dock.RequestId + "]");
            entry.Category = HarborLogCategoryEnum.Dock;
            entry.Level = HarborLogLevelEnum.Detail;
            entry.Phase = HarborLogPhaseEnum.Request;
            entry.RequestId = dock.RequestId;
            ApplyDock(entry, dock);
            entry.Summary = Prefix(entry) + "dock " + OperationVerb(dock.Operation) + " requested";
            return entry;
        }

        /// <summary>
        /// A dock request's result. A provisioned dock and a checkout are remembered, so work in them is attributed to
        /// their vessel and mission; a reclaimed dock is forgotten. Resolving a checkout is Detail; a dock made or
        /// removed, and any failure, is Summary.
        /// </summary>
        /// <param name="dock">The request.</param>
        /// <param name="result">The result.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry DockResult(HarborDockRequest dock, HarborDockResult result)
        {
            if (dock == null) throw new ArgumentNullException(nameof(dock));
            if (result == null) throw new ArgumentNullException(nameof(result));

            if (result.Success) Remember(dock, result);

            HarborLogEntry entry = new HarborLogEntry(HarborLogDirection.Out, DescribeDockResult(dock, result) + " [req " + dock.RequestId + "]");
            entry.Category = HarborLogCategoryEnum.Dock;
            entry.Outcome = result.Success ? HarborLogOutcomeEnum.Ok : HarborLogOutcomeEnum.Failed;
            entry.Level = result.Success && dock.Operation == HarborDockOperationEnum.Resolve ? HarborLogLevelEnum.Detail : HarborLogLevelEnum.Summary;
            entry.Phase = HarborLogPhaseEnum.Result;
            entry.RequestId = dock.RequestId;
            ApplyDock(entry, dock);
            if (dock.Operation == HarborDockOperationEnum.Provision && result.Success)
            {
                entry.Path = result.WorktreePath;
                entry.PathKind = HarborLogPathKindEnum.Dock;
                entry.Stage = HarborLogStageEnum.Preparing;
            }

            if (!result.Success)
                entry.Summary = Prefix(entry) + "dock " + OperationVerb(dock.Operation) + " failed: " + (result.Message ?? "no reason given");
            else if (dock.Operation == HarborDockOperationEnum.Provision)
                entry.Summary = Prefix(entry) + "dock ready from " + DescribeSourceShort(result);
            else if (dock.Operation == HarborDockOperationEnum.Reclaim)
                entry.Summary = Prefix(entry) + "dock removed";
            else
                entry.Summary = Prefix(entry) + "served from " + DescribeSourceShort(result);

            if (result.Success && dock.Operation == HarborDockOperationEnum.Reclaim) Forget(result.WorktreePath ?? dock.WorktreePath);
            return entry;
        }

        /// <summary>
        /// A file operation's result (writes, git excludes, and failures are logged; reads and stats are not). Detail
        /// unless it failed.
        /// </summary>
        /// <param name="file">The request.</param>
        /// <param name="result">The result.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry FileResult(HarborFileRequest file, HarborFileResult result)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (result == null) throw new ArgumentNullException(nameof(result));
            HarborLogEntry entry = new HarborLogEntry(HarborLogDirection.In, "File " + file.Operation + " " + file.Path
                + (result.Success ? "" : " failed: " + result.Message) + " [req " + file.RequestId + "]");
            entry.Category = HarborLogCategoryEnum.File;
            entry.Outcome = result.Success ? HarborLogOutcomeEnum.Ok : HarborLogOutcomeEnum.Failed;
            entry.Level = result.Success ? HarborLogLevelEnum.Detail : HarborLogLevelEnum.Summary;
            entry.Phase = HarborLogPhaseEnum.Result;
            entry.RequestId = file.RequestId;

            string? directory = !String.IsNullOrWhiteSpace(file.Root) ? file.Root : ParentOf(file.Path);
            HarborLogSubject? place = FindPlace(directory);
            ApplyPlace(entry, directory, place);
            if (place == null && !String.IsNullOrWhiteSpace(file.VesselName)) entry.VesselName = file.VesselName;
            entry.Summary = Prefix(entry) + "file " + file.Operation + " " + HarborLogFormat.HomeRelative(file.Path)
                + (result.Success ? "" : " failed: " + (result.Message ?? "no reason given"));
            return entry;
        }

        /// <summary>
        /// A launch received. Records the job, and marks its mission dock as running.
        /// </summary>
        /// <param name="launch">The request.</param>
        /// <param name="resolvedDirectory">Where the job runs on this host (the job runner's answer), or null.</param>
        /// <param name="nowUtc">Current time, UTC.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry Launch(HarborLaunchRequest launch, string? resolvedDirectory, DateTime nowUtc)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            string requested = launch.WorkingDirectory ?? String.Empty;
            HarborLogSubject where = new HarborLogSubject { Path = resolvedDirectory ?? String.Empty, PathKind = HarborLogPathKindEnum.None };
            if (!String.IsNullOrWhiteSpace(resolvedDirectory))
            {
                if (String.Equals(resolvedDirectory, requested, StringComparison.Ordinal))
                {
                    HarborLogSubject? known = FindPlace(resolvedDirectory);
                    if (known != null)
                    {
                        where = known.Clone();
                        where.Path = resolvedDirectory!;
                    }
                    else
                    {
                        where.PathKind = HarborLogPathKindEnum.Other;
                    }
                }
                else
                {
                    where.PathKind = HarborLogPathKindEnum.Scratch;
                }
            }

            HarborJobInfo job = HarborJobInfo.FromLaunch(launch, nowUtc);
            if (where.PathKind == HarborLogPathKindEnum.Dock) where.Stage = HarborLogStageEnum.Running;
            if (where.MissionId == null && job.MissionId != null) where.MissionId = job.MissionId;
            lock (_Lock)
            {
                if (where.PathKind == HarborLogPathKindEnum.Dock && _Places.TryGetValue(Key(where.Path), out HarborLogSubject? dock))
                    dock.Stage = HarborLogStageEnum.Running;
                TrimIfFull(_Jobs);
                TrimIfFull(_JobPlaces);
                _Jobs[launch.JobId] = job;
                _JobPlaces[launch.JobId] = where.Clone();
            }

            HarborLogEntry entry = new HarborLogEntry(HarborLogDirection.In, "Launch job " + launch.JobId + " (runtime " + launch.Runtime + ") " + DescribeLaunchDirectory(requested, resolvedDirectory));
            ApplyJob(entry, job, where);
            entry.Phase = HarborLogPhaseEnum.Request;
            entry.RequestId = launch.JobId;
            string location = where.PathKind == HarborLogPathKindEnum.None
                ? "with no working directory"
                : "in " + HarborLogFormat.ShortPath(where.Path, where.PathKind, where.VesselName, where.MissionId);
            entry.Summary = Title(job) + " launching " + location + " (" + launch.Runtime + ")";
            return entry;
        }

        /// <summary>
        /// A launch refused because this Harbor cannot launch captains.
        /// </summary>
        /// <param name="launch">The request.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry LaunchRefused(HarborLaunchRequest launch)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            HarborLogEntry entry = JobEntry(launch.JobId, "Refused launch " + launch.JobId + " (captain delegation not enabled)", true);
            entry.Outcome = HarborLogOutcomeEnum.Failed;
            entry.Phase = HarborLogPhaseEnum.Result;
            entry.RequestId = launch.JobId;
            entry.Summary = TitleOf(launch.JobId) + " refused: this Harbor cannot launch captains";
            return entry;
        }

        /// <summary>
        /// A job started.
        /// </summary>
        /// <param name="launch">The launch.</param>
        /// <param name="processId">Process ID.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry Started(HarborLaunchRequest launch, int processId)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            HarborLogEntry entry = JobEntry(launch.JobId, "Started job " + launch.JobId + " (pid " + processId + ")", false);
            entry.Outcome = HarborLogOutcomeEnum.Ok;
            entry.Phase = HarborLogPhaseEnum.Result;
            entry.RequestId = launch.JobId;
            entry.Summary = TitleOf(launch.JobId) + " started"
                + (String.IsNullOrWhiteSpace(entry.VesselName) ? "" : " in " + entry.VesselName)
                + " (" + launch.Runtime + ")";
            return entry;
        }

        /// <summary>
        /// A job exited: ok on exit code 0, failed otherwise. Marks its mission dock as finishing and forgets the job.
        /// </summary>
        /// <param name="launch">The launch.</param>
        /// <param name="exitCode">Exit code.</param>
        /// <param name="durationMs">Runtime in milliseconds.</param>
        /// <param name="timeToFirstOutputMs">Time to first output in milliseconds, or null.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry Exited(HarborLaunchRequest launch, int exitCode, long durationMs, long? timeToFirstOutputMs)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            lock (_Lock)
            {
                if (_JobPlaces.TryGetValue(launch.JobId, out HarborLogSubject? where))
                {
                    where.Stage = where.PathKind == HarborLogPathKindEnum.Dock ? HarborLogStageEnum.Finishing : where.Stage;
                    if (where.PathKind == HarborLogPathKindEnum.Dock && _Places.TryGetValue(Key(where.Path), out HarborLogSubject? dock))
                        dock.Stage = HarborLogStageEnum.Finishing;
                }
            }

            HarborLogEntry entry = JobEntry(launch.JobId, "Exited job " + launch.JobId + " (code " + exitCode
                + ", runtime " + HarborLogFormat.Duration(durationMs)
                + (timeToFirstOutputMs.HasValue ? ", first output " + HarborLogFormat.Duration(timeToFirstOutputMs.Value) : "") + ")", false);
            entry.Outcome = exitCode == 0 ? HarborLogOutcomeEnum.Ok : HarborLogOutcomeEnum.Failed;
            entry.Summary = TitleOf(launch.JobId)
                + (exitCode == 0 ? " exited ok" : " exited with code " + exitCode)
                + " in " + HarborLogFormat.Duration(durationMs)
                + (timeToFirstOutputMs.HasValue ? " (first output " + HarborLogFormat.Duration(timeToFirstOutputMs.Value) + ")" : "");
            ForgetJob(launch.JobId);
            return entry;
        }

        /// <summary>
        /// A launch that failed to start.
        /// </summary>
        /// <param name="launch">The launch.</param>
        /// <param name="message">Why.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry LaunchFailed(HarborLaunchRequest launch, string message)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            HarborLogEntry entry = JobEntry(launch.JobId, "Launch " + launch.JobId + " failed: " + message, true);
            entry.Outcome = HarborLogOutcomeEnum.Failed;
            entry.Phase = HarborLogPhaseEnum.Result;
            entry.RequestId = launch.JobId;
            entry.Summary = TitleOf(launch.JobId) + " failed to start: " + message;
            ForgetJob(launch.JobId);
            return entry;
        }

        /// <summary>
        /// A stop request for a job.
        /// </summary>
        /// <param name="jobId">Job ID.</param>
        /// <returns>The entry.</returns>
        public HarborLogEntry Stop(string jobId)
        {
            HarborLogEntry entry = JobEntry(jobId, "Stop job " + jobId, false);
            entry.Direction = HarborLogDirection.In;
            entry.Summary = "Stop requested for " + TitleOf(jobId);
            return entry;
        }

        #endregion

        #region Private-Methods

        private static HarborLogCategoryEnum CategoryFor(HostCommandKindEnum kind)
        {
            switch (kind)
            {
                case HostCommandKindEnum.CheckRun: return HarborLogCategoryEnum.CheckRun;
                case HostCommandKindEnum.Command: return HarborLogCategoryEnum.Command;
                default: return HarborLogCategoryEnum.Git;
            }
        }

        private static string DescribeCommand(HarborGitRequest git, HostCommandKindEnum kind, HarborLogEntry entry)
        {
            string command = HarborLogFormat.Command(git.Executable, git.Arguments);
            string where = HarborLogFormat.ShortPath(entry.Path, entry.PathKind, entry.VesselName, entry.MissionId);
            if (kind == HostCommandKindEnum.CheckRun)
                return "Check run" + (String.IsNullOrWhiteSpace(git.Label) ? "" : " " + git.Label!.Trim()) + (where.Length > 0 ? " in " + where : "") + ": " + command;
            if (kind == HostCommandKindEnum.Command)
                return (String.IsNullOrWhiteSpace(git.Label) ? "Command" : git.Label!.Trim()) + (where.Length > 0 ? " in " + where : "") + ": " + command;
            string? label = HarborLogFormat.SubjectLabel(entry.VesselName, entry.MissionId);
            if (label == null && where.Length > 0) label = where;
            return (label != null ? label + ": " : "") + command;
        }

        private static string Prefix(HarborLogEntry entry)
        {
            string? label = HarborLogFormat.SubjectLabel(entry.VesselName, entry.MissionId);
            return label != null ? label + ": " : "";
        }

        private static string OperationVerb(HarborDockOperationEnum operation)
        {
            switch (operation)
            {
                case HarborDockOperationEnum.Provision: return "provision";
                case HarborDockOperationEnum.Reclaim: return "removal";
                default: return "resolve";
            }
        }

        private void ApplyDock(HarborLogEntry entry, HarborDockRequest dock)
        {
            entry.VesselName = String.IsNullOrWhiteSpace(dock.VesselName) ? null : dock.VesselName;
            if (dock.Operation == HarborDockOperationEnum.Provision)
            {
                entry.MissionId = String.IsNullOrWhiteSpace(dock.DockName) ? null : dock.DockName;
                entry.PathKind = HarborLogPathKindEnum.Dock;
                entry.Stage = HarborLogStageEnum.Preparing;
            }
            else if (dock.Operation == HarborDockOperationEnum.Reclaim)
            {
                HarborLogSubject? place = FindPlace(dock.WorktreePath);
                ApplyPlace(entry, dock.WorktreePath, place);
                if (place == null) entry.PathKind = HarborLogPathKindEnum.Dock;
                if (String.IsNullOrWhiteSpace(entry.VesselName) && !String.IsNullOrWhiteSpace(dock.VesselName)) entry.VesselName = dock.VesselName;
            }
        }

        private void Remember(HarborDockRequest dock, HarborDockResult result)
        {
            lock (_Lock)
            {
                TrimIfFull(_Places);
                foreach (string? checkout in new string?[] { result.CheckoutPath, result.RepositoryPath })
                {
                    if (String.IsNullOrWhiteSpace(checkout)) continue;
                    _Places[Key(checkout!)] = new HarborLogSubject
                    {
                        VesselName = String.IsNullOrWhiteSpace(dock.VesselName) ? null : dock.VesselName,
                        Path = checkout!,
                        PathKind = HarborLogPathKindEnum.Checkout
                    };
                }

                if (dock.Operation == HarborDockOperationEnum.Provision && !String.IsNullOrWhiteSpace(result.WorktreePath))
                {
                    _Places[Key(result.WorktreePath!)] = new HarborLogSubject
                    {
                        VesselName = String.IsNullOrWhiteSpace(dock.VesselName) ? null : dock.VesselName,
                        MissionId = String.IsNullOrWhiteSpace(dock.DockName) ? null : dock.DockName,
                        Path = result.WorktreePath!,
                        PathKind = HarborLogPathKindEnum.Dock,
                        Stage = HarborLogStageEnum.Preparing
                    };
                }
            }
        }

        private void Forget(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return;
            lock (_Lock) { _Places.Remove(Key(path!)); }
        }

        private void ForgetJob(string jobId)
        {
            lock (_Lock)
            {
                _Jobs.Remove(jobId);
                _JobPlaces.Remove(jobId);
            }
        }

        /// <summary>
        /// The remembered directory that holds <paramref name="directory"/> (itself or its nearest remembered parent), as a
        /// copy, or null.
        /// </summary>
        private HarborLogSubject? FindPlace(string? directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) return null;
            lock (_Lock)
            {
                if (_Places.Count == 0) return null;
                string? current = Key(directory!);
                while (!String.IsNullOrEmpty(current))
                {
                    if (_Places.TryGetValue(current, out HarborLogSubject? place)) return place.Clone();
                    string? parent = System.IO.Path.GetDirectoryName(current);
                    if (parent == null || String.Equals(parent, current, StringComparison.Ordinal)) break;
                    current = Key(parent);
                }
            }

            return null;
        }

        private static void ApplyPlace(HarborLogEntry entry, string? directory, HarborLogSubject? place)
        {
            entry.Path = String.IsNullOrWhiteSpace(directory) ? null : directory;
            if (place == null)
            {
                entry.PathKind = entry.Path == null ? HarborLogPathKindEnum.None : HarborLogPathKindEnum.Other;
                return;
            }

            entry.VesselName = place.VesselName;
            entry.MissionId = place.MissionId;
            entry.PathKind = place.PathKind;
            entry.Stage = place.Stage;
        }

        private HarborLogEntry JobEntry(string jobId, string message, bool failed)
        {
            HarborJobInfo? job;
            HarborLogSubject? where;
            lock (_Lock)
            {
                _Jobs.TryGetValue(jobId, out job);
                _JobPlaces.TryGetValue(jobId, out where);
                where = where?.Clone();
            }

            HarborLogEntry entry = new HarborLogEntry(HarborLogDirection.Out, message);
            if (job != null) ApplyJob(entry, job, where ?? new HarborLogSubject { PathKind = HarborLogPathKindEnum.None });
            else
            {
                entry.Category = HarborLogCategoryEnum.Job;
                entry.JobId = jobId;
            }

            if (failed) entry.Outcome = HarborLogOutcomeEnum.Failed;
            return entry;
        }

        private static void ApplyJob(HarborLogEntry entry, HarborJobInfo job, HarborLogSubject where)
        {
            entry.Category = HarborLogCategoryEnum.Job;
            entry.Level = HarborLogLevelEnum.Summary;
            entry.JobId = job.JobId;
            entry.MissionId = where.MissionId ?? job.MissionId;
            entry.VesselName = where.VesselName;
            entry.Path = String.IsNullOrWhiteSpace(where.Path) ? null : where.Path;
            entry.PathKind = where.PathKind;
            entry.Stage = where.Stage;
        }

        private string TitleOf(string jobId)
        {
            HarborJobInfo? job;
            lock (_Lock) { _Jobs.TryGetValue(jobId, out job); }
            return job != null ? Title(job) : "Job " + HarborLogFormat.ShortId(jobId);
        }

        private static string Title(HarborJobInfo job)
        {
            if (job.Kind == HarborJobKindEnum.Mission && job.MissionId != null) return "Mission " + HarborLogFormat.ShortId(job.MissionId);
            return job.KindName();
        }

        private static string DescribeDockResult(HarborDockRequest request, HarborDockResult result)
        {
            if (!result.Success) return "Dock " + request.Operation + " failed: " + (result.Message ?? "no reason given");
            switch (request.Operation)
            {
                case HarborDockOperationEnum.Provision:
                    return "Dock ready at " + result.WorktreePath + " (" + DescribeSource(result) + ")";
                case HarborDockOperationEnum.Reclaim:
                    return "Dock removed at " + result.WorktreePath;
                default:
                    return "Vessel " + request.VesselName + " served from " + DescribeSource(result);
            }
        }

        private static string DescribeSource(HarborDockResult result)
        {
            switch (result.Source)
            {
                case HarborRepositorySourceEnum.Mapped:
                    return "mapped checkout " + result.CheckoutPath;
                case HarborRepositorySourceEnum.Discovered:
                    return "discovered checkout " + result.CheckoutPath;
                case HarborRepositorySourceEnum.Clone:
                    return "Harbor clone " + result.RepositoryPath;
                default:
                    return "no repository";
            }
        }

        private static string DescribeSourceShort(HarborDockResult result)
        {
            switch (result.Source)
            {
                case HarborRepositorySourceEnum.Mapped:
                    return "mapped checkout " + HarborLogFormat.HomeRelative(result.CheckoutPath);
                case HarborRepositorySourceEnum.Discovered:
                    return "discovered checkout " + HarborLogFormat.HomeRelative(result.CheckoutPath);
                case HarborRepositorySourceEnum.Clone:
                    return "Harbor clone";
                default:
                    return "no repository";
            }
        }

        /// <summary>
        /// Describe where a launch runs on this host, for the detail line: the requested working directory, the per-job
        /// scratch directory the job runner creates (naming the requested path when it does not exist here), or that the
        /// request names no working directory.
        /// </summary>
        private static string DescribeLaunchDirectory(string requested, string? resolved)
        {
            if (String.IsNullOrWhiteSpace(resolved))
                return "with no working directory";
            if (String.Equals(resolved, requested, StringComparison.Ordinal))
                return "in " + resolved;
            if (String.IsNullOrWhiteSpace(requested))
                return "in scratch directory " + resolved;
            return "in scratch directory " + resolved + " (requested " + requested + " does not exist on this host)";
        }

        private static string? ParentOf(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return null;
            try
            {
                return System.IO.Path.GetDirectoryName(path);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static string Key(string path)
        {
            string trimmed = path.Trim();
            string stripped = trimmed.TrimEnd('/', '\\');
            return stripped.Length == 0 ? trimmed : stripped;
        }

        private static void TrimIfFull<T>(Dictionary<string, T> map)
        {
            // Docks and jobs are forgotten when they end; this only bounds what a link that never reports their end leaves.
            if (map.Count >= _MaxTracked) map.Clear();
        }

        #endregion
    }
}
