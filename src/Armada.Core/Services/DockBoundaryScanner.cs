namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;

    /// <summary>
    /// Scans a mission's diff and changed-path set before landing for boundary violations: secrets a
    /// captain introduced, changes to protected paths, and private-identifier leaks into public repos.
    /// Findings never carry the secret bytes -- only the rule id, the path, and (for secrets) the matched
    /// line number. Pure and side-effect free so it can be unit tested against diff fixtures.
    /// </summary>
    public static class DockBoundaryScanner
    {
        #region Private-Members

        // Built-in secret patterns. Kept intentionally high-signal to avoid noisy false positives.
        private static readonly (string RuleId, Regex Pattern)[] _SecretPatterns = new (string, Regex)[]
        {
            ("aws-access-key-id", new Regex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled)),
            ("github-token", new Regex(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b", RegexOptions.Compiled)),
            ("slack-token", new Regex(@"\bxox[baprs]-[A-Za-z0-9-]{10,}\b", RegexOptions.Compiled)),
            ("google-api-key", new Regex(@"\bAIza[0-9A-Za-z_\-]{35}\b", RegexOptions.Compiled)),
            ("private-key-block", new Regex(@"-----BEGIN (?:RSA |EC |OPENSSH |DSA |PGP )?PRIVATE KEY-----", RegexOptions.Compiled)),
            ("generic-secret-assignment", new Regex(
                @"(?i)\b(?:api[_-]?key|secret|password|passwd|token|access[_-]?key)\b\s*[:=]\s*[""']?[A-Za-z0-9/\+_\-]{16,}[""']?",
                RegexOptions.Compiled)),
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Scan a diff + changed paths against the supplied policy and return structured findings.
        /// An empty list means the dock is clean under this policy.
        /// </summary>
        /// <param name="diffText">Unified git diff text for the mission (null/empty = no changes). Paths it names are
        /// added to <paramref name="changedPaths"/> for the protected-path check.</param>
        /// <param name="changedPaths">Additional changed file paths (forward-slash relative), for example from git --name-status.</param>
        /// <param name="policy">Per-vessel policy. Null disables all checks.</param>
        /// <returns>Structured findings; never contains secret bytes.</returns>
        public static List<BoundaryFinding> Scan(string? diffText, IEnumerable<string>? changedPaths, DockBoundaryPolicy? policy)
        {
            List<BoundaryFinding> findings = new List<BoundaryFinding>();
            if (policy == null) return findings;

            List<UnifiedDiffFile> diffFiles = UnifiedDiffParser.Parse(diffText);

            // Protected paths -- evaluate every path the change touches: the supplied set plus every path
            // the diff itself names (both sides of a rename, deleted paths, mode-only and binary changes).
            if (policy.ProtectedPathGlobs != null && policy.ProtectedPathGlobs.Count > 0)
            {
                List<string> paths = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                if (changedPaths != null)
                {
                    foreach (string rawPath in changedPaths)
                    {
                        string path = NormalizePath(rawPath);
                        if (!String.IsNullOrEmpty(path) && seen.Add(path)) paths.Add(path);
                    }
                }
                foreach (UnifiedDiffFile file in diffFiles)
                {
                    foreach (string rawPath in file.Paths)
                    {
                        string path = NormalizePath(rawPath);
                        if (!String.IsNullOrEmpty(path) && seen.Add(path)) paths.Add(path);
                    }
                }

                foreach (string path in paths)
                {
                    foreach (string glob in policy.ProtectedPathGlobs)
                    {
                        if (GlobMatches(glob, path))
                        {
                            findings.Add(new BoundaryFinding("protected-path", glob, path, null));
                            break;
                        }
                    }
                }
            }

            // Secrets + private identifiers -- scan only ADDED lines, taken from inside hunks by their @@ ranges.
            bool scanSecrets = policy.SecretScanEnabled;
            bool scanPrivate = policy.PrivateIdentifiers != null && policy.PrivateIdentifiers.Count > 0;
            if (scanSecrets || scanPrivate)
            {
                foreach (UnifiedDiffFile file in diffFiles)
                {
                    string currentFile = NormalizePath(file.DisplayPath);
                    foreach (UnifiedDiffLine addedLine in file.AddedLines)
                    {
                        string added = addedLine.Content;

                        if (scanSecrets)
                        {
                            foreach ((string ruleId, Regex pattern) in _SecretPatterns)
                            {
                                if (pattern.IsMatch(added))
                                {
                                    findings.Add(new BoundaryFinding("secret", ruleId, currentFile, addedLine.NewLineNumber));
                                    break; // one secret finding per line is enough; do not echo the value
                                }
                            }
                        }

                        if (scanPrivate)
                        {
                            foreach (string identifier in policy.PrivateIdentifiers!)
                            {
                                if (String.IsNullOrWhiteSpace(identifier)) continue;
                                if (added.IndexOf(identifier.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    findings.Add(new BoundaryFinding("private-identifier", identifier.Trim(), currentFile, addedLine.NewLineNumber));
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            return findings;
        }

        /// <summary>
        /// Render findings into a single redaction-safe summary suitable for a mission FailureReason.
        /// </summary>
        /// <param name="findings">The findings to summarize.</param>
        /// <returns>A human-readable, secret-free summary.</returns>
        public static string Summarize(IReadOnlyList<BoundaryFinding> findings)
        {
            if (findings == null || findings.Count == 0) return "dock_boundary_violation: no details";
            List<string> parts = new List<string>();
            foreach (BoundaryFinding f in findings)
            {
                string loc = f.Line.HasValue ? (f.Path + ":" + f.Line.Value) : f.Path;
                parts.Add(f.Kind + " [" + f.RuleId + "] at " + (String.IsNullOrEmpty(loc) ? "(unknown)" : loc));
            }
            return "dock_boundary_violation: " + String.Join("; ", parts);
        }

        #endregion

        #region Private-Methods

        private static string NormalizePath(string? path)
        {
            if (String.IsNullOrWhiteSpace(path)) return String.Empty;
            return path.Replace('\\', '/').TrimStart('/').Trim();
        }

        // Minimal glob: supports * (any run within a segment) and ** (across segments), anchored to the full path.
        private static bool GlobMatches(string glob, string path)
        {
            if (String.IsNullOrWhiteSpace(glob)) return false;
            string normalizedGlob = glob.Replace('\\', '/').TrimStart('/').Trim();
            string regex = "^" + Regex.Escape(normalizedGlob)
                .Replace(@"\*\*/", "(?:.*/)?")
                .Replace(@"\*\*", ".*")
                .Replace(@"\*", "[^/]*")
                .Replace(@"\?", "[^/]") + "$";
            return Regex.IsMatch(path, regex, RegexOptions.IgnoreCase);
        }

        #endregion
    }
}
