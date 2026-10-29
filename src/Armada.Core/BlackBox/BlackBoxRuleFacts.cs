namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Facts derived from a black box record that rule predicates and placeholders share: failure kind and step, the
    /// reason text, dirty and conflicted counts, the out-of-scope list (from the Scope section or parsed from the reason),
    /// dependency language and configuration values. Pure, null-safe and thread-safe.
    /// </summary>
    public static class BlackBoxRuleFacts
    {
        #region Private-Members

        private static readonly Regex _ScopeList = new Regex(@"outside its scoped file list:(?<list>.*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly Regex _Dependency = new Regex(@"depends on (?:mission\s+)?(?<dep>msn_[A-Za-z0-9_]+|#?\d+|mission\s+\d+)[^.;\n]{0,160}?(?:not (?:yet )?(?:landed|merged|complete|finished)|has not landed|hasn't landed|is still running|running (?:concurrently|in parallel)|in parallel|concurrently)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex _Concurrent = new Regex(@"(?:missions?\s+[\d\s,\-and]+\s+(?:are|were)\s+(?:still\s+)?running\s+(?:concurrently|in parallel))|(?:dispatched\s+(?:concurrently|in parallel))|(?:(?:prerequisite|depends on|builds on|requires)[^.;\n]{0,200}?(?:(?:still\s+)?running (?:concurrently|in parallel)|not (?:yet )?(?:landed|merged|on the target)))", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex _MissingDirectory = new Regex(@"(?:start process .{0,200}working directory|working directory .{0,200}(?:does not exist|not found))|(?:No such file or directory)|(?:cannot find the (?:path|directory))|(?:DirectoryNotFoundException)", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline);
        private static readonly string[] _GeneratedFileNames = new string[] { "CHANGELOG.md", "package-lock.json", "yarn.lock", "pnpm-lock.yaml", "packages.lock.json" };

        #endregion

        #region Public-Methods

        /// <summary>The failure kind (classification, else null).</summary>
        /// <param name="r">Record.</param>
        /// <returns>The kind or null.</returns>
        public static MissionFailureKindEnum? Kind(BlackBoxRecord r)
        {
            return r?.Classification?.Kind;
        }

        /// <summary>The failure step (site, else Unknown).</summary>
        /// <param name="r">Record.</param>
        /// <returns>The step.</returns>
        public static BlackBoxStepEnum Step(BlackBoxRecord r)
        {
            return r?.Site?.Step ?? BlackBoxStepEnum.Unknown;
        }

        /// <summary>The failure phase (site, else derived from the step).</summary>
        /// <param name="r">Record.</param>
        /// <returns>The phase.</returns>
        public static BlackBoxPhaseEnum Phase(BlackBoxRecord r)
        {
            if (r?.Site == null) return BlackBoxPhaseEnum.Unknown;
            return r.Site.Phase != BlackBoxPhaseEnum.Unknown ? r.Site.Phase : BlackBoxTaxonomy.PhaseOf(r.Site.Step);
        }

        /// <summary>The runtime failure kind, if recorded.</summary>
        /// <param name="r">Record.</param>
        /// <returns>The runtime failure kind or null.</returns>
        public static RuntimeFailureKindEnum? RuntimeKind(BlackBoxRecord r)
        {
            return r?.Mechanism?.RuntimeFailureKind;
        }

        /// <summary>Every failure text the record holds (reason, exception, git stderr, command output), joined.</summary>
        /// <param name="r">Record.</param>
        /// <returns>The text (empty when none).</returns>
        public static string AllText(BlackBoxRecord r)
        {
            if (r == null) return String.Empty;
            List<string?> parts = new List<string?>
            {
                r.Classification?.RawReason,
                r.Mechanism?.ExceptionMessage,
                r.Mechanism?.GitStderrTail,
                r.Mechanism?.CommandOutputTail
            };
            if (r.Evidence != null) parts.AddRange(r.Evidence.Objects.Select(o => o.Excerpt));
            return String.Join("\n", parts.Where(p => !String.IsNullOrWhiteSpace(p)));
        }

        /// <summary>Number of dirty paths in the checkout (0 when none recorded).</summary>
        /// <param name="r">Record.</param>
        /// <returns>The count.</returns>
        public static int DirtyCount(BlackBoxRecord r)
        {
            BlackBoxWorkspace? w = r?.Workspace;
            if (w == null) return 0;
            return Math.Max(w.DirtyPathTotal, w.DirtyPaths.Count);
        }

        /// <summary>Number of conflicted files (0 when none recorded).</summary>
        /// <param name="r">Record.</param>
        /// <returns>The count.</returns>
        public static int ConflictCount(BlackBoxRecord r)
        {
            BlackBoxConflict? c = r?.Conflict;
            if (c == null) return 0;
            return Math.Max(c.ConflictedFileTotal, c.ConflictedFiles.Count);
        }

        /// <summary>The first conflicting mission that landed through Armada with overlapping files, or null.</summary>
        /// <param name="r">Record.</param>
        /// <returns>The conflicting mission or null.</returns>
        public static BlackBoxConflictingMission? LandedConflict(BlackBoxRecord r)
        {
            BlackBoxConflict? c = r?.Conflict;
            if (c == null) return null;
            return c.ConflictingMissions.FirstOrDefault(m => !m.IsOutsideArmada && !String.IsNullOrEmpty(m.MissionId) && (m.OverlappingFiles.Count > 0 || m.OverlappingFileTotal > 0));
        }

        /// <summary>Whether every conflicted file is a generated or append-only file (CHANGELOG, i18n catalogs, API
        /// surface snapshots, Postman collections, lock files, generated JSON Schemas).</summary>
        /// <param name="r">Record.</param>
        /// <returns>True when there are conflicted files and all are generated.</returns>
        public static bool OnlyGeneratedConflicts(BlackBoxRecord r)
        {
            BlackBoxConflict? c = r?.Conflict;
            if (c == null || c.ConflictedFiles.Count == 0 || c.ConflictedFilesTruncated) return false;
            return c.ConflictedFiles.All(IsGenerated);
        }

        /// <summary>Whether a path is a generated or append-only file.</summary>
        /// <param name="path">Repository-relative path.</param>
        /// <returns>True when generated.</returns>
        public static bool IsGenerated(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return false;
            string p = "/" + path.Trim().Replace('\\', '/').TrimStart('/');
            string name = p.Substring(p.LastIndexOf('/') + 1);
            if (_GeneratedFileNames.Any(g => String.Equals(g, name, StringComparison.OrdinalIgnoreCase))) return true;
            if (p.Contains("/i18n/", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return true;
            if (name.StartsWith("api-surface", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return true;
            return name.EndsWith(".postman_collection.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The out-of-scope entries: the Scope section's list when present, otherwise parsed from the reason
        /// ("... outside its scoped file list: a, b"). Blank entries are kept as empty strings.</summary>
        /// <param name="r">Record.</param>
        /// <returns>The entries (empty when unknown).</returns>
        public static List<string> OutOfScopeEntries(BlackBoxRecord r)
        {
            if (r?.Scope != null && r.Scope.OutOfScopeFiles.Count > 0) return r.Scope.OutOfScopeFiles.Select(f => (f ?? String.Empty).Trim()).ToList();
            string? reason = r?.Classification?.RawReason;
            if (String.IsNullOrEmpty(reason)) return new List<string>();
            Match m = _ScopeList.Match(reason!);
            if (!m.Success) return new List<string>();
            string list = m.Groups["list"].Value.Trim().TrimEnd('.');
            if (list.Length == 0) return new List<string> { String.Empty };
            return list.Split(',').Select(s => s.Trim()).Take(BlackBoxLimits.MaxScopeFiles).ToList();
        }

        /// <summary>Whether a file is in the mission's scoped file list (path separators and a leading ./ ignored).</summary>
        /// <param name="r">Record.</param>
        /// <param name="path">Path.</param>
        /// <returns>True when scoped.</returns>
        public static bool IsScoped(BlackBoxRecord r, string path)
        {
            if (r?.Scope == null || String.IsNullOrWhiteSpace(path)) return false;
            string normalized = NormalizePath(path);
            return r.Scope.ScopedFiles.Any(s => String.Equals(NormalizePath(s), normalized, StringComparison.Ordinal));
        }

        /// <summary>Whether the scope violation is false: every out-of-scope entry is blank or a scoped file.</summary>
        /// <param name="r">Record.</param>
        /// <returns>True when the violation is false.</returns>
        public static bool FalseScopeViolation(BlackBoxRecord r)
        {
            List<string> entries = OutOfScopeEntries(r);
            if (entries.Count == 0) return false;
            return entries.All(e => e.Length == 0 || IsScoped(r, e));
        }

        /// <summary>Whether the record says the captain was blocked by unlanded prerequisites dispatched in parallel.</summary>
        /// <param name="r">Record.</param>
        /// <returns>True when the dependency language is present.</returns>
        public static bool ParallelDependency(BlackBoxRecord r)
        {
            string text = AllText(r);
            if (text.Length == 0) return false;
            return _Dependency.IsMatch(text) || _Concurrent.IsMatch(text);
        }

        /// <summary>The prerequisite named in the dependency language ("mission 1", "msn_x"), or null.</summary>
        /// <param name="r">Record.</param>
        /// <returns>The prerequisite or null.</returns>
        public static string? Prerequisite(BlackBoxRecord r)
        {
            Match m = _Dependency.Match(AllText(r));
            if (!m.Success) return null;
            string dep = m.Groups["dep"].Value.Trim();
            return dep.StartsWith("msn_", StringComparison.Ordinal) || dep.StartsWith("mission", StringComparison.OrdinalIgnoreCase) ? dep : "mission " + dep.TrimStart('#');
        }

        /// <summary>Whether the failure text says a process could not start because its directory is gone.</summary>
        /// <param name="r">Record.</param>
        /// <returns>True when a missing working directory is reported.</returns>
        public static bool MissingWorkingDirectory(BlackBoxRecord r)
        {
            string text = AllText(r);
            return text.Length > 0 && _MissingDirectory.IsMatch(text);
        }

        /// <summary>A configuration value by setting name (case-insensitive), or null.</summary>
        /// <param name="r">Record.</param>
        /// <param name="setting">Setting name.</param>
        /// <returns>The value entry or null.</returns>
        public static BlackBoxConfigValue? Config(BlackBoxRecord r, string setting)
        {
            if (r?.Configuration == null || String.IsNullOrEmpty(setting)) return null;
            return r.Configuration.Values.FirstOrDefault(v => String.Equals(v.Setting, setting, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Whether the Harbor link was recorded as disconnected.</summary>
        /// <param name="r">Record.</param>
        /// <returns>True when disconnected.</returns>
        public static bool HarborDisconnected(BlackBoxRecord r)
        {
            string? state = r?.Workspace?.HarborLinkState;
            return !String.IsNullOrEmpty(state) && (state!.Equals("Disconnected", StringComparison.OrdinalIgnoreCase) || state.Equals("Offline", StringComparison.OrdinalIgnoreCase) || state.Equals("Lost", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Whether the record is about a mission (has a mission id).</summary>
        /// <param name="r">Record.</param>
        /// <returns>True for a mission record.</returns>
        public static bool IsMission(BlackBoxRecord r)
        {
            return r?.Subject != null && r.Subject.SubjectType != BlackBoxSubjectTypeEnum.Voyage && !String.IsNullOrEmpty(r.Subject.MissionId);
        }

        /// <summary>Whether the record has nothing a rule can match on (no site step, kind, reason or mechanism).</summary>
        /// <param name="r">Record.</param>
        /// <returns>True when empty.</returns>
        public static bool IsEmpty(BlackBoxRecord r)
        {
            if (r == null) return true;
            return Step(r) == BlackBoxStepEnum.Unknown && Kind(r) == null && AllText(r).Length == 0 && RuntimeKind(r) == null;
        }

        #endregion

        #region Private-Methods

        private static string NormalizePath(string path)
        {
            string p = (path ?? String.Empty).Trim().Replace('\\', '/');
            while (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
            return p.TrimStart('/');
        }

        #endregion
    }
}
