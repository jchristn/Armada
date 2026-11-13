namespace Armada.Core.BlackBox
{
    using System;
    using Armada.Core.Services;

    /// <summary>
    /// Hard limits of the black box (docs/BLACKBOX.md, "Size budget"): the most items each record collection holds, the
    /// longest each text field may be, and the byte budgets of records, briefs and object reads. Collections over a limit
    /// are cut by <see cref="BlackBoxRecordBounder"/> with the original size recorded in the record's Truncation section;
    /// nothing is dropped silently. Thread-safe (constants only).
    /// </summary>
    public static class BlackBoxLimits
    {
        /// <summary>Status timeline entries per record.</summary>
        public const int MaxTimeline = 100;

        /// <summary>Armada stack frames per mechanism.</summary>
        public const int MaxStackFrames = 20;

        /// <summary>Dirty paths per workspace.</summary>
        public const int MaxDirtyPaths = 200;

        /// <summary>Conflicted files per conflict section.</summary>
        public const int MaxConflictedFiles = 500;

        /// <summary>Conflicted directories per conflict section.</summary>
        public const int MaxConflictedDirectories = 100;

        /// <summary>Conflicting missions per conflict section.</summary>
        public const int MaxConflictingMissions = 25;

        /// <summary>Overlapping files per conflicting mission.</summary>
        public const int MaxOverlappingFiles = 50;

        /// <summary>Target commits since start per conflict section.</summary>
        public const int MaxTargetCommits = 50;

        /// <summary>Files per scope list (scoped, out of scope, boundary findings).</summary>
        public const int MaxScopeFiles = 200;

        /// <summary>Effective configuration values per record.</summary>
        public const int MaxConfigValues = 64;

        /// <summary>Prior attempts per record.</summary>
        public const int MaxAttempts = 20;

        /// <summary>Evidence objects listed in the evidence index.</summary>
        public const int MaxEvidenceItems = 64;

        /// <summary>Stage check results per evidence index.</summary>
        public const int MaxCheckResults = 10;

        /// <summary>Papercuts per evidence index.</summary>
        public const int MaxPapercuts = 10;

        /// <summary>Top paths in a diff stat.</summary>
        public const int MaxDiffTopPaths = 50;

        /// <summary>Relevant events per evidence index.</summary>
        public const int MaxRelevantEvents = 50;

        /// <summary>Why findings per analysis.</summary>
        public const int MaxFindings = 10;

        /// <summary>Evidence references per finding.</summary>
        public const int MaxEvidenceRefs = 10;

        /// <summary>Configuration recommendations per analysis.</summary>
        public const int MaxRecommendations = 10;

        /// <summary>Armada improvement hypotheses per analysis.</summary>
        public const int MaxImprovements = 10;

        /// <summary>Source pointers per improvement.</summary>
        public const int MaxSourcePointers = 10;

        /// <summary>Recovery paths per analysis.</summary>
        public const int MaxRecoveryPaths = 10;

        /// <summary>Steps per recovery path.</summary>
        public const int MaxRecoverySteps = 10;

        /// <summary>Preconditions per recovery path.</summary>
        public const int MaxPreconditions = 10;

        /// <summary>Arguments per machine-readable action.</summary>
        public const int MaxActionArguments = 32;

        /// <summary>Triage votes per verdict.</summary>
        public const int MaxTriageVotes = 20;

        /// <summary>Triage candidates reported on a tie (one per BlackBoxTriageEnum value).</summary>
        public const int MaxTriageCandidates = 6;

        /// <summary>Similar entries per related section.</summary>
        public const int MaxSimilarEntries = 10;

        /// <summary>Downstream mission ids per cascade.</summary>
        public const int MaxDownstreamMissions = 100;

        /// <summary>Status counts per voyage roll-up (one per mission status).</summary>
        public const int MaxVoyageStatusCounts = 16;

        /// <summary>Failed mission ids and entry ids per voyage roll-up.</summary>
        public const int MaxVoyageFailedMissions = 100;

        /// <summary>Redaction rules reported per record.</summary>
        public const int MaxRedactionRules = 32;

        /// <summary>Truncated fields reported per record.</summary>
        public const int MaxTruncatedFields = 64;

        /// <summary>Capture notes per record (timeouts, lower fidelity, missing facts).</summary>
        public const int MaxNotes = 20;

        /// <summary>Entry ids per bulk delete request.</summary>
        public const int MaxDeleteIds = CollectionLimits.MaxRequestItems;

        /// <summary>Entries per maintainer bundle.</summary>
        public const int MaxBundleEntries = 50;

        /// <summary>Armada versions listed per maintainer bundle group.</summary>
        public const int MaxBundleVersions = 10;

        /// <summary>Most UTF-8 bytes of a Markdown maintainer bundle before later groups are left out.</summary>
        public const int MaxBundleBytes = 1048576;

        /// <summary>Entries of one fingerprint counted by a fingerprint trend.</summary>
        public const int MaxTrendEntries = 1000;

        /// <summary>Armada versions listed by a fingerprint trend.</summary>
        public const int MaxTrendVersions = 100;

        /// <summary>Sample entry ids reported by purge and garbage collection.</summary>
        public const int MaxSampleIds = 100;

        /// <summary>Error messages reported by a garbage collection run or job.</summary>
        public const int MaxErrors = 20;

        /// <summary>Objects per entry.</summary>
        public const int MaxObjectsPerEntry = 128;

        /// <summary>Exception message characters.</summary>
        public const int MaxExceptionMessageChars = 4000;

        /// <summary>Command output tail characters.</summary>
        public const int MaxCommandOutputChars = 8000;

        /// <summary>Git stderr tail characters.</summary>
        public const int MaxGitStderrChars = 4000;

        /// <summary>Raw failure reason characters.</summary>
        public const int MaxRawReasonChars = 8000;

        /// <summary>Reason excerpt characters stored on the index row (searchable).</summary>
        public const int MaxEntryReasonExcerptChars = 1000;

        /// <summary>Reason excerpt characters on an attempt or summary.</summary>
        public const int MaxReasonExcerptChars = 500;

        /// <summary>Analysis summary characters.</summary>
        public const int MaxSummaryChars = 2000;

        /// <summary>Note body characters.</summary>
        public const int MaxNoteChars = 8000;

        /// <summary>Resolution note characters.</summary>
        public const int MaxResolutionNoteChars = 2000;

        /// <summary>Resolution link characters.</summary>
        public const int MaxResolutionLinkChars = 1000;

        /// <summary>Title characters (mission, voyage, recovery path).</summary>
        public const int MaxTitleChars = 500;

        /// <summary>Path characters.</summary>
        public const int MaxPathChars = 1024;

        /// <summary>Other short text characters (statements, commands, values).</summary>
        public const int MaxShortTextChars = 2000;

        /// <summary>Evidence excerpt characters.</summary>
        public const int MaxEvidenceExcerptChars = 500;

        /// <summary>Search query characters.</summary>
        public const int MaxSearchQueryChars = 200;

        /// <summary>Maximum record.json size in bytes (uncompressed): 256 KB.</summary>
        public const int MaxRecordBytes = 262144;

        /// <summary>Target brief size in bytes: 12 KB.</summary>
        public const int BriefTargetBytes = 12288;

        /// <summary>Hard cap on a brief in bytes: 40 KB.</summary>
        public const int BriefMaxBytes = 40960;

        /// <summary>Largest object chunk an MCP read returns: 64 KB.</summary>
        public const int MaxMcpChunkBytes = 65536;

        /// <summary>Largest object chunk a REST read returns: 1 MB.</summary>
        public const int MaxRestChunkBytes = 1048576;

        /// <summary>Record schema version written by this build.</summary>
        public const int SchemaVersion = 1;

        /// <summary>Default attempts before enrichment stops retrying an entry.</summary>
        public const int DefaultMaxEnrichmentAttempts = 5;

        /// <summary>Proposed Armada changes per resilience section.</summary>
        public const int MaxProposedChanges = 5;

        /// <summary>Code pointers per resilience section.</summary>
        public const int MaxCodePointers = 12;

        /// <summary>Automatic recoveries Armada already attempted, listed per resilience section.</summary>
        public const int MaxAttemptedRecoveries = 10;

        /// <summary>Contributing rule ids per resilience section.</summary>
        public const int MaxResilienceRules = 10;

        /// <summary>Steps in a record's handoff and landing trail.</summary>
        public const int MaxHandoffSteps = 100;

        /// <summary>Automatic recovery kinds in a record's handoff and landing trail (one per BlackBoxAutoRecoveryKindEnum value).</summary>
        public const int MaxAutoRecoveries = 16;

        /// <summary>Dock and lease transitions in a record's handoff and landing trail.</summary>
        public const int MaxDockTransitions = 20;

        /// <summary>Armada versions a fingerprint was seen in, kept on the Related section.</summary>
        public const int MaxRelatedVersions = 20;

        /// <summary>Resilience gap groups per page (each reads its newest entry's record).</summary>
        public const int MaxResilienceGapPageSize = 50;

        /// <summary>Weight of the Armada prior vote (BBX-PRIOR-ARMADA) in handoff and landing scope.</summary>
        public const int ArmadaPriorWeight = 2;

        /// <summary>
        /// Cut a string to at most <paramref name="max"/> characters, keeping the head. Null stays null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="max">Maximum characters (at least 0).</param>
        /// <returns>The value, cut when longer.</returns>
        public static string? Clip(string? value, int max)
        {
            if (value == null) return null;
            if (max < 0) max = 0;
            return value.Length <= max ? value : value.Substring(0, max);
        }

        /// <summary>
        /// Cut a string to at most <paramref name="max"/> characters, keeping the tail. Null stays null.
        /// </summary>
        /// <param name="value">Value.</param>
        /// <param name="max">Maximum characters (at least 0).</param>
        /// <returns>The value, cut when longer.</returns>
        public static string? Tail(string? value, int max)
        {
            if (value == null) return null;
            if (max < 0) max = 0;
            return value.Length <= max ? value : value.Substring(value.Length - max);
        }
    }
}
