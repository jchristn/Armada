namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// The dashboard's backlog helpers (<c>components/backlog/backlogUtils.ts</c> and <c>lib/duplicates.ts</c>): the
    /// option lists, group membership, priority weight, list splitting, suggested-playbook text, the planning,
    /// dispatch, and release-notes prompts, and the duplicate payload. Thread-safe (stateless).
    /// </summary>
    public static class BacklogLogic
    {
        #region Public-Members

        /// <summary>
        /// Group keys in pill order.
        /// </summary>
        public static readonly string[] GroupKeys = new string[] { "all", "inbox", "planning", "dispatch", "blocked" };

        /// <summary>
        /// Group labels (English) in pill order.
        /// </summary>
        public static readonly string[] GroupLabels = new string[] { "All", "Inbox", "Ready For Planning", "Ready For Dispatch", "Blocked" };

        /// <summary>
        /// Lifecycle statuses.
        /// </summary>
        public static readonly string[] Statuses = new string[] { "Draft", "Scoped", "Planned", "InProgress", "Released", "Deployed", "Completed", "Blocked", "Cancelled" };

        /// <summary>
        /// Kinds.
        /// </summary>
        public static readonly string[] Kinds = new string[] { "Feature", "Bug", "Refactor", "Research", "Chore", "Initiative" };

        /// <summary>
        /// Priorities.
        /// </summary>
        public static readonly string[] Priorities = new string[] { "P0", "P1", "P2", "P3" };

        /// <summary>
        /// Backlog states.
        /// </summary>
        public static readonly string[] BacklogStates = new string[] { "Inbox", "Triaged", "Refining", "ReadyForPlanning", "ReadyForDispatch", "Dispatched" };

        /// <summary>
        /// Effort sizes.
        /// </summary>
        public static readonly string[] Efforts = new string[] { "XS", "S", "M", "L", "XL" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The group a backlog item belongs to (the dashboard's <c>getBacklogGroup</c>).
        /// </summary>
        /// <param name="o">Item.</param>
        /// <returns>Group key.</returns>
        public static string Group(Objective o)
        {
            if (o.Status == ObjectiveStatusEnum.Blocked || (o.BlockedByObjectiveIds?.Count ?? 0) > 0) return "blocked";
            if (o.BacklogState == ObjectiveBacklogStateEnum.ReadyForDispatch || o.BacklogState == ObjectiveBacklogStateEnum.Dispatched) return "dispatch";
            if (o.BacklogState == ObjectiveBacklogStateEnum.ReadyForPlanning) return "planning";
            if (o.BacklogState == ObjectiveBacklogStateEnum.Inbox || o.BacklogState == ObjectiveBacklogStateEnum.Triaged || o.BacklogState == ObjectiveBacklogStateEnum.Refining) return "inbox";
            return "all";
        }

        /// <summary>
        /// Priority weight (P0 is 0).
        /// </summary>
        /// <param name="priority">Priority.</param>
        /// <returns>Weight.</returns>
        public static int PriorityWeight(ObjectivePriorityEnum priority)
        {
            switch (priority)
            {
                case ObjectivePriorityEnum.P0: return 0;
                case ObjectivePriorityEnum.P1: return 1;
                case ObjectivePriorityEnum.P2: return 2;
                case ObjectivePriorityEnum.P3: return 3;
                default: return 99;
            }
        }

        /// <summary>
        /// Order items like the dashboard's sort select (rank, priority, updated, due).
        /// </summary>
        /// <param name="items">Items.</param>
        /// <param name="sortBy">Sort key.</param>
        /// <returns>Ordered items.</returns>
        public static List<Objective> Order(IEnumerable<Objective> items, string sortBy)
        {
            List<Objective> list = items.ToList();
            Comparison<Objective> cmp;
            switch (sortBy)
            {
                case "priority":
                    cmp = (l, r) =>
                    {
                        int d = PriorityWeight(l.Priority) - PriorityWeight(r.Priority);
                        return d != 0 ? d : l.Rank.CompareTo(r.Rank);
                    };
                    break;
                case "due":
                    cmp = (l, r) =>
                    {
                        DateTime ld = l.DueUtc ?? DateTime.MaxValue;
                        DateTime rd = r.DueUtc ?? DateTime.MaxValue;
                        int d = ld.CompareTo(rd);
                        return d != 0 ? d : l.Rank.CompareTo(r.Rank);
                    };
                    break;
                case "updated":
                    cmp = (l, r) =>
                    {
                        int d = r.LastUpdateUtc.CompareTo(l.LastUpdateUtc);
                        return d != 0 ? d : l.Rank.CompareTo(r.Rank);
                    };
                    break;
                default:
                    cmp = (l, r) =>
                    {
                        int d = l.Rank.CompareTo(r.Rank);
                        return d != 0 ? d : PriorityWeight(l.Priority) - PriorityWeight(r.Priority);
                    };
                    break;
            }

            List<KeyValuePair<int, Objective>> indexed = list.Select((o, i) => new KeyValuePair<int, Objective>(i, o)).ToList();
            indexed.Sort((a, b) =>
            {
                int c = cmp(a.Value, b.Value);
                return c != 0 ? c : a.Key.CompareTo(b.Key);
            });
            return indexed.Select(k => k.Value).ToList();
        }

        /// <summary>
        /// Split on newlines and commas, trim, drop empties (the dashboard's <c>splitList</c>).
        /// </summary>
        /// <param name="value">Text.</param>
        /// <returns>Items.</returns>
        public static List<string> SplitList(string? value)
        {
            if (String.IsNullOrEmpty(value)) return new List<string>();
            return value!.Replace("\r\n", "\n").Split(new char[] { '\n', ',' }).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }

        /// <summary>
        /// Split on newlines only (tags keep commas).
        /// </summary>
        /// <param name="value">Text.</param>
        /// <returns>Lines.</returns>
        public static List<string> SplitLines(string? value)
        {
            if (String.IsNullOrEmpty(value)) return new List<string>();
            return value!.Replace("\r\n", "\n").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }

        /// <summary>
        /// Join with newlines.
        /// </summary>
        /// <param name="values">Values.</param>
        /// <returns>Text.</returns>
        public static string JoinList(IEnumerable<string>? values)
        {
            return String.Join("\n", values ?? Enumerable.Empty<string>());
        }

        /// <summary>
        /// Suggested playbooks as <c>id:mode</c> lines.
        /// </summary>
        /// <param name="values">Playbooks.</param>
        /// <returns>Text.</returns>
        public static string JoinPlaybooks(IEnumerable<SelectedPlaybook>? values)
        {
            return String.Join("\n", (values ?? Enumerable.Empty<SelectedPlaybook>()).Select(p => p.PlaybookId + ":" + p.DeliveryMode));
        }

        /// <summary>
        /// Parse <c>id:mode</c> lines (mode defaults to InlineFullContent).
        /// </summary>
        /// <param name="value">Text.</param>
        /// <returns>Playbooks.</returns>
        public static List<SelectedPlaybook> ParsePlaybooks(string? value)
        {
            List<SelectedPlaybook> list = new List<SelectedPlaybook>();
            foreach (string line in SplitList(value))
            {
                string[] parts = line.Split(new char[] { ':' }, 2);
                string id = parts[0].Trim();
                if (id.Length == 0) continue;
                SelectedPlaybook p = new SelectedPlaybook();
                p.PlaybookId = id;
                string mode = parts.Length > 1 ? parts[1].Trim() : "";
                p.DeliveryMode = Enum.TryParse(mode, true, out PlaybookDeliveryModeEnum m) ? m : PlaybookDeliveryModeEnum.InlineFullContent;
                list.Add(p);
            }

            return list;
        }

        /// <summary>
        /// Parse tag lines (<c>key:value</c>, <c>key=value</c>, or a bare key) into the stored form (<c>key:value</c>).
        /// </summary>
        /// <param name="value">Text, one tag per line.</param>
        /// <returns>Tags.</returns>
        public static List<string> ParseTags(string? value)
        {
            List<string> tags = new List<string>();
            foreach (string line in SplitLines(value))
            {
                int colon = line.IndexOf(':');
                int eq = line.IndexOf('=');
                int sep = new int[] { colon, eq }.Where(i => i > 0).DefaultIfEmpty(-1).Min();
                if (sep < 1)
                {
                    tags.Add(line);
                    continue;
                }

                string key = line.Substring(0, sep).Trim();
                string val = line.Substring(sep + 1).Trim();
                if (key.Length == 0 && val.Length == 0) continue;
                tags.Add(val.Length == 0 ? key : key + ":" + val);
            }

            return tags;
        }

        /// <summary>
        /// The dashboard's <c>buildObjectivePlanningPrompt</c>.
        /// </summary>
        /// <param name="o">Item.</param>
        /// <returns>Prompt.</returns>
        public static string PlanningPrompt(Objective o)
        {
            List<string> lines = new List<string>();
            lines.Add("Backlog Item: " + o.Title);
            lines.Add("Kind: " + o.Kind);
            lines.Add("Priority: " + o.Priority);
            lines.Add("Backlog State: " + o.BacklogState);
            if (!String.IsNullOrEmpty(o.Description)) { lines.Add(""); lines.Add("Context"); lines.Add(o.Description!); }
            if (!String.IsNullOrEmpty(o.RefinementSummary)) { lines.Add(""); lines.Add("Refinement Summary"); lines.Add(o.RefinementSummary!); }
            Bullets(lines, "Acceptance Criteria", o.AcceptanceCriteria);
            Bullets(lines, "Non-Goals", o.NonGoals);
            Bullets(lines, "Rollout Constraints", o.RolloutConstraints);
            lines.Add("");
            lines.Add("Turn this refined backlog item into a practical implementation plan, call out risks, and identify the best dispatch shape.");
            return String.Join("\n", lines);
        }

        /// <summary>
        /// The dashboard's <c>buildObjectiveDispatchPrompt</c>.
        /// </summary>
        /// <param name="o">Item.</param>
        /// <returns>Prompt.</returns>
        public static string DispatchPrompt(Objective o)
        {
            List<string> lines = new List<string>();
            lines.Add("Implement backlog item: " + o.Title);
            if (!String.IsNullOrEmpty(o.Description)) { lines.Add(""); lines.Add(o.Description!); }
            if (!String.IsNullOrEmpty(o.RefinementSummary)) { lines.Add(""); lines.Add("Refinement Summary"); lines.Add(o.RefinementSummary!); }
            Bullets(lines, "Acceptance Criteria", o.AcceptanceCriteria);
            Bullets(lines, "Non-Goals", o.NonGoals);
            Bullets(lines, "Constraints", o.RolloutConstraints);
            return String.Join("\n", lines);
        }

        /// <summary>
        /// The dashboard's <c>buildObjectiveReleaseNotes</c>.
        /// </summary>
        /// <param name="o">Item.</param>
        /// <returns>Notes.</returns>
        public static string ReleaseNotes(Objective o)
        {
            List<string> lines = new List<string>();
            lines.Add("Backlog-derived release notes for " + o.Title);
            if (!String.IsNullOrEmpty(o.Description)) { lines.Add(""); lines.Add(o.Description!); }
            if (!String.IsNullOrEmpty(o.RefinementSummary)) { lines.Add(""); lines.Add("Refinement Summary"); lines.Add(o.RefinementSummary!); }
            Bullets(lines, "Acceptance Criteria", o.AcceptanceCriteria);
            Bullets(lines, "Rollout Constraints", o.RolloutConstraints);
            Bullets(lines, "Evidence Links", o.EvidenceLinks);
            return String.Join("\n", lines);
        }

        /// <summary>
        /// The dashboard's <c>buildObjectiveDuplicatePayload</c>.
        /// </summary>
        /// <param name="o">Item.</param>
        /// <returns>Create request.</returns>
        public static ObjectiveUpsertRequest DuplicatePayload(Objective o)
        {
            ObjectiveUpsertRequest r = new ObjectiveUpsertRequest();
            string trimmed = (o.Title ?? "").Trim();
            r.Title = trimmed.Length > 0 ? trimmed + " (Copy)" : "Copy";
            r.Description = o.Description;
            r.Status = ObjectiveStatusEnum.Draft;
            r.Kind = o.Kind;
            r.Category = o.Category;
            r.Priority = o.Priority;
            r.Rank = null;
            r.BacklogState = ObjectiveBacklogStateEnum.Inbox;
            r.Effort = o.Effort;
            r.Owner = o.Owner;
            r.TargetVersion = o.TargetVersion;
            r.DueUtc = null;
            r.ParentObjectiveId = null;
            r.BlockedByObjectiveIds = new List<string>();
            r.RefinementSummary = null;
            r.SuggestedPipelineId = o.SuggestedPipelineId;
            r.SuggestedPlaybooks = (o.SuggestedPlaybooks ?? new List<SelectedPlaybook>()).ToList();
            r.Tags = (o.Tags ?? new List<string>()).ToList();
            r.AcceptanceCriteria = (o.AcceptanceCriteria ?? new List<string>()).ToList();
            r.NonGoals = (o.NonGoals ?? new List<string>()).ToList();
            r.RolloutConstraints = (o.RolloutConstraints ?? new List<string>()).ToList();
            r.EvidenceLinks = (o.EvidenceLinks ?? new List<string>()).ToList();
            r.FleetIds = (o.FleetIds ?? new List<string>()).ToList();
            r.VesselIds = (o.VesselIds ?? new List<string>()).ToList();
            r.PlanningSessionIds = new List<string>();
            r.RefinementSessionIds = new List<string>();
            r.VoyageIds = new List<string>();
            r.MissionIds = new List<string>();
            r.CheckRunIds = new List<string>();
            r.ReleaseIds = new List<string>();
            r.DeploymentIds = new List<string>();
            r.IncidentIds = new List<string>();
            return r;
        }

        /// <summary>
        /// Replace the first id of a list, keeping the rest (the dashboard's <c>replacePrimaryLinkedId</c>).
        /// </summary>
        /// <param name="current">Current ids.</param>
        /// <param name="nextId">New primary id (empty clears the list).</param>
        /// <returns>New list.</returns>
        public static List<string> ReplacePrimary(List<string> current, string? nextId)
        {
            if (String.IsNullOrEmpty(nextId)) return new List<string>();
            List<string> rest = (current ?? new List<string>()).Where((id, i) => i > 0 && id != nextId).ToList();
            List<string> result = new List<string> { nextId! };
            result.AddRange(rest);
            return result;
        }

        #endregion

        #region Private-Methods

        private static void Bullets(List<string> lines, string heading, List<string>? items)
        {
            if (items == null || items.Count == 0) return;
            lines.Add("");
            lines.Add(heading);
            foreach (string item in items) lines.Add("- " + item);
        }

        #endregion
    }
}
