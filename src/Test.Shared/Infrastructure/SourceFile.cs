namespace Test.Shared.Infrastructure
{
    using System;
    using System.Text.RegularExpressions;

    /// <summary>
    /// One C# source file read for a source scan: its path relative to <c>src</c>, its lines with comments removed, and
    /// the types it declares.
    /// </summary>
    public sealed class SourceFile
    {
        #region Public-Members

        /// <summary>
        /// Path relative to the repository's <c>src</c> folder, with forward slashes.
        /// </summary>
        public string Relative { get; }

        /// <summary>
        /// Each line with its line comment, XML documentation and block-comment text replaced by an empty string.
        /// </summary>
        public string[] Code { get; }

        #endregion

        #region Private-Members

        private readonly string _Text;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="relative">Path relative to <c>src</c>.</param>
        /// <param name="lines">The file's lines.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="relative"/> or <paramref name="lines"/> is null.</exception>
        public SourceFile(string relative, string[] lines)
        {
            Relative = relative ?? throw new ArgumentNullException(nameof(relative));
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            Code = new string[lines.Length];
            bool inBlock = false;
            for (int i = 0; i < lines.Length; i++) Code[i] = StripComments(lines[i], ref inBlock);
            _Text = String.Join("\n", Code);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the file declares a class, record, struct or interface with this name (partial declarations count).
        /// </summary>
        /// <param name="typeName">Type name without namespace.</param>
        /// <returns>True when declared here.</returns>
        public bool Declares(string typeName)
        {
            if (String.IsNullOrEmpty(typeName)) return false;
            return Regex.IsMatch(_Text, @"\b(class|record|struct|interface)\s+" + Regex.Escape(typeName) + @"\b");
        }

        /// <summary>
        /// A location for a failure message: path, line number and the trimmed code.
        /// </summary>
        /// <param name="index">Zero-based line index.</param>
        /// <returns>The location text.</returns>
        public string Location(int index)
        {
            return Relative + ":" + (index + 1) + ": " + Code[index].Trim();
        }

        #endregion

        #region Private-Methods

        private static string StripComments(string line, ref bool inBlock)
        {
            string code = line;
            if (inBlock)
            {
                int end = code.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0) return String.Empty;
                code = code.Substring(end + 2);
                inBlock = false;
            }

            int start = code.IndexOf("/*", StringComparison.Ordinal);
            if (start >= 0 && !InString(code, start))
            {
                int end = code.IndexOf("*/", start + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    inBlock = true;
                    code = code.Substring(0, start);
                }
                else code = code.Substring(0, start) + code.Substring(end + 2);
            }

            int comment = code.IndexOf("//", StringComparison.Ordinal);
            while (comment >= 0 && InString(code, comment)) comment = code.IndexOf("//", comment + 2, StringComparison.Ordinal);
            return comment >= 0 ? code.Substring(0, comment) : code;
        }

        private static bool InString(string code, int index)
        {
            // Counts unescaped double quotes before the index; good enough for one-line C# literals.
            bool inside = false;
            for (int i = 0; i < index; i++)
            {
                if (code[i] == '\\' && inside) { i++; continue; }
                if (code[i] == '"') inside = !inside;
            }
            return inside;
        }

        #endregion
    }
}
