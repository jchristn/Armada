namespace Armada.Core.Services
{
    using System.Text;
    using System.Text.Json;
    using Armada.Core;
    using Armada.Core.BlackBox;
    using Armada.Core.Database;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;
    using Armada.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Fails missions that changed files outside their declared scope.
    /// </summary>
    internal sealed class MissionScopeGuard
    {
        #region Private-Members

        private readonly MissionService _Service;
        private readonly MissionServiceContext _Context;
        private readonly IGitService? _Git;
        private readonly string _Header;
        private readonly LoggingModule _Logging;
        private readonly ArmadaSettings _Settings;

        private static readonly System.Text.RegularExpressions.Regex _ScopedFilesDirectiveRegex =
            new System.Text.RegularExpressions.Regex(@"^\s*(?:Touch|Edit|Modify)\s+only\s+(?<files>.+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Multiline |
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly System.Text.RegularExpressions.Regex _ScopedFileTokenRegex =
            new System.Text.RegularExpressions.Regex(
                @"(?<path>(?:[A-Za-z0-9_.-]+[\\/])+[A-Za-z0-9_.-]+|[A-Za-z0-9_.-]+\.(?:cs|csproj|sln|md|json|yaml|yml|ts|tsx|js|jsx|css|html|sh|bat))",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                System.Text.RegularExpressions.RegexOptions.Compiled);

        private static readonly HashSet<string> _IgnoredMissionArtifactFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CODEX.md",
            "CLAUDE.md",
            "MUX.md",
            "AGENTS.override.md",
            "ARMADA.md"
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="service">The mission service facade.</param>
        /// <param name="context">State and services shared by the mission services.</param>
        /// <exception cref="ArgumentNullException">A parameter is null.</exception>
        internal MissionScopeGuard(MissionService service, MissionServiceContext context)
        {
            _Service = service ?? throw new ArgumentNullException(nameof(service));
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            _Git = context.Git;
            _Header = context.Header;
            _Logging = context.Logging;
            _Settings = context.Settings;
        }

        #endregion

        #region Internal-Methods

        /// <inheritdoc />
        internal bool IsBroadScope(Mission mission)
        {
            if (mission == null) return false;

            string text = ((mission.Title ?? "") + " " + (mission.Description ?? "")).ToLowerInvariant();

            string[] broadIndicators = new[]
            {
                "refactor entire",
                "refactor all",
                "rename across",
                "migrate project",
                "upgrade framework",
                "restructure",
                "rewrite",
                "overhaul",
                "global search and replace",
                "update all",
                "format all",
                "lint entire"
            };

            foreach (string indicator in broadIndicators)
            {
                if (text.Contains(indicator)) return true;
            }

            return false;
        }

        internal async Task<bool> TryFailMissionForScopeViolationAsync(Mission mission, Dock dock, CancellationToken token)
        {
            if (_Git == null ||
                String.IsNullOrEmpty(mission.Description) ||
                String.IsNullOrEmpty(dock.Id) ||
                String.IsNullOrEmpty(dock.WorktreePath))
            {
                return false;
            }

            HashSet<string> allowedFiles = ParseMissionScopedFiles(mission.Description);
            if (allowedFiles.Count < 1)
            {
                return false;
            }

            string? startCommit = TryReadDockStartCommit(dock, mission);
            if (String.IsNullOrEmpty(startCommit))
            {
                _Logging.Warn(_Header + "scope validation skipped for mission " + mission.Id +
                    " because dock start commit metadata is missing for " + dock.Id);
                return false;
            }

            IReadOnlyList<string> changedFiles;
            try
            {
                DockHost? harborHost = _Context.StageCheckRunner.HarborHostFor(dock);
                IGitService git = harborHost != null ? harborHost.Git : _Git;
                changedFiles = await git.GetChangedFilesSinceAsync(dock.WorktreePath, startCommit, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "scope validation failed for mission " + mission.Id +
                    " while reading changed files: " + ex.Message);
                return false;
            }

            List<string> outOfScopeFiles = FindOutOfScopeFiles(changedFiles, allowedFiles);
            if (outOfScopeFiles.Count < 1)
            {
                return false;
            }

            MissionStatusEnum statusBefore = mission.Status;
            mission.CompletedUtc = DateTime.UtcNow;
            mission.FailureReason = "Mission modified files outside its scoped file list: " + String.Join(", ", outOfScopeFiles);
            mission.FailureKind = MissionFailureKindEnum.ScopeViolation;
            if (!await _Service.Transitions.TryTransitionAsync(mission, new[] { MissionStatusEnum.WorkProduced }, MissionStatusEnum.Failed, null, token).ConfigureAwait(false)) return true;
            _Logging.Warn(_Header + "mission " + mission.Id + " failed scope validation: " + mission.FailureReason);

            // Black box reference capture site: update the row first, then record the failure in one line. The capture
            // never throws and records this method, file and line as the failure site.
            await _Service.BlackBox.RecordMissionFailureAsync(mission, statusBefore, BlackBoxStepEnum.ScopeViolation, context =>
            {
                context.Dock = dock;
                context.ScopedFiles.AddRange(allowedFiles.OrderBy(f => f, StringComparer.Ordinal));
                context.OutOfScopeFiles.AddRange(outOfScopeFiles);
                context.Note = "Scope check base: dock start commit " + startCommit + ".";
            }, token).ConfigureAwait(false);
            return true;
        }

        internal static HashSet<string> ParseMissionScopedFiles(string description)
        {
            HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(description))
            {
                return files;
            }

            foreach (System.Text.RegularExpressions.Match directiveMatch in _ScopedFilesDirectiveRegex.Matches(description))
            {
                string fileSegment = directiveMatch.Groups["files"].Value;
                foreach (System.Text.RegularExpressions.Match pathMatch in _ScopedFileTokenRegex.Matches(fileSegment))
                {
                    string normalizedPath = NormalizeMissionPath(pathMatch.Groups["path"].Value).TrimEnd('.');
                    if (!String.IsNullOrEmpty(normalizedPath))
                    {
                        files.Add(normalizedPath);
                    }
                }
            }

            return files;
        }

        internal string? TryReadDockStartCommit(Dock dock, Mission? mission = null)
        {
            // The .start file is written at provision on the Admiral; the dock's anchors snapshot and the mission row hold
            // the same commit durably (they survive a lost log directory, and exist for Harbor-hosted docks).
            string? fromFile = null;
            try
            {
                string metadataPath = Path.Combine(_Settings.LogDirectory, "docks", dock.Id + ".start");
                if (File.Exists(metadataPath)) fromFile = File.ReadAllText(metadataPath).Trim();
            }
            catch (Exception ex)
            {
                _Logging.Warn(_Header + "could not read dock start commit metadata for " + dock.Id + ": " + ex.ToString());
            }

            if (!String.IsNullOrEmpty(fromFile)) return fromFile;
            string? durable = DockStartCommit.FromDock(dock);
            if (!String.IsNullOrEmpty(durable)) return durable;
            if (mission != null && !String.IsNullOrEmpty(mission.StartCommit)
                && (String.IsNullOrEmpty(mission.DockId) || String.Equals(mission.DockId, dock.Id, StringComparison.Ordinal)))
                return mission.StartCommit;
            return null;
        }

        /// <summary>
        /// Return the changed files that fall outside a mission's scoped allow list. Empty paths (a transport artifact)
        /// and the runtime instruction files are never reported. An allow-list entry with a directory must match the
        /// repo-relative path exactly (case-insensitive); a bare file name matches a changed file with that file name
        /// in any directory.
        /// </summary>
        /// <param name="changedFiles">Repo-relative paths changed since the dock's start commit.</param>
        /// <param name="allowedFiles">The scoped allow list parsed from the mission description.</param>
        /// <returns>The normalized out-of-scope paths, without empty entries or duplicates.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
        internal static List<string> FindOutOfScopeFiles(IEnumerable<string> changedFiles, ICollection<string> allowedFiles)
        {
            if (changedFiles == null) throw new ArgumentNullException(nameof(changedFiles));
            if (allowedFiles == null) throw new ArgumentNullException(nameof(allowedFiles));

            HashSet<string> exactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> bareNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string allowed in allowedFiles)
            {
                string normalizedAllowed = NormalizeMissionPath(allowed);
                if (String.IsNullOrEmpty(normalizedAllowed)) continue;
                if (normalizedAllowed.IndexOf('/') < 0) bareNames.Add(normalizedAllowed);
                else exactPaths.Add(normalizedAllowed);
            }

            List<string> outOfScope = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string changedFile in changedFiles)
            {
                string normalizedPath = NormalizeMissionPath(changedFile);
                if (String.IsNullOrEmpty(normalizedPath)) continue;
                if (_IgnoredMissionArtifactFiles.Contains(normalizedPath)) continue;
                if (exactPaths.Contains(normalizedPath)) continue;

                int slash = normalizedPath.LastIndexOf('/');
                string fileName = slash >= 0 ? normalizedPath.Substring(slash + 1) : normalizedPath;
                if (fileName.Length > 0 && bareNames.Contains(fileName)) continue;

                if (seen.Add(normalizedPath)) outOfScope.Add(normalizedPath);
            }

            return outOfScope;
        }

        #endregion

        #region Private-Methods

        private static string NormalizeMissionPath(string path)
        {
            string normalized = (path ?? String.Empty).Trim().Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal)) normalized = normalized.Substring(2);
            return normalized;
        }

        #endregion
    }
}
