namespace Armada.Core.Hosting
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One desktop shell launch (open a folder, reveal a file, open a file in its editor), described as data so the
    /// per-platform command lines can be checked without starting a process.
    /// </summary>
    public class ShellLaunch
    {
        #region Public-Members

        /// <summary>
        /// Program to start, or the document itself when <see cref="UseShellExecute"/> is true.
        /// </summary>
        public string FileName
        {
            get { return _FileName; }
            set { _FileName = value ?? throw new ArgumentNullException(nameof(FileName)); }
        }

        /// <summary>
        /// Arguments passed one by one (quoted by the runtime). Ignored when <see cref="RawArguments"/> is set.
        /// </summary>
        public List<string> Arguments
        {
            get { return _Arguments; }
            set { _Arguments = value ?? new List<string>(); }
        }

        /// <summary>
        /// Verbatim argument string, for programs with their own parsing rules (explorer.exe /select,"path").
        /// </summary>
        public string? RawArguments { get; set; } = null;

        /// <summary>
        /// True to let the operating system shell pick the handler for <see cref="FileName"/> (Windows associations).
        /// </summary>
        public bool UseShellExecute { get; set; } = false;

        #endregion

        #region Private-Members

        private string _FileName = String.Empty;
        private List<string> _Arguments = new List<string>();

        #endregion
    }
}
