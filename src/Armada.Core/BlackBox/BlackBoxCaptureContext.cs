namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Armada.Core.BlackBox;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Everything a mission failure site knows, handed to IBlackBoxCapture.CaptureMissionFailureAsync. Fill what you have; the capture loads the rest. Mission is the mission after the failure write.
    /// </summary>
    public class BlackBoxCaptureContext
    {
        /// <summary>
        /// The mission after the failure write (required).
        /// </summary>
        public Mission? Mission { get; set; } = null;

        /// <summary>
        /// Status before the failure write.
        /// </summary>
        public MissionStatusEnum? StatusBefore { get; set; } = null;

        /// <summary>
        /// Failure step (the phase is derived from it).
        /// </summary>
        public BlackBoxStepEnum Step { get; set; } = BlackBoxStepEnum.Unknown;

        /// <summary>
        /// Inline at a failure site; Sweeper and Backfill set by the sweeper.
        /// </summary>
        public BlackBoxCaptureModeEnum CaptureMode { get; set; } = BlackBoxCaptureModeEnum.Inline;

        /// <summary>
        /// Exception, or null.
        /// </summary>
        public Exception? Exception { get; set; } = null;

        /// <summary>
        /// Agent exit code, or null.
        /// </summary>
        public int? ExitCode { get; set; } = null;

        /// <summary>
        /// Runtime exit classification, or null.
        /// </summary>
        public RuntimeFailureKindEnum? RuntimeFailureKind { get; set; } = null;

        /// <summary>
        /// Failing command, or null.
        /// </summary>
        public string? FailingCommand { get; set; } = null;

        /// <summary>
        /// Failing command's exit code, or null.
        /// </summary>
        public int? CommandExitCode { get; set; } = null;

        /// <summary>
        /// Failing command's output (the tail is kept).
        /// </summary>
        public string? CommandOutput { get; set; } = null;

        /// <summary>
        /// Git facts, or null.
        /// </summary>
        public BlackBoxGitFacts? GitFacts { get; set; } = null;

        /// <summary>
        /// Scoped files (scope violations).
        /// </summary>
        public List<string> ScopedFiles
        {
            get => _ScopedFiles;
            set => _ScopedFiles = value ?? new List<string>();
        }

        /// <summary>
        /// Out-of-scope files (scope violations).
        /// </summary>
        public List<string> OutOfScopeFiles
        {
            get => _OutOfScopeFiles;
            set => _OutOfScopeFiles = value ?? new List<string>();
        }

        /// <summary>
        /// Protected-path findings (boundary violations).
        /// </summary>
        public List<string> BoundaryFindings
        {
            get => _BoundaryFindings;
            set => _BoundaryFindings = value ?? new List<string>();
        }

        /// <summary>
        /// Stage check results.
        /// </summary>
        public List<StageCheckResult> CheckResults
        {
            get => _CheckResults;
            set => _CheckResults = value ?? new List<StageCheckResult>();
        }

        /// <summary>
        /// Merge-queue entry, or null.
        /// </summary>
        public string? MergeEntryId { get; set; } = null;

        /// <summary>
        /// Incident, or null.
        /// </summary>
        public string? IncidentId { get; set; } = null;

        /// <summary>
        /// Stage outcome, or null.
        /// </summary>
        public string? StageOutcomeId { get; set; } = null;

        /// <summary>
        /// Suspected cause, or null.
        /// </summary>
        public FailureCauseEnum? SuspectedCause { get; set; } = null;

        /// <summary>
        /// Rule that assigned the cause, or null.
        /// </summary>
        public string? CauseRuleId { get; set; } = null;

        /// <summary>
        /// Why it failed, when the site does not write Mission.FailureReason (a non-terminal entry such as a provisioning
        /// failure that returns the mission to Pending), or null. Mission.FailureReason wins when set.
        /// </summary>
        public string? Reason { get; set; } = null;

        /// <summary>
        /// Free-text note added to the record's capture notes, or null.
        /// </summary>
        public string? Note { get; set; } = null;

        /// <summary>
        /// The vessel when the site already holds it (saves a read), or null.
        /// </summary>
        public Vessel? Vessel { get; set; } = null;

        /// <summary>
        /// The captain when the site holds it, or null.
        /// </summary>
        public Captain? Captain { get; set; } = null;

        /// <summary>
        /// The captain's id when the site cleared Mission.CaptainId before the failure write and holds no Captain row,
        /// or null.
        /// </summary>
        public string? CaptainId { get; set; } = null;

        /// <summary>
        /// The voyage when the site holds it, or null.
        /// </summary>
        public Voyage? Voyage { get; set; } = null;

        /// <summary>
        /// The dock when the site holds it, or null.
        /// </summary>
        public Dock? Dock { get; set; } = null;

        /// <summary>
        /// Calling method ([CallerMemberName]).
        /// </summary>
        public string? CallerMember { get; set; } = null;

        /// <summary>
        /// Calling source file ([CallerFilePath]); trimmed to a repository-relative path.
        /// </summary>
        public string? CallerFile { get; set; } = null;

        /// <summary>
        /// Calling line ([CallerLineNumber]).
        /// </summary>
        public int? CallerLine { get; set; } = null;

        /// <summary>
        /// Component (class) name; defaults to the caller file name.
        /// </summary>
        public string? Component { get; set; } = null;

        private List<string> _ScopedFiles = new List<string>();
        private List<string> _OutOfScopeFiles = new List<string>();
        private List<string> _BoundaryFindings = new List<string>();
        private List<StageCheckResult> _CheckResults = new List<StageCheckResult>();

        /// <summary>
        /// Instantiate.
        /// </summary>
        public BlackBoxCaptureContext()
        {
        }
    }
}
