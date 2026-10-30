namespace Armada.Core.Services.Ask
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Armada.Core.Models;

    /// <summary>
    /// The Ask Armada black box guidance (MISSION_BLACKBOX_PLAN.md section 10.2): the "Black box" section of the Ask system
    /// prompt, the canonical question the entry points pre-fill, and the answer headings a grounded answer uses. The
    /// section is part of the built-in <c>ask.system</c> template; a customized template that lacks it gets it appended
    /// to the turn prompt, so operators who edited the template still get grounded black box answers.
    /// </summary>
    public static class AskBlackBoxGuidance
    {
        #region Public-Members

        /// <summary>
        /// Heading that marks the section in a system prompt.
        /// </summary>
        public const string Heading = "## Black box";

        /// <summary>
        /// The answer headings, in order.
        /// </summary>
        public static readonly string[] AnswerHeadings = new[] { "What happened", "Why", "My opinion", "What could have been done differently", "Next steps" };

        /// <summary>
        /// The "Black box" section of the Ask system prompt. It carries the resilience bias: for a failure in Armada's
        /// handoff and landing the captain first assesses whether Armada could have handled it, cites the entry's
        /// Resilience section and offers "Draft an Armada fix mission".
        /// </summary>
        public const string Section =
            Heading + "\n" +
            "- When the user names a failed mission or voyage, asks why something failed, or asks for its black box, call get_blackbox first (format summary, the default) with missionId, voyageId or entryId.\n" +
            "- Before anything else, assess whether Armada's own handoff and landing could have handled the failure. The summary carries ResilienceInScope, SelfHealable, SelfHealingProposal, DetectionGap and TopProposedChange; read the full Resilience section (read_blackbox_section with section Resilience) and, for the step timeline and the recoveries Armada already tried, the Handoff section. When ResilienceInScope is true the black box assumes Armada could have been more resilient: say what Armada could have done automatically, why it did not catch the problem earlier, and which Armada change (with its test) would prevent it, before you conclude it is the user's configuration or a transient. Say plainly when the triage is a suspected Armada defect (TriageSuspected).\n" +
            "- Then read only the other sections the question needs with read_blackbox_section (usually Conflict, Workspace and Analysis; Configuration for a settings question), and page evidence objects with read_blackbox_object (offset and length) instead of loading them whole. list_blackbox_objects shows what evidence exists.\n" +
            "- Answer with these headings, in this order:\n" +
            "  - What happened: the pertinent details (the mission, where it failed: phase, step, host, source file and method; how: the mechanism; status before and after; conflicting missions; the files and directories involved). Every line comes from the entry.\n" +
            "  - Why: the entry's findings with their rule ids and confidence, and the triage verdict (with BBX-PRIOR-ARMADA when the handoff and landing prior voted).\n" +
            "  - My opinion: label it as your opinion. Say whether you agree with the triage, what you think the real root cause is, whether Armada's handoff and landing could have handled it, and what evidence would change your mind.\n" +
            "  - What could have been done differently: start with what Armada should change (the Resilience section: the self-healing proposal, the detection gap and invariant, the proposed Armada changes and the regression test), then configuration, the mission brief and dependency ordering. Separate what the user can change from what Armada should change.\n" +
            "  - Next steps: the entry's ranked recovery paths by path id, Copy FIX THIS (get_blackbox with format brief), and, when the entry has a proposed Armada change, offer \"Draft an Armada fix mission\".\n" +
            "- Never invent files, commits, missions or rule ids that are not in the entry. When a fact is missing (for example an entry with CaptureMode=Sweeper has no workspace snapshot or captain log), say that it is missing instead of guessing.\n" +
            "- When get_blackbox answers NotFound (Code BlackBoxNotFound), say that the mission has no black box entry and offer to show the mission's FailureReason (mission_status) instead.\n" +
            "- Offer to save your opinion as a note on the entry with annotate_blackbox; it becomes a proposal the user approves.\n" +
            "- Follow-ups: \"Fix it\" means call run_blackbox_recovery once with the entry and the top-ranked pathId and leave dryRun unset: it becomes a proposal, and approving it runs the dry run and then confirms it. Ask before setting acknowledgeDataLoss on a path marked dataLossRisk. \"Apply the configuration change\" means apply_blackbox_recommendation with the recommendationId, the same way. \"Draft an Armada fix mission\" means propose create_mission on the vessel that holds Armada's own source with the top proposed Armada change from the Resilience section (its MissionBrief: title, change, acceptance test, code pointers at the Armada build commit) and the regression test; without such a vessel, give the user the brief. \"Compare with similar failures\" means read the entry's fingerprint trend (get_blackbox_fingerprint_trend) and similar entries (search_blackbox, list_blackbox_entries, list_blackbox_fingerprints, list_blackbox_resilience_gaps), then compare them with this one.\n";

        /// <summary>
        /// The "Black box" section as it shipped before the resilience bias (kept so an untouched built-in ask.system
        /// template is upgraded in place).
        /// </summary>
        public const string PreviousSection =
            Heading + "\n" +
            "- When the user names a failed mission or voyage, asks why something failed, or asks for its black box, call get_blackbox first (format summary, the default) with missionId, voyageId or entryId.\n" +
            "- Then read only the sections the question needs with read_blackbox_section (usually Conflict, Workspace and Analysis; Configuration for a settings question), and page evidence objects with read_blackbox_object (offset and length) instead of loading them whole. list_blackbox_objects shows what evidence exists.\n" +
            "- Answer with these headings, in this order:\n" +
            "  - What happened: the pertinent details (the mission, where it failed: phase, step, host, source file and method; how: the mechanism; status before and after; conflicting missions; the files and directories involved). Every line comes from the entry.\n" +
            "  - Why: the entry's findings with their rule ids and confidence, and the triage verdict.\n" +
            "  - My opinion: label it as your opinion. Say whether you agree with the triage, what you think the real root cause is, and what evidence would change your mind.\n" +
            "  - What could have been done differently: configuration, the mission brief, dependency ordering and Armada behavior. Separate what the user can change from what Armada should change.\n" +
            "  - Next steps: the entry's ranked recovery paths by path id, and Copy FIX THIS (get_blackbox with format brief).\n" +
            "- Never invent files, commits, missions or rule ids that are not in the entry. When a fact is missing (for example an entry with CaptureMode=Sweeper has no workspace snapshot or captain log), say that it is missing instead of guessing.\n" +
            "- When get_blackbox answers NotFound (Code BlackBoxNotFound), say that the mission has no black box entry and offer to show the mission's FailureReason (mission_status) instead.\n" +
            "- Offer to save your opinion as a note on the entry with annotate_blackbox; it becomes a proposal the user approves.\n" +
            "- Follow-ups: \"Fix it\" means call run_blackbox_recovery once with the entry and the top-ranked pathId and leave dryRun unset: it becomes a proposal, and approving it runs the dry run and then confirms it. Ask before setting acknowledgeDataLoss on a path marked dataLossRisk. \"Apply the configuration change\" means apply_blackbox_recommendation with the recommendationId, the same way. \"Draft an Armada fix mission\" means propose create_mission on the vessel that holds Armada's own source with the entry's Armada improvements and the entry's brief (get_blackbox format brief); without such a vessel, give the user the brief. \"Compare with similar failures\" means read the entry's fingerprint trend (get_blackbox_fingerprint_trend) and similar entries (search_blackbox, list_blackbox_entries, list_blackbox_fingerprints), then compare them with this one.\n";

        #endregion

        #region Public-Methods

        /// <summary>
        /// The question the black box entry points pre-fill in a new conversation.
        /// </summary>
        /// <param name="missionId">Mission identifier (msn_ prefix).</param>
        /// <returns>The question.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="missionId"/> is null or empty.</exception>
        public static string CanonicalPrompt(string missionId)
        {
            if (String.IsNullOrWhiteSpace(missionId)) throw new ArgumentNullException(nameof(missionId));
            return "Show me the pertinent details from the black box for mission " + missionId.Trim() + " and give me your opinion on why it failed and what could have been done differently";
        }

        /// <summary>
        /// The follow-ups a black box card offers after an answer (plan 10.3), in order: Fix it (the top-ranked recovery
        /// path, proposed as run_blackbox_recovery), Apply the configuration change (the first recommendation Armada can
        /// apply, proposed as apply_blackbox_recommendation), Draft an Armada fix mission and Compare with similar
        /// failures (both sent to the captain as messages). Fix it and Apply appear only when the entry has a path or an
        /// applicable recommendation.
        /// </summary>
        /// <param name="card">Card with its digest and recommendations.</param>
        /// <returns>Follow-ups.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="card"/> is null.</exception>
        public static List<AskBlackBoxFollowUp> FollowUps(AskBlackBoxCard card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            List<AskBlackBoxFollowUp> followUps = new List<AskBlackBoxFollowUp>();
            BlackBoxDigest digest = card.Digest;
            string entryId = digest.EntryId;
            string subject = !String.IsNullOrEmpty(digest.MissionId) ? "mission " + digest.MissionId : (!String.IsNullOrEmpty(digest.VoyageId) ? "voyage " + digest.VoyageId : "entry " + entryId);

            BlackBoxRecoveryPath? path = digest.RecoveryPaths.OrderBy(p => p.Rank).FirstOrDefault();
            if (path != null && !String.IsNullOrEmpty(path.Id))
            {
                AskBlackBoxFollowUp fix = new AskBlackBoxFollowUp();
                fix.Name = "fix";
                fix.Label = "Fix it";
                fix.Description = "Propose recovery path \"" + path.Title + "\" (" + path.Id + "). Approving it runs a dry run, then confirms it" + (path.DataLossRisk ? "; this path risks data loss and needs your acknowledgement." : ".");
                fix.ToolName = "run_blackbox_recovery";
                JsonObject args = new JsonObject { ["entryId"] = entryId, ["pathId"] = path.Id };
                if (path.DataLossRisk) args["acknowledgeDataLoss"] = true;
                fix.ArgumentsText = args.ToJsonString();
                followUps.Add(fix);
            }

            AskBlackBoxRecommendation? recommendation = card.Recommendations.FirstOrDefault(r => r.CanApply && !String.IsNullOrEmpty(r.Id));
            if (recommendation != null)
            {
                AskBlackBoxFollowUp apply = new AskBlackBoxFollowUp();
                apply.Name = "apply";
                apply.Label = "Apply the configuration change";
                apply.Description = "Propose setting " + recommendation.Setting + (recommendation.SuggestedValue != null ? " to " + recommendation.SuggestedValue : String.Empty) + " (" + recommendation.Id + "). Approving it runs a dry run, then applies it.";
                apply.ToolName = "apply_blackbox_recommendation";
                apply.ArgumentsText = new JsonObject { ["entryId"] = entryId, ["recommendationId"] = recommendation.Id }.ToJsonString();
                followUps.Add(apply);
            }

            AskBlackBoxFollowUp armada = new AskBlackBoxFollowUp();
            armada.Name = "armada-fix";
            armada.Label = "Draft an Armada fix mission";
            BlackBoxProposedChange? top = digest.TopProposedChange;
            armada.Description = top != null
                ? "Ask the captain to draft the Armada change \"" + top.Title + "\" (severity " + top.Severity + ") as a mission on the Armada vessel."
                : "Ask the captain to draft a mission that changes Armada so this failure does not happen again.";
            armada.Prompt = "Draft an Armada fix mission for black box entry " + entryId + " (" + subject + "): start from the entry's Resilience section"
                + (top != null ? " (top proposed change: \"" + top.Title + "\")" : String.Empty)
                + ", with its acceptance test, regression test and code pointers at the Armada build commit, and propose it on the Armada vessel for my approval.";
            followUps.Add(armada);

            AskBlackBoxFollowUp compare = new AskBlackBoxFollowUp();
            compare.Name = "compare";
            compare.Label = "Compare with similar failures";
            compare.Description = "Ask the captain to compare this failure with others that share its fingerprint.";
            compare.Prompt = "Compare black box entry " + entryId + " (" + subject + ") with similar failures" + (String.IsNullOrEmpty(card.Fingerprint) ? String.Empty : " (fingerprint " + card.Fingerprint + ")") + ": what do they have in common, and what differs?";
            followUps.Add(compare);
            return followUps;
        }

        /// <summary>
        /// Whether a system prompt already carries the black box section.
        /// </summary>
        /// <param name="systemPrompt">System prompt, or null.</param>
        /// <returns>True when the heading is present.</returns>
        public static bool IsPresent(string? systemPrompt)
        {
            return !String.IsNullOrEmpty(systemPrompt) && systemPrompt.Contains(Heading, StringComparison.Ordinal);
        }

        /// <summary>
        /// The system prompt with the black box section appended when it lacks one (a customized ask.system template, or
        /// none at all).
        /// </summary>
        /// <param name="systemPrompt">System prompt, or null.</param>
        /// <returns>The system prompt with the section.</returns>
        public static string WithSection(string? systemPrompt)
        {
            if (IsPresent(systemPrompt)) return systemPrompt!;
            if (String.IsNullOrWhiteSpace(systemPrompt)) return Section.TrimEnd();
            return systemPrompt!.TrimEnd() + "\n\n" + Section.TrimEnd();
        }

        #endregion
    }
}
