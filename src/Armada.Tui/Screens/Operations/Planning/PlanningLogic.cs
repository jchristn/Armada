namespace Armada.Tui.Screens.Operations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Client.Socket;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Ask;

    /// <summary>
    /// Pure helpers ported from the dashboard's <c>pages/planning/planningUtils.ts</c>, <c>lib/captains.ts</c>, and
    /// <c>ChatToolChips.applyToolEvent</c>: session and message upserts, the latest assistant reply, dispatch draft
    /// seeding, planning eligibility, and tool event folding. Thread-safe (stateless).
    /// </summary>
    public static class PlanningLogic
    {
        #region Public-Methods

        /// <summary>
        /// Insert or replace a session, newest update first.
        /// </summary>
        /// <param name="sessions">Sessions.</param>
        /// <param name="session">Session.</param>
        /// <returns>New list.</returns>
        public static List<PlanningSession> UpsertSession(IEnumerable<PlanningSession> sessions, PlanningSession session)
        {
            List<PlanningSession> next = sessions.ToList();
            int idx = next.FindIndex(s => s.Id == session.Id);
            if (idx >= 0) next[idx] = session;
            else next.Insert(0, session);
            return next.OrderByDescending(s => s.LastUpdateUtc).ToList();
        }

        /// <summary>
        /// Insert or replace a message, ordered by sequence.
        /// </summary>
        /// <param name="messages">Messages.</param>
        /// <param name="message">Message.</param>
        /// <returns>New list.</returns>
        public static List<PlanningSessionMessage> UpsertMessage(IEnumerable<PlanningSessionMessage> messages, PlanningSessionMessage message)
        {
            List<PlanningSessionMessage> next = messages.ToList();
            int idx = next.FindIndex(m => m.Id == message.Id);
            if (idx >= 0) next[idx] = message;
            else next.Add(message);
            return next.OrderBy(m => m.Sequence).ToList();
        }

        /// <summary>
        /// True for an assistant role.
        /// </summary>
        /// <param name="message">Message.</param>
        /// <returns>True for assistant replies.</returns>
        public static bool IsAssistant(PlanningSessionMessage message)
        {
            return message != null && String.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The latest assistant reply with content, or null.
        /// </summary>
        /// <param name="messages">Messages.</param>
        /// <returns>Message or null.</returns>
        public static PlanningSessionMessage? LatestAssistant(IEnumerable<PlanningSessionMessage> messages)
        {
            return messages.Reverse().FirstOrDefault(m => IsAssistant(m) && !String.IsNullOrWhiteSpace(m.Content));
        }

        /// <summary>
        /// The dashboard's canCaptainStartPlanning: a supported runtime that is Idle (or Available).
        /// </summary>
        /// <param name="captain">Captain, or null.</param>
        /// <returns>True when the captain can start a session.</returns>
        public static bool CanStartPlanning(Captain? captain)
        {
            if (captain == null || !captain.SupportsPlanningSessions) return false;
            return captain.State == CaptainStateEnum.Idle;
        }

        /// <summary>
        /// The dashboard's resolveDispatchSeedUpdate: the draft to apply for the selected message, or null when the
        /// current draft should be left alone.
        /// </summary>
        /// <param name="sessionId">Session id.</param>
        /// <param name="sessionTitle">Session title.</param>
        /// <param name="message">Selected message, or null.</param>
        /// <param name="currentTitle">Current draft title.</param>
        /// <param name="currentDescription">Current draft description.</param>
        /// <param name="previous">Previous seed, or null.</param>
        /// <returns>New seed or null.</returns>
        public static PlanningDispatchSeed? ResolveSeed(string sessionId, string? sessionTitle, PlanningSessionMessage? message, string currentTitle, string currentDescription, PlanningDispatchSeed? previous)
        {
            if (message == null || String.IsNullOrWhiteSpace(message.Content)) return null;
            string key = sessionId + ":" + message.Id;
            if (previous != null && previous.Source == "summary" && previous.Key == key) return null;
            string content = message.Content.Trim();
            string seedTitle = !String.IsNullOrWhiteSpace(sessionTitle) ? sessionTitle!.Trim() : (content.Length > 80 ? content.Substring(0, 80) : content);
            if (previous == null || previous.Key != key) return new PlanningDispatchSeed(key, seedTitle, content, "auto");
            string title = String.IsNullOrWhiteSpace(currentTitle) || currentTitle == previous.Title ? seedTitle : currentTitle;
            string description = String.IsNullOrWhiteSpace(currentDescription) || currentDescription == previous.Description ? content : currentDescription;
            return new PlanningDispatchSeed(key, title, description, "auto");
        }

        /// <summary>
        /// Fold a <c>planning-session.tool</c> event into a message's tool chips (ChatToolChips.applyToolEvent).
        /// </summary>
        /// <param name="existing">Existing chips, or null.</param>
        /// <param name="e">Event.</param>
        /// <returns>New list.</returns>
        public static List<AskToolChip> ApplyTool(List<AskToolChip>? existing, PlanningSessionEvent e)
        {
            List<AskToolChip> tools = existing != null ? existing.ToList() : new List<AskToolChip>();
            if (e == null || String.IsNullOrEmpty(e.Id)) return tools;
            int idx = tools.FindIndex(t => t.Id == e.Id);
            if (e.Phase == ToolCallPhaseEnum.Started)
            {
                if (idx < 0)
                {
                    AskToolChip chip = new AskToolChip();
                    chip.Id = e.Id!;
                    chip.Name = e.Name ?? "tool";
                    chip.Status = AskToolChipStatusEnum.Running;
                    chip.Arguments = AskEventParser.NodeText(e.Arguments);
                    tools.Add(chip);
                }
            }
            else if (e.Phase == ToolCallPhaseEnum.Completed)
            {
                AskToolChip? prior = idx >= 0 ? tools[idx] : null;
                AskToolChip done = new AskToolChip();
                done.Id = e.Id!;
                done.Name = e.Name ?? prior?.Name ?? "tool";
                done.Status = e.Ok == false ? AskToolChipStatusEnum.Failed : AskToolChipStatusEnum.Success;
                done.Arguments = prior?.Arguments;
                done.Result = AskEventParser.NodeText(e.Result);
                done.ElapsedMs = e.ElapsedMs;
                if (idx >= 0) tools[idx] = done;
                else tools.Add(done);
            }

            return tools;
        }

        /// <summary>
        /// Format a millisecond value like the dashboard's metrics bar.
        /// </summary>
        /// <param name="ms">Milliseconds, or null.</param>
        /// <returns>Text.</returns>
        public static string Ms(double? ms)
        {
            if (ms == null) return "-";
            if (ms.Value >= 1000) return (ms.Value / 1000).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "s";
            return Math.Round(ms.Value).ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms";
        }

        #endregion
    }
}
