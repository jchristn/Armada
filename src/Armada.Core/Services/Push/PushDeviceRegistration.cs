namespace Armada.Core.Services.Push
{
    using System;
    using Armada.Core.Models;

    /// <summary>
    /// Result of a device registration: the device and whether it was newly created.
    /// </summary>
    public class PushDeviceRegistration
    {
        #region Public-Members

        /// <summary>
        /// The registered device (token masked).
        /// </summary>
        public PushDevice Device { get; }

        /// <summary>
        /// True when the token was not known before.
        /// </summary>
        public bool Created { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="device">Device.</param>
        /// <param name="created">Whether it was created.</param>
        public PushDeviceRegistration(PushDevice device, bool created)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
            Created = created;
        }

        #endregion
    }
}
