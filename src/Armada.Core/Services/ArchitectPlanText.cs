namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Text rules for Architect plan output: normalizing slice descriptions, extracting dependency references and owned
    /// paths, and recognizing placeholder slices and agent telemetry lines. Pure functions with no state.
    /// </summary>
    internal static class ArchitectPlanText
    {
        private static readonly Regex _TelemetryNumberRegex = new Regex(@"^[\d,]+$", RegexOptions.Compiled);

        /// <summary>Trim a slice description and drop agent telemetry lines at its start and end.</summary>
        internal static string NormalizeArchitectDescription(string? description)
        {
            if (String.IsNullOrWhiteSpace(description)) return "";

            List<string> descriptionLines = description
                .Split('\n')
                .Select(l => l.Trim('\r'))
                .ToList();

            while (descriptionLines.Count > 0)
            {
                string firstLine = descriptionLines[0].Trim();
                if (IsAgentTelemetryLine(firstLine))
                {
                    descriptionLines.RemoveAt(0);
                    continue;
                }

                break;
            }

            while (descriptionLines.Count > 0)
            {
                string lastLine = descriptionLines[descriptionLines.Count - 1].Trim();
                if (IsAgentTelemetryLine(lastLine))
                {
                    descriptionLines.RemoveAt(descriptionLines.Count - 1);
                    continue;
                }

                break;
            }

            return String.Join("\n", descriptionLines).Trim();
        }

        /// <summary>Split a leading dependency line off a slice description.</summary>
        internal static ArchitectDependencyExtractionResult ExtractArchitectDependencyReference(string description)
        {
            if (String.IsNullOrWhiteSpace(description))
            {
                return new ArchitectDependencyExtractionResult();
            }

            List<string> keptLines = new List<string>();
            string? dependencyReference = null;

            foreach (string rawLine in description.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = rawLine.Trim();
                if (TryExtractArchitectDependency(trimmed, out string? dependencyCandidate, out string? remainingDescription))
                {
                    if (String.IsNullOrWhiteSpace(dependencyReference))
                    {
                        dependencyReference = NormalizeArchitectDependencyReference(dependencyCandidate ?? String.Empty);
                    }

                    if (!String.IsNullOrWhiteSpace(remainingDescription))
                    {
                        keptLines.Add(remainingDescription);
                    }

                    continue;
                }

                keptLines.Add(rawLine.TrimEnd('\r'));
            }

            return new ArchitectDependencyExtractionResult
            {
                Description = String.Join("\n", keptLines).Trim(),
                DependsOnReference = String.IsNullOrWhiteSpace(dependencyReference) ? null : dependencyReference
            };
        }

        // Legacy markers: an "Owns: a, b" line names the files the slice may change; it is removed from the description.
        /// <summary>Remove the owned-paths line from a slice description and return the paths it lists.</summary>
        internal static string ExtractOwnedPaths(string description, out List<string> ownedPaths)
        {
            ownedPaths = new List<string>();
            if (String.IsNullOrWhiteSpace(description)) return description;
            List<string> kept = new List<string>();
            foreach (string rawLine in description.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = rawLine.Trim();
                if (trimmed.StartsWith("owns:", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string path in trimmed.Substring(5).Split(','))
                    {
                        string normalized = OwnedPathMatcher.Normalize(path);
                        if (normalized.Length > 0 && !ownedPaths.Contains(normalized)) ownedPaths.Add(normalized);
                    }
                    continue;
                }
                kept.Add(rawLine.TrimEnd('\r'));
            }
            return String.Join("\n", kept).Trim();
        }

        /// <summary>Read a dependency reference from one description line.</summary>
        internal static bool TryExtractArchitectDependency(string line, out string? dependencyReference, out string? remainingDescription)
        {
            dependencyReference = null;
            remainingDescription = null;

            if (String.IsNullOrWhiteSpace(line)) return false;

            string trimmed = line.Trim();
            // "Parent: <n>" (v1.3) and the older "Depends on: <n>" both name the slice this one builds on.
            string keyword = trimmed.StartsWith("parent", StringComparison.OrdinalIgnoreCase) && trimmed.Length > 6 && (trimmed[6] == ':' || trimmed[6] == ' ')
                ? "parent"
                : trimmed.StartsWith("depends on", StringComparison.OrdinalIgnoreCase) ? "depends on" : String.Empty;
            if (keyword.Length == 0)
            {
                return false;
            }

            string remainder = trimmed.Substring(keyword.Length).TrimStart();
            if (remainder.StartsWith(":", StringComparison.Ordinal))
            {
                remainder = remainder.Substring(1).TrimStart();
            }

            if (String.IsNullOrWhiteSpace(remainder)) return false;

            int sentenceBoundary = remainder.IndexOf(". ", StringComparison.Ordinal);
            if (sentenceBoundary >= 0)
            {
                dependencyReference = remainder.Substring(0, sentenceBoundary).Trim().TrimEnd('.');
                remainingDescription = remainder.Substring(sentenceBoundary + 2).Trim();
            }
            else
            {
                dependencyReference = remainder.Trim().TrimEnd('.');
                remainingDescription = "";
            }

            return !String.IsNullOrWhiteSpace(dependencyReference);
        }

        /// <summary>Normalize a dependency reference for matching against slice titles and numbers.</summary>
        internal static string NormalizeArchitectDependencyReference(string dependencyReference)
        {
            if (String.IsNullOrWhiteSpace(dependencyReference)) return "";

            string normalized = dependencyReference.Trim().Trim('"', '\'', '`');
            int commentIndex = normalized.IndexOf(" (", StringComparison.Ordinal);
            if (commentIndex > 0)
            {
                normalized = normalized.Substring(0, commentIndex).Trim();
            }

            return normalized.Trim().TrimEnd('.', ';', ',');
        }

        /// <summary>True for a line of agent telemetry (token counts and similar) rather than plan text.</summary>
        internal static bool IsAgentTelemetryLine(string? line)
        {
            if (String.IsNullOrWhiteSpace(line)) return true;

            string trimmed = line.Trim();
            if (ProgressParser.TryParse(trimmed) != null) return true;

            return trimmed.Equals("tokens used", StringComparison.OrdinalIgnoreCase) ||
                _TelemetryNumberRegex.IsMatch(trimmed);
        }

        /// <summary>True for a slice title that is an unfilled template placeholder.</summary>
        internal static bool IsArchitectPlaceholderTitle(string? title)
        {
            if (String.IsNullOrWhiteSpace(title)) return true;

            string trimmed = title.Trim();
            if (trimmed.Equals("...", StringComparison.Ordinal)) return true;
            if (trimmed.StartsWith("title:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("goal:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("inputs:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("deliverables:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("dependencies:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("risks:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("done_when:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("status:", StringComparison.OrdinalIgnoreCase)) return true;
            if (trimmed.StartsWith("reason:", StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        /// <summary>True for a slice description that is an unfilled template placeholder.</summary>
        internal static bool IsArchitectPlaceholderDescription(string? description)
        {
            if (String.IsNullOrWhiteSpace(description)) return false;

            string[] lines = description
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => !String.IsNullOrEmpty(l))
                .ToArray();

            if (lines.Length == 0) return false;

            string[] placeholderPrefixes =
            {
                "goal:",
                "inputs:",
                "deliverables:",
                "dependencies:",
                "risks:",
                "done_when:"
            };

            return lines.All(line =>
                placeholderPrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) &&
                line.Contains("...", StringComparison.Ordinal));
        }
    }
}
