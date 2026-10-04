namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The single queue of everything waiting on the user (Ask proposals, mission reviews, deployment approvals, failed
    /// landings, stalled captains). This skeleton drives the header count; the sources (socket events, inbox polling)
    /// and the Approvals center are wired in later waves (W2.4, W3.3). Call on the UI loop thread.
    /// </summary>
    public class ApprovalService
    {
        #region Public-Members

        /// <summary>
        /// Pending item count.
        /// </summary>
        public int Count
        {
            get { return _Items.Count; }
        }

        /// <summary>
        /// Items by urgency, then age. Never null.
        /// </summary>
        public IReadOnlyList<ApprovalItem> Items
        {
            get { return _Items.Values.OrderByDescending(i => i.Urgency).ThenBy(i => i.CreatedUtc).ToList(); }
        }

        /// <summary>
        /// Raised after the queue changes.
        /// </summary>
        public event EventHandler? Changed;

        /// <summary>
        /// Raised when a new item arrives (attention escalation).
        /// </summary>
        public event EventHandler<ApprovalItem>? Arrived;

        #endregion

        #region Private-Members

        private readonly Dictionary<string, ApprovalItem> _Items = new Dictionary<string, ApprovalItem>(StringComparer.Ordinal);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Add or replace an item.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is null.</exception>
        public void Upsert(ApprovalItem item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            bool isNew = !_Items.ContainsKey(item.Key);
            _Items[item.Key] = item;
            if (isNew) Arrived?.Invoke(this, item);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Remove an item (decided, expired, or resolved elsewhere).
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="entityId">Entity id.</param>
        /// <returns>True when removed.</returns>
        public bool Remove(ApprovalKindEnum kind, string entityId)
        {
            bool removed = _Items.Remove(kind + ":" + entityId);
            if (removed) Changed?.Invoke(this, EventArgs.Empty);
            return removed;
        }

        /// <summary>
        /// Remove every item.
        /// </summary>
        public void Clear()
        {
            if (_Items.Count == 0) return;
            _Items.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}
