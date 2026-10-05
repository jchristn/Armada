namespace Armada.Tui.Screens.Configuration
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Client;
    using Armada.Tui.Widgets;

    /// <summary>
    /// Shared pieces of the workflow profile and project profile screens: the Global/Fleet/Vessel scope options, the
    /// status filter options, the dashboard's known persona names, deep copies of records, and a line diff of two
    /// prompts for the Persona Prompt Diff panel. Thread-safe (stateless).
    /// </summary>
    public static class ProfileFormSupport
    {
        #region Public-Members

        /// <summary>
        /// Persona names the dashboard suggests (<c>KNOWN_PERSONAS</c> in ProjectProfileDetail.tsx).
        /// </summary>
        public static readonly string[] KnownPersonas = new[] { "Product Manager", "Architect", "Worker", "Test Engineer", "Judge", "Usability Engineer" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Global, Fleet, and Vessel options (labels translated).
        /// </summary>
        /// <param name="context">Context.</param>
        /// <returns>Options.</returns>
        public static List<SelectOption<string>> ScopeOptions(TuiContext context)
        {
            return new List<SelectOption<string>>
            {
                new SelectOption<string>("Global", context.Loc.T("Global")),
                new SelectOption<string>("Fleet", context.Loc.T("Fleet")),
                new SelectOption<string>("Vessel", context.Loc.T("Vessel"))
            };
        }

        /// <summary>
        /// Active only / Inactive only options for the status filter.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <returns>Options.</returns>
        public static List<SelectOption<string>> StatusOptions(TuiContext context)
        {
            return new List<SelectOption<string>>
            {
                new SelectOption<string>("active", context.Loc.T("Active only")),
                new SelectOption<string>("inactive", context.Loc.T("Inactive only"))
            };
        }

        /// <summary>
        /// Deep copy of a record through JSON.
        /// </summary>
        /// <typeparam name="T">Record type.</typeparam>
        /// <param name="value">Record.</param>
        /// <returns>Copy.</returns>
        public static T Clone<T>(T value) where T : class
        {
            T? copy = ArmadaJson.Deserialize<T>(ArmadaJson.Serialize(value));
            if (copy == null) throw new InvalidOperationException("Unable to copy " + typeof(T).Name + ".");
            return copy;
        }

        /// <summary>
        /// "Name (Copy)" (dashboard <c>duplicateDisplayName</c>).
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Duplicate name.</returns>
        public static string DuplicateName(string? name)
        {
            string trimmed = (name ?? "").Trim();
            return trimmed.Length > 0 ? trimmed + " (Copy)" : "Copy";
        }

        /// <summary>
        /// A unified line diff of two texts (longest common subsequence), suitable for <see cref="DiffViewer"/>.
        /// </summary>
        /// <param name="baseName">Label of the base text.</param>
        /// <param name="baseText">Base text.</param>
        /// <param name="effectiveName">Label of the effective text.</param>
        /// <param name="effectiveText">Effective text.</param>
        /// <returns>Diff text.</returns>
        public static string LineDiff(string baseName, string? baseText, string effectiveName, string? effectiveText)
        {
            string[] a = (baseText ?? "").Replace("\r\n", "\n").Split('\n');
            string[] b = (effectiveText ?? "").Replace("\r\n", "\n").Split('\n');
            int n = a.Length;
            int m = b.Length;
            int[,] lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    lcs[i, j] = String.Equals(a[i], b[j], StringComparison.Ordinal) ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("--- ").Append(baseName).Append('\n');
            sb.Append("+++ ").Append(effectiveName).Append('\n');
            sb.Append("@@ -1,").Append(n).Append(" +1,").Append(m).Append(" @@").Append('\n');
            int x = 0;
            int y = 0;
            while (x < n || y < m)
            {
                if (x < n && y < m && String.Equals(a[x], b[y], StringComparison.Ordinal))
                {
                    sb.Append(' ').Append(a[x]).Append('\n');
                    x++;
                    y++;
                }
                else if (y < m && (x >= n || lcs[x, y + 1] >= lcs[x + 1, y]))
                {
                    sb.Append('+').Append(b[y]).Append('\n');
                    y++;
                }
                else
                {
                    sb.Append('-').Append(a[x]).Append('\n');
                    x++;
                }
            }

            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Count non-empty values.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <returns>Count.</returns>
        public static int CountSet(params string?[] values)
        {
            return values.Count(v => !String.IsNullOrWhiteSpace(v));
        }

        #endregion
    }
}
