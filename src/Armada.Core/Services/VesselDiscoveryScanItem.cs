namespace Armada.Core.Services
{
    using System;

    /// <summary>
    /// A directory queued for breadth-first vessel discovery, with its depth below the scan root.
    /// </summary>
    public class VesselDiscoveryScanItem
    {
        #region Public-Members

        /// <summary>
        /// Directory path. Never null.
        /// </summary>
        public string Path
        {
            get => _Path;
            set => _Path = value ?? throw new ArgumentNullException(nameof(Path));
        }

        /// <summary>
        /// Depth below the scan root (the root is 0). Minimum 0.
        /// </summary>
        public int Depth
        {
            get => _Depth;
            set => _Depth = value < 0 ? 0 : value;
        }

        #endregion

        #region Private-Members

        private string _Path = String.Empty;
        private int _Depth = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="path">Directory path.</param>
        /// <param name="depth">Depth below the scan root.</param>
        public VesselDiscoveryScanItem(string path, int depth)
        {
            Path = path;
            Depth = depth;
        }

        #endregion
    }
}
