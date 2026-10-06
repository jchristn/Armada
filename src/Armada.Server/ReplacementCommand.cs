namespace Armada.Server
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The command line a replacement Admiral is started with by <c>server/restart</c>, rebuild, and rollback:
    /// the program (a native server executable, or the dotnet host for a framework-dependent server), its arguments
    /// (for the dotnet host, the server assembly first), and the working directory. Built by the pure methods of
    /// <see cref="ReplacementProcessLauncher"/>.
    /// </summary>
    public class ReplacementCommand
    {
        #region Public-Members

        /// <summary>
        /// Program to start: the server executable, or the dotnet host.
        /// </summary>
        public string FileName
        {
            get { return _FileName; }
            set { _FileName = !String.IsNullOrEmpty(value) ? value : throw new ArgumentNullException(nameof(FileName)); }
        }

        /// <summary>
        /// Arguments passed to <see cref="FileName"/>; for the dotnet host the first one is the server assembly.
        /// </summary>
        public List<string> Arguments
        {
            get { return _Arguments; }
            set { _Arguments = value ?? new List<string>(); }
        }

        /// <summary>
        /// Working directory of the replacement (the directory of the server executable or assembly).
        /// </summary>
        public string WorkingDirectory
        {
            get { return _WorkingDirectory; }
            set { _WorkingDirectory = value ?? String.Empty; }
        }

        /// <summary>
        /// True when <see cref="FileName"/> is the dotnet host running a server assembly.
        /// </summary>
        public bool IsDotnetHosted { get; set; } = false;

        #endregion

        #region Private-Members

        private string _FileName = "Armada.Server";
        private List<string> _Arguments = new List<string>();
        private string _WorkingDirectory = String.Empty;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ReplacementCommand()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The command line for logs, arguments quoted when they contain spaces.
        /// </summary>
        /// <returns>Command line text.</returns>
        public override string ToString()
        {
            List<string> parts = new List<string>();
            parts.Add(Quote(_FileName));
            foreach (string argument in _Arguments) parts.Add(Quote(argument));
            return String.Join(" ", parts);
        }

        #endregion

        #region Private-Methods

        private static string Quote(string value)
        {
            if (String.IsNullOrEmpty(value)) return "\"\"";
            return value.IndexOf(' ') >= 0 ? "\"" + value + "\"" : value;
        }

        #endregion
    }
}
