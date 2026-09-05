namespace Armada.Core.Models
{
    using System;

    /// <summary>
    /// A single capability a Harbor advertises: an installed agent runtime (e.g. "claude", "codex") or a
    /// host tool (e.g. "git", "gh"), and whether it is currently available. Used by the router to match a
    /// mission's requested runtime and required capabilities to an eligible Harbor.
    /// </summary>
    public class HarborCapability
    {
        #region Public-Members

        /// <summary>
        /// Capability name, for example a runtime identifier ("claude", "codex", "opencode") or a tool
        /// name ("git", "gh"). Required.
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value.Trim();
            }
        }

        /// <summary>
        /// Whether the capability is currently available on the Harbor host.
        /// </summary>
        public bool Available { get; set; } = true;

        /// <summary>
        /// Optional detail such as a resolved version or path. Never carries secrets.
        /// </summary>
        public string? Detail { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Name = "unknown";

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public HarborCapability()
        {
        }

        #endregion
    }
}
