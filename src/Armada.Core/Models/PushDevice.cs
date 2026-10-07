namespace Armada.Core.Models
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;

    /// <summary>
    /// A mobile app installation registered to receive push notifications for one user. Devices are keyed by their
    /// Expo push token: registering a token that is already known refreshes that device (and moves it to the caller
    /// when another user registered it before). API responses mask <see cref="ExpoPushToken"/>.
    /// </summary>
    public class PushDevice
    {
        #region Public-Members

        /// <summary>
        /// Unique identifier (pdv_ prefix).
        /// </summary>
        public string Id
        {
            get => _Id;
            set => _Id = String.IsNullOrEmpty(value) ? throw new ArgumentNullException(nameof(Id)) : value;
        }

        /// <summary>
        /// Tenant of the owning user.
        /// </summary>
        public string? TenantId { get; set; } = null;

        /// <summary>
        /// Owning user.
        /// </summary>
        public string? UserId { get; set; } = null;

        /// <summary>
        /// Platform.
        /// </summary>
        public PushPlatformEnum Platform { get; set; } = PushPlatformEnum.Ios;

        /// <summary>
        /// Expo push token (ExponentPushToken[...] or ExpoPushToken[...]). Masked in API responses.
        /// </summary>
        public string ExpoPushToken
        {
            get => _ExpoPushToken;
            set => _ExpoPushToken = value ?? String.Empty;
        }

        /// <summary>
        /// Human-readable device name (for example "Joel's iPhone").
        /// </summary>
        public string? DeviceName { get; set; } = null;

        /// <summary>
        /// Version of the mobile app.
        /// </summary>
        public string? AppVersion { get; set; } = null;

        /// <summary>
        /// Device locale (BCP 47, for example en-US).
        /// </summary>
        public string? Locale { get; set; } = null;

        /// <summary>
        /// Categories this device receives. An empty list mutes the device. Never null.
        /// </summary>
        public List<PushCategoryEnum> Categories
        {
            get => _Categories;
            set => _Categories = value == null ? new List<PushCategoryEnum>() : value.Distinct().ToList();
        }

        /// <summary>
        /// Whether the device receives pushes. A device is deactivated when the Expo Push Service reports it as not
        /// registered; registering it again reactivates it.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// UTC creation time.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time the app last registered (refreshed) the device.
        /// </summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC last update time.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = Constants.IdGenerator.GenerateKSortable(Constants.PushDeviceIdPrefix, 24);
        private string _ExpoPushToken = String.Empty;
        private List<PushCategoryEnum> _Categories = new List<PushCategoryEnum>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        public PushDevice()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A copy of the device whose <see cref="ExpoPushToken"/> is masked (prefix and the last four characters of
        /// the token kept), for API responses.
        /// </summary>
        /// <returns>The masked copy.</returns>
        public PushDevice ToMasked()
        {
            PushDevice copy = new PushDevice();
            copy.Id = Id;
            copy.TenantId = TenantId;
            copy.UserId = UserId;
            copy.Platform = Platform;
            copy.ExpoPushToken = MaskToken(ExpoPushToken);
            copy.DeviceName = DeviceName;
            copy.AppVersion = AppVersion;
            copy.Locale = Locale;
            copy.Categories = new List<PushCategoryEnum>(Categories);
            copy.Active = Active;
            copy.CreatedUtc = CreatedUtc;
            copy.LastSeenUtc = LastSeenUtc;
            copy.LastUpdateUtc = LastUpdateUtc;
            return copy;
        }

        /// <summary>
        /// Mask an Expo push token: keeps the ExponentPushToken[ (or ExpoPushToken[) prefix and the last four
        /// characters inside the brackets.
        /// </summary>
        /// <param name="token">Token.</param>
        /// <returns>The masked token.</returns>
        public static string MaskToken(string? token)
        {
            if (String.IsNullOrEmpty(token)) return String.Empty;
            int open = token!.IndexOf('[');
            int close = token.LastIndexOf(']');
            if (open < 0 || close <= open) return "****";
            string inner = token.Substring(open + 1, close - open - 1);
            string tail = inner.Length > 8 ? inner.Substring(inner.Length - 4) : String.Empty;
            return token.Substring(0, open + 1) + "****" + tail + "]";
        }

        #endregion
    }
}
