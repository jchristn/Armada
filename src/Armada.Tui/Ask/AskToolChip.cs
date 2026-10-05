namespace Armada.Tui.Ask
{
    using System;

    /// <summary>
    /// One tool call shown as a chip above a captain reply (live from <c>ask.tool</c> events or persisted on the
    /// message). Mirrors the dashboard's <c>ToolEvent</c>.
    /// </summary>
    public class AskToolChip
    {
        #region Public-Members

        /// <summary>
        /// Call id.
        /// </summary>
        public string Id
        {
            get { return _Id; }
            set { _Id = String.IsNullOrEmpty(value) ? "call" : value; }
        }

        /// <summary>
        /// Tool name.
        /// </summary>
        public string Name
        {
            get { return _Name; }
            set { _Name = String.IsNullOrEmpty(value) ? "tool" : value; }
        }

        /// <summary>
        /// Status.
        /// </summary>
        public AskToolChipStatusEnum Status { get; set; } = AskToolChipStatusEnum.Running;

        /// <summary>
        /// Arguments text (usually JSON), or null.
        /// </summary>
        public string? Arguments { get; set; } = null;

        /// <summary>
        /// Result text (usually JSON), or null.
        /// </summary>
        public string? Result { get; set; } = null;

        /// <summary>
        /// Elapsed milliseconds, or null.
        /// </summary>
        public double? ElapsedMs { get; set; } = null;

        /// <summary>
        /// True when the CLI refused the call because the turn's CLI tool permission policy did not grant it.
        /// </summary>
        public bool PermissionDenied { get; set; } = false;

        #endregion

        #region Private-Members

        private string _Id = "call";
        private string _Name = "tool";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public AskToolChip()
        {
        }

        #endregion
    }
}
