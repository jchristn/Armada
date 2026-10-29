namespace Armada.Core.BlackBox
{
    using System;

    /// <summary>
    /// Limits of the black box prevention, forensics and operations surfaces (overlap warnings, reservations, brief
    /// lint, configuration doctor, attempt diff, success flight recorder, combined timeline, voyage cascade, recovery
    /// outcomes). Every collection these surfaces return is bounded by one of these constants.
    /// </summary>
    public static class BlackBoxInsightLimits
    {
        /// <summary>Most overlap warnings returned for one mission.</summary>
        public const int MaxOverlapWarnings = 50;

        /// <summary>Most files listed on one overlap warning or nearby success.</summary>
        public const int MaxWarningFiles = 20;

        /// <summary>Most likely files listed in an overlap preview.</summary>
        public const int MaxLikelyFiles = 200;

        /// <summary>Most likely files (and directories) kept for one mission while comparing.</summary>
        public const int MaxComparedFiles = 2000;

        /// <summary>Most active missions on a vessel compared with a new mission.</summary>
        public const int MaxActiveMissions = 200;

        /// <summary>Most branch reads (active missions without a diff snapshot) for one comparison.</summary>
        public const int MaxBranchLookups = 10;

        /// <summary>Most hotspot directories read for one vessel.</summary>
        public const int MaxHotspots = 1000;

        /// <summary>Most paths read from a brief.</summary>
        public const int MaxBriefPaths = 50;

        /// <summary>Most brief lint findings.</summary>
        public const int MaxLintFindings = 50;

        /// <summary>Longest brief excerpt on a lint finding.</summary>
        public const int MaxLintExcerptChars = 200;

        /// <summary>Most entries whose records the configuration doctor reads.</summary>
        public const int MaxDoctorScan = 200;

        /// <summary>Default configuration doctor window in days.</summary>
        public const int DefaultDoctorWindowDays = 30;

        /// <summary>Longest configuration doctor window in days.</summary>
        public const int MaxDoctorWindowDays = 365;

        /// <summary>Most differences in an attempt diff.</summary>
        public const int MaxDiffFields = 100;

        /// <summary>Most files per side of an attempt diff.</summary>
        public const int MaxDiffFiles = 200;

        /// <summary>Longest value in an attempt diff.</summary>
        public const int MaxDiffValueChars = 500;

        /// <summary>Most nearby successes kept in a record's Related section.</summary>
        public const int MaxNearbySuccesses = 10;

        /// <summary>Most success records read when looking for nearby successes.</summary>
        public const int MaxNearbyCandidates = 200;

        /// <summary>Most changed files kept on a success record.</summary>
        public const int MaxSuccessFiles = 200;

        /// <summary>Most mission.completed events read by one success recorder pass.</summary>
        public const int MaxSuccessScan = 200;

        /// <summary>Most items collected from one timeline source.</summary>
        public const int MaxTimelinePerSource = 200;

        /// <summary>Most items in one combined timeline.</summary>
        public const int MaxTimelineItems = 1000;

        /// <summary>Most timeline source counts (one per source).</summary>
        public const int MaxTimelineSources = 16;

        /// <summary>Most missions in a cascade graph.</summary>
        public const int MaxCascadeNodes = 200;

        /// <summary>Most edges in a cascade graph.</summary>
        public const int MaxCascadeEdges = 1000;

        /// <summary>Most pending recovery outcomes settled by one pass.</summary>
        public const int MaxOutcomeScan = 200;

        /// <summary>Days a recovery outcome is watched before it expires.</summary>
        public const int OutcomeWatchDays = 7;

        /// <summary>Settled runs a path needs before its success rate is used.</summary>
        public const int MinSettledRunsForRate = 3;

        /// <summary>Most path statistics held in the advisor's cache.</summary>
        public const int MaxCachedPathStats = 1000;

        /// <summary>Most runbooks read when linking recovery paths.</summary>
        public const int MaxRunbookCandidates = 200;
    }
}
