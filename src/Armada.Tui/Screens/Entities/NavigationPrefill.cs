namespace Armada.Tui.Screens.Entities
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// The TUI's version of the dashboard's router <c>location.state</c> prefill: a screen stores a payload for a target
    /// path (for example a check-run request for <c>/delivery?tab=checks</c>) right before navigating, and the target
    /// screen takes it once when it opens. Payloads are kept per <see cref="TuiContext"/> and consumed on read. Use on
    /// the UI loop thread.
    /// </summary>
    public static class NavigationPrefill
    {
        #region Private-Members

        private static readonly ConditionalWeakTable<TuiContext, Dictionary<string, object>> _Store = new ConditionalWeakTable<TuiContext, Dictionary<string, object>>();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Store a payload for a slot (normally the target screen name) and navigate.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="slot">Slot key (for example "ChecksScreen.run").</param>
        /// <param name="payload">Payload.</param>
        /// <param name="path">Path to open, or null to only store.</param>
        public static void Set(TuiContext context, string slot, object payload, string? path = null)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (String.IsNullOrEmpty(slot)) throw new ArgumentNullException(nameof(slot));
            Dictionary<string, object> map = _Store.GetOrCreateValue(context);
            map[slot] = payload ?? throw new ArgumentNullException(nameof(payload));
            if (!String.IsNullOrEmpty(path)) context.Navigate(path!);
        }

        /// <summary>
        /// Take (and remove) a payload.
        /// </summary>
        /// <typeparam name="T">Payload type.</typeparam>
        /// <param name="context">Context.</param>
        /// <param name="slot">Slot key.</param>
        /// <returns>The payload, or null when none of that type is stored.</returns>
        public static T? Take<T>(TuiContext context, string slot) where T : class
        {
            if (context == null) return null;
            if (!_Store.TryGetValue(context, out Dictionary<string, object>? map)) return null;
            if (!map.TryGetValue(slot, out object? value)) return null;
            map.Remove(slot);
            return value as T;
        }

        /// <summary>
        /// True when a payload is waiting in a slot.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="slot">Slot key.</param>
        /// <returns>True when present.</returns>
        public static bool Has(TuiContext context, string slot)
        {
            return context != null && _Store.TryGetValue(context, out Dictionary<string, object>? map) && map.ContainsKey(slot);
        }

        #endregion
    }
}
