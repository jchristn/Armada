namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TUIKit;
    using TUIKit.Input;

    /// <summary>
    /// Unified diff viewer for mission and workspace diffs: additions, removals, hunk headers, and file headers are
    /// styled by role (with the +/- markers kept, so color is never the only signal); <c>]</c>/<c>[</c> jump between
    /// files and the file count is shown in <see cref="Files"/>. Not thread-safe.
    /// </summary>
    public class DiffViewer : ScrollTextView
    {
        #region Public-Members

        /// <summary>
        /// Unified diff text.
        /// </summary>
        public string Diff
        {
            get { return _Diff; }
            set
            {
                _Diff = value ?? "";
                _Lines = _Diff.Replace("\r\n", "\n").Split('\n').ToList();
                Files = _Lines.Where(l => l.StartsWith("diff --git ", StringComparison.Ordinal)).Select(FileName).ToList();
                Invalidate();
            }
        }

        /// <summary>
        /// Changed files in order.
        /// </summary>
        public List<string> Files { get; private set; } = new List<string>();

        /// <inheritdoc />
        public override string PlainText
        {
            get { return _Diff; }
        }

        #endregion

        #region Private-Members

        private string _Diff = "";
        private List<string> _Lines = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="diff">Unified diff.</param>
        public DiffViewer(string diff = "")
        {
            Diff = diff;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override bool HandleKey(KeyEvent key)
        {
            if (!Searching && key.Code == KeyCode.Character && key.Modifiers == KeyModifiers.None && (key.Rune == ']' || key.Rune == '['))
            {
                return JumpFile(key.Rune == ']');
            }

            return base.HandleKey(key);
        }

        #endregion

        #region Protected-Methods

        /// <inheritdoc />
        protected override IReadOnlyList<StyledText> BuildLines()
        {
            List<StyledText> lines = new List<StyledText>();
            foreach (string line in _Lines)
            {
                CellStyle style;
                if (line.StartsWith("diff --git ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal)) style = Theme.Accent;
                else if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)) style = Theme.Muted;
                else if (line.StartsWith("@@", StringComparison.Ordinal)) style = Theme.Info;
                else if (line.StartsWith("+", StringComparison.Ordinal)) style = Theme.Success;
                else if (line.StartsWith("-", StringComparison.Ordinal)) style = Theme.Error;
                else style = Theme.Text;
                lines.Add(StyledText.From(line.Replace("\t", "    "), style));
            }

            if (lines.Count == 1 && lines[0].ToPlainString().Length == 0) lines.Clear();
            return lines;
        }

        /// <inheritdoc />
        protected override string EmptyText()
        {
            return "No changes.";
        }

        #endregion

        #region Private-Methods

        private bool JumpFile(bool forward)
        {
            if (Files.Count == 0) return true;
            Search("diff --git ");
            if (!forward) FindNext(false);
            return true;
        }

        private static string FileName(string header)
        {
            int b = header.LastIndexOf(" b/", StringComparison.Ordinal);
            return b >= 0 ? header.Substring(b + 3) : header.Substring("diff --git ".Length);
        }

        #endregion
    }
}
