namespace Armada.Core.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Enums;

    /// <summary>
    /// Settings for push notifications to the Armada mobile apps, delivered through the Expo Push Service (which relays
    /// to APNs and FCM). Delivery is outbound from the Admiral, so it works behind Armada.Proxy too. All values apply
    /// live.
    /// </summary>
    public class PushSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether pushes are sent. Default true; with no registered devices nothing is sent.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Optional Expo access token, sent as a bearer token to the Expo Push Service when the Expo project has
        /// enhanced push security enabled. A secret: GET /api/v1/settings returns it redacted, and a redacted value in
        /// PUT keeps the stored token.
        /// </summary>
        public string? ExpoAccessToken { get; set; } = null;

        /// <summary>
        /// Categories enabled on a newly registered device when the app does not choose. Default: every category.
        /// Never null; setting null restores the default.
        /// </summary>
        public List<PushCategoryEnum> Categories
        {
            get => _Categories;
            set => _Categories = value == null ? AllCategories() : value.Distinct().ToList();
        }

        /// <summary>
        /// Maximum pushes per user per minute (across the user's devices); further pushes in the minute are dropped and
        /// logged. Default 20, clamped to 1..600.
        /// </summary>
        public int MaxPerUserPerMinute
        {
            get => _MaxPerUserPerMinute;
            set => _MaxPerUserPerMinute = value < 1 ? 1 : (value > 600 ? 600 : value);
        }

        /// <summary>
        /// Seconds within which a repeat push about the same item (same user, kind, and entity) is suppressed. Default
        /// 300, clamped to 0..86400 (0 disables deduplication).
        /// </summary>
        public int DedupeWindowSeconds
        {
            get => _DedupeWindowSeconds;
            set => _DedupeWindowSeconds = value < 0 ? 0 : (value > 86400 ? 86400 : value);
        }

        #endregion

        #region Private-Members

        private List<PushCategoryEnum> _Categories = AllCategories();
        private int _MaxPerUserPerMinute = 20;
        private int _DedupeWindowSeconds = 300;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with defaults.
        /// </summary>
        public PushSettings()
        {
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Every category.
        /// </summary>
        /// <returns>A new list of every category.</returns>
        public static List<PushCategoryEnum> AllCategories()
        {
            return Enum.GetValues(typeof(PushCategoryEnum)).Cast<PushCategoryEnum>().ToList();
        }

        #endregion
    }
}
