namespace Armada.Tui.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// Thread list rules shared with the dashboard (<c>lib/askThreads.ts</c>): ordering, filter matching, folding an
    /// <c>ask.thread</c> update into the visible list, and the live working and replying indicators. Thread-safe
    /// (stateless).
    /// </summary>
    public static class AskThreadListLogic
    {
        #region Public-Methods

        /// <summary>
        /// Pinned first, then the most recent message first (the server's order).
        /// </summary>
        /// <param name="threads">Threads.</param>
        /// <returns>New sorted list.</returns>
        public static List<AskThread> Sort(IEnumerable<AskThread> threads)
        {
            return threads
                .OrderBy(t => t.Pinned ? 0 : 1)
                .ThenByDescending(t => t.LastMessageUtc ?? t.LastUpdateUtc)
                .ToList();
        }

        /// <summary>
        /// True when a thread belongs in the list under a filter (archived hidden unless shown; search over title and
        /// summary, case-insensitive).
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="search">Search text.</param>
        /// <param name="includeArchived">Show archived.</param>
        /// <returns>True when it matches.</returns>
        public static bool Matches(AskThread thread, string search, bool includeArchived)
        {
            if (thread.Archived && !includeArchived) return false;
            string q = (search ?? "").Trim();
            if (q.Length == 0) return true;
            return (thread.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || (thread.SummaryText ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Fold an updated thread into the visible list: replace the row, insert a new thread that matches the filter,
        /// drop one that no longer matches (archived), and keep the open thread's unread count at zero.
        /// </summary>
        /// <param name="threads">Visible list.</param>
        /// <param name="thread">Updated thread.</param>
        /// <param name="search">Search text.</param>
        /// <param name="includeArchived">Show archived.</param>
        /// <param name="openThreadId">Open thread id, or null.</param>
        /// <returns>New list.</returns>
        public static List<AskThread> ApplyUpdate(IReadOnlyList<AskThread> threads, AskThread thread, string search, bool includeArchived, string? openThreadId)
        {
            if (thread.Id == openThreadId) thread.UnreadCount = 0;
            int idx = -1;
            for (int i = 0; i < threads.Count; i++)
            {
                if (threads[i].Id == thread.Id)
                {
                    idx = i;
                    break;
                }
            }

            if (idx < 0)
            {
                if (!Matches(thread, search, includeArchived)) return threads.ToList();
                List<AskThread> added = threads.ToList();
                added.Add(thread);
                return Sort(added);
            }

            if (!Matches(thread, "", includeArchived)) return threads.Where(t => t.Id != thread.Id).ToList();
            List<AskThread> copy = threads.ToList();
            copy[idx] = thread;
            return Sort(copy);
        }

        /// <summary>
        /// Track live working and replying state from an event. Returns true when the map changed.
        /// </summary>
        /// <param name="map">Activity by thread id.</param>
        /// <param name="e">Event.</param>
        /// <returns>True when changed.</returns>
        public static bool ApplyActivity(Dictionary<string, AskThreadActivity> map, AskEvent e)
        {
            if (!map.TryGetValue(e.ThreadId, out AskThreadActivity? current))
            {
                current = new AskThreadActivity();
                map[e.ThreadId] = current;
            }

            if (e.Type == "ask.work")
            {
                bool active = e.Snapshot != null ? AskWorkLogic.IsActive(e.Snapshot) : AskWorkLogic.IsActive(e.TrackedWork);
                if (current.Work.TryGetValue(e.TrackedWorkId, out bool prior) && prior == active) return false;
                current.Work[e.TrackedWorkId] = active;
                return true;
            }

            if (e.Type == "ask.turn")
            {
                bool replying = e.State == "started";
                if (current.Replying == replying) return false;
                current.Replying = replying;
                return true;
            }

            if (e.Type == "ask.chunk" || e.Type == "ask.tool" || e.Type == "ask.thinking")
            {
                if (current.Replying) return false;
                current.Replying = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Whether a thread has active tracked work: live events win, otherwise the server's active count.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="map">Activity.</param>
        /// <returns>True when working.</returns>
        public static bool IsWorking(AskThread thread, IReadOnlyDictionary<string, AskThreadActivity> map)
        {
            if (map.TryGetValue(thread.Id, out AskThreadActivity? live) && live.Work.Count > 0) return live.Work.Values.Any(v => v);
            return thread.ActiveWorkCount > 0;
        }

        /// <summary>
        /// Whether a captain turn is running in a thread: live events win, otherwise the server's active turn.
        /// </summary>
        /// <param name="thread">Thread.</param>
        /// <param name="map">Activity.</param>
        /// <returns>True when replying.</returns>
        public static bool IsReplying(AskThread thread, IReadOnlyDictionary<string, AskThreadActivity> map)
        {
            if (map.TryGetValue(thread.Id, out AskThreadActivity? live)) return live.Replying;
            return !String.IsNullOrEmpty(thread.ActiveTurnId);
        }

        #endregion
    }
}
