namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;

    /// <summary>
    /// Lints a mission brief before dispatch. Pure and deterministic. Findings are warnings and never block a dispatch:
    /// BRIEF-DEP-001 dependency language ("after mission 2 lands", "depends on", "mission N") without DependsOnMissionId,
    /// BRIEF-NOOP-001 a verification-only brief (likely to finish with no changes and fail as NoOp), BRIEF-PATH-001 a path
    /// whose top-level directory the vessel does not have, BRIEF-PATH-002 an absolute or parent-relative path.
    /// </summary>
    public static class MissionBriefLinter
    {
        #region Public-Members

        /// <summary>Dependency language without DependsOnMissionId.</summary>
        public const string DependencyRule = "BRIEF-DEP-001";

        /// <summary>Verification-only brief (NoOp risk).</summary>
        public const string NoOpRule = "BRIEF-NOOP-001";

        /// <summary>Path outside the vessel.</summary>
        public const string OutsidePathRule = "BRIEF-PATH-001";

        /// <summary>Absolute or parent-relative path.</summary>
        public const string HostPathRule = "BRIEF-PATH-002";

        #endregion

        #region Private-Members

        private static readonly RegexOptions _Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

        private static readonly Regex[] _DependencyPatterns = new Regex[]
        {
            new Regex(@"\b(?:after|once|when)\s+(?:mission\s+)?[#\w./-]{1,80}(?:\s+[#\w./-]{1,40}){0,6}\s+(?:lands|has\s+landed|is\s+landed|merges|is\s+merged|has\s+merged|completes|is\s+complete|is\s+done)\b", _Options),
            new Regex(@"\bdepends\s+on\b|\bdependent\s+on\b|\bbuilds\s+on\s+(?:mission|the\s+work\s+of)\b", _Options),
            new Regex(@"\bmission\s+#?\d{1,3}\b", _Options),
            new Regex(@"\bprerequisite\s+mission\b|\bblocked\s+(?:on|by)\s+mission\b|\bwait\s+for\s+mission\b", _Options),
            new Regex(@"\bmsn_[A-Za-z0-9_]{6,}\b", _Options)
        };

        private static readonly Regex _VerificationOnly = new Regex(@"\b(?:verify|verification|confirm|check\s+that|check\s+whether|ensure\s+that|validate|make\s+sure|run\s+(?:the\s+)?(?:tests?|suites?|build)|smoke[- ]test|audit|investigate|review|report\s+(?:on|back|whether))\b", _Options);

        private static readonly Regex _ChangeVerb = new Regex(@"\b(?:add|adds|adding|implement|implements|fix|fixes|change|changes|update|updates|create|creates|write|writes|remove|removes|refactor|rename|modify|edit|replace|delete|move|introduce|wire|extend|port|migrate|bump|upgrade|regenerate|convert)\b", _Options);

        private static readonly Regex _NoChangePhrase = new Regex(@"\b(?:do\s+not\s+(?:change|modify|edit|commit)|don't\s+(?:change|modify|edit|commit)|no\s+code\s+changes|without\s+changing|read[- ]only)\b", _Options);

        private static readonly Regex _HostPath = new Regex(@"(?<![\w.:/-])(?:(?:/(?:Users|home|tmp|var|opt|etc|private|mnt|srv|root|Volumes)/[^\s`'""<>)]+)|(?:[A-Za-z]:\\[^\s`'""<>)]+)|(?:~/[^\s`'""<>)]+)|(?:\.\./[^\s`'""<>)]+))", RegexOptions.CultureInvariant | RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Lint a brief.
        /// </summary>
        /// <param name="title">Mission title, or null.</param>
        /// <param name="description">Mission brief, or null.</param>
        /// <param name="dependsOnMissionId">The dependency the caller set, or null.</param>
        /// <param name="tracked">The vessel's tracked files, or null to skip the path check.</param>
        /// <param name="mode">Mission mode, or null (Audit and Research briefs are expected to change nothing).</param>
        /// <returns>The result (at most 50 findings).</returns>
        public static BriefLintResult Lint(string? title, string? description, string? dependsOnMissionId, VesselTrackedFiles? tracked, MissionModeEnum? mode = null)
        {
            string text = (title ?? String.Empty) + "\n" + (description ?? String.Empty);
            List<BriefLintFinding> findings = new List<BriefLintFinding>();

            if (String.IsNullOrWhiteSpace(dependsOnMissionId))
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Regex pattern in _DependencyPatterns)
                {
                    foreach (Match m in pattern.Matches(text))
                    {
                        if (!seen.Add(m.Value.Trim())) continue;
                        findings.Add(new BriefLintFinding
                        {
                            RuleId = DependencyRule,
                            Severity = BriefLintSeverityEnum.Warning,
                            Message = "The brief describes a dependency (\"" + Clip(m.Value.Trim(), 80) + "\") but the mission has no DependsOnMissionId, so it can start before that work lands.",
                            Excerpt = Excerpt(text, m.Index, m.Length),
                            Suggestion = "Set dependsOnMissionId to the prerequisite mission (dispatch it in the same voyage and chain it), or remove the dependency from the brief."
                        });
                        if (findings.Count >= 5) break;
                    }
                    if (findings.Count >= 5) break;
                }
            }

            bool expectsNoChanges = mode == MissionModeEnum.Audit || mode == MissionModeEnum.Research;
            if (!expectsNoChanges && !String.IsNullOrWhiteSpace(description))
            {
                Match verify = _VerificationOnly.Match(text);
                Match noChange = _NoChangePhrase.Match(text);
                bool changes = _ChangeVerb.IsMatch(text);
                if (noChange.Success || (verify.Success && !changes))
                {
                    Match trigger = noChange.Success ? noChange : verify;
                    findings.Add(new BriefLintFinding
                    {
                        RuleId = NoOpRule,
                        Severity = BriefLintSeverityEnum.Warning,
                        Message = noChange.Success
                            ? "The brief asks for no changes, so the mission is likely to finish without a diff and fail as NoOp."
                            : "The brief only asks to verify or check, with nothing to change, so the mission is likely to finish without a diff and fail as NoOp.",
                        Excerpt = Excerpt(text, trigger.Index, trigger.Length),
                        Suggestion = "Dispatch it with mode Audit or Research (an empty diff is then a success), or say what to change."
                    });
                }
            }

            HashSet<string> reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in _HostPath.Matches(text))
            {
                if (findings.Count >= BlackBoxInsightLimits.MaxLintFindings) break;
                string value = m.Value.TrimEnd('.', ',', ';', ':');
                if (!reported.Add(value)) continue;
                findings.Add(new BriefLintFinding
                {
                    RuleId = HostPathRule,
                    Severity = BriefLintSeverityEnum.Warning,
                    Message = "The brief names a host or parent-relative path (" + Clip(value, 120) + "); captains work in their own checkout and cannot rely on it.",
                    Excerpt = Excerpt(text, m.Index, m.Length),
                    Path = Clip(value, BlackBoxLimits.MaxPathChars),
                    Suggestion = "Use a path relative to the repository root."
                });
            }

            if (tracked != null)
            {
                foreach (string raw in GitAnchorInputs.ExtractPaths(text, BlackBoxInsightLimits.MaxBriefPaths))
                {
                    if (findings.Count >= BlackBoxInsightLimits.MaxLintFindings) break;
                    string path = MissionFileSet.Normalize(raw);
                    if (path.IndexOf('/') < 0 || reported.Contains(raw) || reported.Contains(path)) continue;
                    if (LooksLikeUrlOrPackage(text, raw)) continue;
                    if (tracked.IsFile(path) || tracked.IsDirectory(path) || tracked.RootExists(path)) continue;
                    reported.Add(path);
                    int at = text.IndexOf(raw, StringComparison.Ordinal);
                    findings.Add(new BriefLintFinding
                    {
                        RuleId = OutsidePathRule,
                        Severity = BriefLintSeverityEnum.Warning,
                        Message = "The brief names " + Clip(path, 120) + ", but the vessel has no " + FirstSegment(path) + " at its root; the path is outside the vessel (wrong vessel or a typo).",
                        Excerpt = at >= 0 ? Excerpt(text, at, raw.Length) : null,
                        Path = Clip(path, BlackBoxLimits.MaxPathChars),
                        Suggestion = "Check the path against the repository, or dispatch to the vessel that has it."
                    });
                }
            }

            BriefLintResult result = new BriefLintResult();
            result.FindingCount = findings.Count;
            result.Findings = findings.Take(BlackBoxInsightLimits.MaxLintFindings).ToList();
            result.Clean = findings.Count == 0;
            result.PathsChecked = tracked != null;
            return result;
        }

        #endregion

        #region Private-Methods

        private static bool LooksLikeUrlOrPackage(string text, string raw)
        {
            int at = text.IndexOf(raw, StringComparison.Ordinal);
            if (at < 0) return false;
            int start = Math.Max(0, at - 12);
            string before = text.Substring(start, at - start);
            return before.Contains("://", StringComparison.Ordinal) || before.EndsWith("@", StringComparison.Ordinal)
                || raw.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || raw.Contains(".com/", StringComparison.OrdinalIgnoreCase)
                || raw.Contains(".org/", StringComparison.OrdinalIgnoreCase) || raw.Contains(".io/", StringComparison.OrdinalIgnoreCase);
        }

        private static string FirstSegment(string path)
        {
            int index = path.IndexOf('/');
            return (index < 0 ? path : path.Substring(0, index)) + "/";
        }

        private static string Excerpt(string text, int index, int length)
        {
            int start = Math.Max(0, index - 40);
            int end = Math.Min(text.Length, index + length + 40);
            string excerpt = text.Substring(start, end - start).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return Clip(excerpt, BlackBoxInsightLimits.MaxLintExcerptChars);
        }

        private static string Clip(string value, int max)
        {
            return BlackBoxLimits.Clip(value, max) ?? String.Empty;
        }

        #endregion
    }
}
