namespace Armada.Tui.Widgets
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
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
                string normalized = _Diff.Replace("\r\n", "\n");
                _Lines = normalized.Split('\n').ToList();
                List<UnifiedDiffFile> parsed = UnifiedDiffParser.Parse(normalized, out List<UnifiedDiffLineKindEnum> kinds);
                _Kinds = kinds;
                Files = parsed.Select(f => f.DisplayPath).ToList();
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
        private List<UnifiedDiffLineKindEnum> _Kinds = new List<UnifiedDiffLineKindEnum>();

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
            for (int i = 0; i < _Lines.Count; i++)
            {
                string line = _Lines[i];
                UnifiedDiffLineKindEnum kind = i < _Kinds.Count ? _Kinds[i] : UnifiedDiffLineKindEnum.Other;
                CellStyle style;
                switch (kind)
                {
                    case UnifiedDiffLineKindEnum.FileHeader: style = Theme.Accent; break;
                    case UnifiedDiffLineKindEnum.Meta: style = Theme.Muted; break;
                    case UnifiedDiffLineKindEnum.HunkHeader: style = Theme.Info; break;
                    case UnifiedDiffLineKindEnum.Added: style = Theme.Success; break;
                    case UnifiedDiffLineKindEnum.Deleted: style = Theme.Error; break;
                    case UnifiedDiffLineKindEnum.NoNewline: style = Theme.Muted; break;
                    default: style = Theme.Text; break;
                }

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

        #endregion
    }
}
