namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Body of POST /api/v1/push/devices: registers (or refreshes) the calling user's device by its Expo push token.
    /// </summary>
    public class PushDeviceRegisterRequest
    {
        #region Public-Members

        /// <summary>
        /// Platform. Required.
        /// </summary>
        public PushPlatformEnum? Platform { get; set; } = null;

        /// <summary>
        /// Expo push token (ExponentPushToken[...] or ExpoPushToken[...]). Required.
        /// </summary>
        public string? ExpoPushToken { get; set; } = null;

        /// <summary>
        /// Device name (at most 128 characters).
        /// </summary>
        public string? DeviceName { get; set; } = null;

        /// <summary>
        /// App version (at most 64 characters).
        /// </summary>
        public string? AppVersion { get; set; } = null;

        /// <summary>
        /// Device locale (at most 35 characters).
        /// </summary>
        public string? Locale { get; set; } = null;

        /// <summary>
        /// Enabled categories. Null keeps the categories of a device the caller already registered, and uses the
        /// server's Push.Categories defaults for a new device (including a token another user registered before).
        /// </summary>
        public List<PushCategoryEnum>? Categories { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushDeviceRegisterRequest()
        {
        }

        #endregion
    }
}
