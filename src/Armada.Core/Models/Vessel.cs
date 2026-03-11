namespace Armada.Core.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A git repository registered with Armada.
    /// </summary>
    public class Vessel
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier.
        /// </summary>
        public string Id
        {
            get => _Id;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Fleet identifier this vessel belongs to.
        /// </summary>
        public string? FleetId { get; set; } = null;

        /// <summary>
        /// Vessel name.
        /// </summary>
        public string Name
        {
            get => _Name;
            set
            {
                if (String.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(Name));
                _Name = value;
            }
        }

        /// <summary>
        /// Remote repository URL.
        /// </summary>
        public string? RepoUrl
        {
            get => _RepoUrl;
            set => _RepoUrl = value;
        }

        /// <summary>
        /// Local path to the bare repository clone.
        /// </summary>
        public string? LocalPath { get; set; } = null;

        /// <summary>
        /// Local working directory (the user's checkout) for local merge on completion.
        /// </summary>
        public string? WorkingDirectory { get; set; } = null;

        /// <summary>
        /// Default branch name.
        /// </summary>
        public string DefaultBranch { get; set; } = "main";

        /// <summary>
        /// Project context describing what the project is, its architecture, key files, and dependencies.
        /// </summary>
        public string? ProjectContext { get; set; } = null;

        /// <summary>
        /// Style guide describing naming conventions, patterns, language restrictions, and library preferences.
        /// </summary>
        public string? StyleGuide { get; set; } = null;

        /// <summary>
        /// Whether the vessel is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Creation timestamp in UTC.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Last update timestamp in UTC.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.VesselIdPrefix, 24);
        private string _Name = "My Vessel";
        private string? _RepoUrl = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public Vessel()
        {
        }

        /// <summary>
        /// Instantiate with name and repository URL.
        /// </summary>
        /// <param name="name">Vessel name.</param>
        /// <param name="repoUrl">Remote repository URL.</param>
        public Vessel(string name, string repoUrl)
        {
            Name = name;
            RepoUrl = repoUrl;
        }

        #endregion
    }
}
