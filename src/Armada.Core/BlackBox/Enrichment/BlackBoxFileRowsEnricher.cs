namespace Armada.Core.BlackBox.Enrichment
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Runs last: builds the complete blackbox_files and blackbox_links rows for the entry. It re-emits the roles the
    /// record holds (Conflicted, DirtyInCheckout, OutOfScope, BoundaryViolation for findings that name a path) next to
    /// the rows earlier enrichers added (ChangedByMission, ChangedByConflictingMission), then removes duplicates. The
    /// pipeline replaces the entry's rows with this set, so a re-run (reanalyze) never duplicates them.
    /// </summary>
    public class BlackBoxFileRowsEnricher : BlackBoxEnricherBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override string Name => "FileRows";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when logging is null.</exception>
        public BlackBoxFileRowsEnricher(LoggingModule logging) : base(logging)
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The path a boundary finding names (its first token that looks like a repository path), or null.
        /// </summary>
        /// <param name="finding">Finding text.</param>
        /// <returns>The path, or null.</returns>
        public static string? PathOfFinding(string? finding)
        {
            if (String.IsNullOrWhiteSpace(finding)) return null;
            foreach (string raw in finding!.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string token = raw.Trim('`', '"', '\'', ',', ';', '(', ')', '[', ']').TrimEnd(':', '.');
                if (token.Length == 0 || token.Contains("://", StringComparison.Ordinal)) continue;
                if (token.Contains('/') || (token.Contains('.') && !token.EndsWith(".", StringComparison.Ordinal) && Char.IsLetterOrDigit(token[token.Length - 1])))
                    return BlackBoxForensics.NormalizePath(token);
            }
            return null;
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override Task EnrichCoreAsync(BlackBoxEnrichmentContext context, CancellationToken token)
        {
            BlackBoxRecord record = context.Record;
            BlackBoxEntry entry = context.Entry;
            List<BlackBoxFile> rows = new List<BlackBoxFile>();
            if (record.Conflict != null) AddRole(rows, entry, record.Conflict.ConflictedFiles, BlackBoxFileRoleEnum.Conflicted);
            if (record.Workspace != null) AddRole(rows, entry, record.Workspace.DirtyPaths, BlackBoxFileRoleEnum.DirtyInCheckout);
            if (record.Scope != null)
            {
                AddRole(rows, entry, record.Scope.OutOfScopeFiles.Where(f => !String.IsNullOrWhiteSpace(f)), BlackBoxFileRoleEnum.OutOfScope);
                AddRole(rows, entry, record.Scope.BoundaryFindings.Select(PathOfFinding).Where(p => p != null).Select(p => p!), BlackBoxFileRoleEnum.BoundaryViolation);
            }
            rows.AddRange(context.Files);

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<BlackBoxFile> files = new List<BlackBoxFile>();
            foreach (BlackBoxFile row in rows)
            {
                if (String.IsNullOrWhiteSpace(row.Path)) continue;
                if (!seen.Add(row.Role + "|" + row.Path + "|" + (row.ConflictingMissionId ?? String.Empty))) continue;
                files.Add(row);
            }
            context.Files = files;

            HashSet<string> seenLinks = new HashSet<string>(StringComparer.Ordinal);
            context.Links = context.Links.Where(l => !String.IsNullOrWhiteSpace(l.MissionId) && l.MissionId != entry.MissionId
                && seenLinks.Add(l.Relation + "|" + l.MissionId + "|" + (l.CommitHash ?? String.Empty))).ToList();
            return Task.CompletedTask;
        }

        private static void AddRole(List<BlackBoxFile> rows, BlackBoxEntry entry, IEnumerable<string> paths, BlackBoxFileRoleEnum role)
        {
            foreach (string p in paths.Take(BlackBoxForensics.MaxFileRowsPerRole)) rows.Add(BlackBoxForensics.FileRow(entry, p, role));
        }

        #endregion
    }
}
