namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Black box NFS (v3) target settings (blackBox.storage.nfs).
    /// </summary>
    public class BlackBoxNfsSettings
    {
        /// <summary>
        /// Server host name.
        /// </summary>
        public string? Hostname
        {
            get => _Hostname;
            set => _Hostname = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        /// <summary>
        /// Export path.
        /// </summary>
        public string? ExportPath
        {
            get => _ExportPath;
            set => _ExportPath = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private string? _Hostname = null;
        private string? _ExportPath = null;

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxNfsSettings()
        {
        }
    }
}
