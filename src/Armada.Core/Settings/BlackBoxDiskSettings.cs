namespace Armada.Core.Settings
{
    using System;

    /// <summary>
    /// Black box Disk target settings (blackBox.storage.disk).
    /// </summary>
    public class BlackBoxDiskSettings
    {
        /// <summary>
        /// Directory for objects. Null means {DataDirectory}/blackbox. Object paths outside it and symlinked object paths are refused.
        /// </summary>
        public string? Directory
        {
            get => _Directory;
            set => _Directory = String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private string? _Directory = null;

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public BlackBoxDiskSettings()
        {
        }
    }
}
