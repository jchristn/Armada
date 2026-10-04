namespace Armada.Tui.Services
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The single queue of everything waiting on the user (Ask proposals, mission reviews, deployment approvals, failed
    /// landings, stalled captains). It drives the header and status bar counts, attention escalation for new items, and
    /// the Approvals center. Sources: the Ask controller (proposal events and thread loads) and
    /// <see cref="Armada.Tui.Approvals.ApprovalSources"/> (inbox polling and entity-change events). Call on the UI loop thread.
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
            Upsert(item, true);
        }

        /// <summary>
        /// Add or replace an item, optionally without raising <see cref="Arrived"/> for a new one (items the user is
        /// already looking at, or the first sync after sign-in).
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="notify">Raise <see cref="Arrived"/> when the item is new.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="item"/> is null.</exception>
        public void Upsert(ApprovalItem item, bool notify)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            bool isNew = !_Items.TryGetValue(item.Key, out ApprovalItem? previous);
            if (previous != null) item.CreatedUtc = previous.CreatedUtc;
            _Items[item.Key] = item;
            if (isNew && notify) Arrived?.Invoke(this, item);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Replace every item of the given kinds with a new set (a full inbox poll): new items are added, items no
        /// longer present are removed, and existing ones are updated.
        /// </summary>
        /// <param name="kinds">Kinds owned by the source.</param>
        /// <param name="items">Current items.</param>
        /// <param name="notify">Raise <see cref="Arrived"/> for new items.</param>
        public void Sync(IEnumerable<ApprovalKindEnum> kinds, IEnumerable<ApprovalItem> items, bool notify)
        {
            HashSet<ApprovalKindEnum> owned = new HashSet<ApprovalKindEnum>(kinds);
            List<ApprovalItem> incoming = items.Where(i => i != null && owned.Contains(i.Kind)).ToList();
            HashSet<string> keep = new HashSet<string>(incoming.Select(i => i.Key), StringComparer.Ordinal);
            bool changed = false;
            foreach (string key in _Items.Where(p => owned.Contains(p.Value.Kind) && !keep.Contains(p.Key)).Select(p => p.Key).ToList())
            {
                _Items.Remove(key);
                changed = true;
            }

            List<ApprovalItem> arrived = new List<ApprovalItem>();
            foreach (ApprovalItem item in incoming)
            {
                if (_Items.TryGetValue(item.Key, out ApprovalItem? previous)) item.CreatedUtc = previous.CreatedUtc;
                else arrived.Add(item);
                _Items[item.Key] = item;
                changed = true;
            }

            if (notify) foreach (ApprovalItem item in arrived) Arrived?.Invoke(this, item);
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Find an item.
        /// </summary>
        /// <param name="kind">Kind.</param>
        /// <param name="entityId">Entity id.</param>
        /// <returns>Item or null.</returns>
        public ApprovalItem? Find(ApprovalKindEnum kind, string entityId)
        {
            return _Items.TryGetValue(kind + ":" + entityId, out ApprovalItem? item) ? item : null;
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
