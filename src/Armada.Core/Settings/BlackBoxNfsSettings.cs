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

        /// <summary>
        /// NFS protocol version (2 to 4, default 3).
        /// </summary>
        public int Version
        {
            get => _Version;
            set => _Version = Math.Clamp(value, 2, 4);
        }

        /// <summary>
        /// NFS port, or null to discover it through the portmapper (2049 by convention).
        /// </summary>
        public int? Port
        {
            get => _Port;
            set => _Port = value.HasValue ? Math.Clamp(value.Value, 1, 65535) : (int?)null;
        }

        /// <summary>
        /// MOUNT protocol port, or null to discover it through the portmapper.
        /// </summary>
        public int? MountPort
        {
            get => _MountPort;
            set => _MountPort = value.HasValue ? Math.Clamp(value.Value, 1, 65535) : (int?)null;
        }

        /// <summary>
        /// Numeric user id sent with AUTH_SYS credentials (0 to 2147483647, default 0).
        /// </summary>
        public int UserId
        {
            get => _UserId;
            set => _UserId = Math.Max(0, value);
        }

        /// <summary>
        /// Numeric group id sent with AUTH_SYS credentials (0 to 2147483647, default 0).
        /// </summary>
        public int GroupId
        {
            get => _GroupId;
            set => _GroupId = Math.Max(0, value);
        }

        private string? _Hostname = null;
        private string? _ExportPath = null;
        private int _Version = 3;
        private int? _Port = null;
        private int? _MountPort = null;
        private int _UserId = 0;
        private int _GroupId = 0;

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxNfsSettings()
        {
        }
    }
}
