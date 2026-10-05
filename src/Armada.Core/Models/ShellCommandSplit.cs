namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A shell command line split at its control operators (see <see cref="Armada.Core.Services.ShellCommandSplitter"/>).
    /// </summary>
    public class ShellCommandSplit
    {
        #region Public-Members

        /// <summary>
        /// The simple commands, trimmed, in order; empty segments are dropped.
        /// </summary>
        public List<string> Commands
        {
            get => _Commands;
            set => _Commands = value ?? new List<string>();
        }

        /// <summary>
        /// True when the line contains command or process substitution outside single quotes (<c>$(</c>, a backtick,
        /// <c>&lt;(</c>, or <c>&gt;(</c>), whose inner command a prefix rule cannot see.
        /// </summary>
        public bool HasSubstitution { get; set; } = false;

        /// <summary>
        /// True when quoting is unbalanced (the line cannot be split reliably).
        /// </summary>
        public bool Unbalanced { get; set; } = false;

        #endregion

        #region Private-Members

        private List<string> _Commands = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ShellCommandSplit()
        {
        }

        #endregion
    }
}
