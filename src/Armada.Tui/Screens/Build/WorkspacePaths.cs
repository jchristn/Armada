namespace Armada.Tui.Screens.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// Workspace path and draft helpers, ported from the dashboard's <c>workspaceUtils.ts</c>: path normalization,
    /// names and parents, language inference, the "Touch only ..." scope directive, the Plan and Dispatch drafts,
    /// and the context snippet built from selected files.
    /// </summary>
    public static class WorkspacePaths
    {
        #region Public-Methods

        /// <summary>
        /// Forward slashes, no leading or trailing slash.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>Normalized path.</returns>
        public static string Normalize(string? path)
        {
            return (path ?? "").Replace('\\', '/').Trim('/');
        }

        /// <summary>
        /// The last path segment.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>Name.</returns>
        public static string Name(string? path)
        {
            string n = Normalize(path);
            if (n.Length == 0) return "";
            int idx = n.LastIndexOf('/');
            return idx >= 0 ? n.Substring(idx + 1) : n;
        }

        /// <summary>
        /// The parent folder, or empty for a root entry.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>Parent.</returns>
        public static string Parent(string? path)
        {
            string n = Normalize(path);
            int idx = n.LastIndexOf('/');
            return idx > 0 ? n.Substring(0, idx) : "";
        }

        /// <summary>
        /// True when a path is the scope path or inside it.
        /// </summary>
        /// <param name="candidate">Candidate.</param>
        /// <param name="scope">Scope.</param>
        /// <returns>True when in scope.</returns>
        public static bool InScope(string candidate, string scope)
        {
            return candidate == scope || candidate.StartsWith(scope + "/", StringComparison.Ordinal);
        }

        /// <summary>
        /// Move a path that is in a renamed scope to the new scope.
        /// </summary>
        /// <param name="candidate">Candidate.</param>
        /// <param name="source">Old scope.</param>
        /// <param name="target">New scope.</param>
        /// <returns>Remapped path.</returns>
        public static string Remap(string candidate, string source, string target)
        {
            if (candidate == source) return target;
            if (candidate.StartsWith(source + "/", StringComparison.Ordinal)) return target + candidate.Substring(source.Length);
            return candidate;
        }

        /// <summary>
        /// Language for highlighting, from the file extension.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>Language id.</returns>
        public static string InferLanguage(string? path)
        {
            string n = Normalize(path).ToLowerInvariant();
            if (n.EndsWith(".cs", StringComparison.Ordinal)) return "csharp";
            if (n.EndsWith(".csproj", StringComparison.Ordinal) || n.EndsWith(".xml", StringComparison.Ordinal)) return "xml";
            if (n.EndsWith(".md", StringComparison.Ordinal)) return "markdown";
            if (n.EndsWith(".json", StringComparison.Ordinal)) return "json";
            if (n.EndsWith(".ts", StringComparison.Ordinal) || n.EndsWith(".tsx", StringComparison.Ordinal)) return "typescript";
            if (n.EndsWith(".js", StringComparison.Ordinal) || n.EndsWith(".jsx", StringComparison.Ordinal)) return "javascript";
            if (n.EndsWith(".css", StringComparison.Ordinal)) return "css";
            if (n.EndsWith(".html", StringComparison.Ordinal)) return "html";
            if (n.EndsWith(".yml", StringComparison.Ordinal) || n.EndsWith(".yaml", StringComparison.Ordinal)) return "yaml";
            if (n.EndsWith(".ps1", StringComparison.Ordinal)) return "powershell";
            if (n.EndsWith(".bat", StringComparison.Ordinal)) return "bat";
            if (n.EndsWith(".sql", StringComparison.Ordinal)) return "sql";
            return "plaintext";
        }

        /// <summary>
        /// "Touch only a, b" for the selected paths, or empty.
        /// </summary>
        /// <param name="paths">Paths.</param>
        /// <returns>Directive.</returns>
        public static string ScopeDirective(IEnumerable<string> paths)
        {
            List<string> n = (paths ?? Enumerable.Empty<string>()).Select(Normalize).Where(p => p.Length > 0).ToList();
            return n.Count == 0 ? "" : "Touch only " + String.Join(", ", n);
        }

        /// <summary>
        /// The Plan draft for a selection.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="paths">Selected paths.</param>
        /// <returns>Title and prompt.</returns>
        public static WorkspaceDraft PlanningDraft(Vessel vessel, IList<string> paths)
        {
            string primary = Name(paths.Count > 0 ? paths[0] : vessel.Name);
            List<string> lines = new List<string>
            {
                ScopeDirective(paths),
                "",
                "Help me plan the changes needed in vessel \"" + vessel.Name + "\".",
                "Review the selected files, identify likely dependencies, and outline a concrete implementation approach before dispatch.",
                "Call out risks, affected areas, and any follow-up files that may need to be touched if the current scope is too narrow.",
            };
            return new WorkspaceDraft("Plan " + primary, String.Join("\n", lines.Where(l => l.Length > 0)));
        }

        /// <summary>
        /// The Dispatch draft for a selection.
        /// </summary>
        /// <param name="vessel">Vessel.</param>
        /// <param name="paths">Selected paths.</param>
        /// <returns>Title and prompt.</returns>
        public static WorkspaceDraft DispatchDraft(Vessel vessel, IList<string> paths)
        {
            string primary = Name(paths.Count > 0 ? paths[0] : vessel.Name);
            List<string> lines = new List<string>
            {
                ScopeDirective(paths),
                "",
                "Implement the requested change in vessel \"" + vessel.Name + "\".",
                "Use the selected files as the primary scope. If adjacent files are required, expand carefully and explain why.",
                "Update tests and documentation when the change requires it.",
            };
            return new WorkspaceDraft("Workspace: " + primary, String.Join("\n", lines.Where(l => l.Length > 0)));
        }

        /// <summary>
        /// The "Workspace Selection" Markdown snippet for context curation.
        /// </summary>
        /// <param name="files">Path and content pairs.</param>
        /// <returns>Snippet.</returns>
        public static string ContextSnippet(IEnumerable<KeyValuePair<string, string>> files)
        {
            List<string> parts = new List<string> { "## Workspace Selection", "" };
            foreach (KeyValuePair<string, string> f in files ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                parts.Add("### " + f.Key + "\n```text\n" + (f.Value ?? "").Trim() + "\n```");
            }

            return String.Join("\n", parts);
        }

        #endregion
    }
}
