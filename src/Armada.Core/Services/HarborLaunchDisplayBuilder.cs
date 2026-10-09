namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Harbor;
    using Armada.Core.Models;

    /// <summary>
    /// Builds the display fields the Admiral sends with a launch (<see cref="HarborLaunchDisplay"/>), so a Harbor can say
    /// in its Running now list what a job is about. Pure.
    /// </summary>
    public static class HarborLaunchDisplayBuilder
    {
        #region Public-Methods

        /// <summary>
        /// The display fields of a mission launch: the mission and its vessel, its voyage and its position in it (by
        /// creation order), the captain and its model, the pipeline stage (the mission's persona), and the branch.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <param name="captain">Captain running it.</param>
        /// <param name="vessel">Its vessel, or null.</param>
        /// <param name="voyage">Its voyage, or null.</param>
        /// <param name="voyageMissions">The voyage's missions, or null when unknown.</param>
        /// <param name="branchName">The branch the captain works on (the dock's), or null to use the mission's.</param>
        /// <returns>The display fields.</returns>
        public static HarborLaunchDisplay ForMission(Mission mission, Captain captain, Vessel? vessel, Voyage? voyage, List<MissionSummary>? voyageMissions, string? branchName)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            HarborLaunchDisplay display = new HarborLaunchDisplay
            {
                MissionTitle = Blank(mission.Title),
                VesselName = Blank(vessel?.Name),
                VoyageId = Blank(mission.VoyageId),
                VoyageTitle = voyage != null && String.Equals(voyage.Id, mission.VoyageId, StringComparison.Ordinal) ? Blank(voyage.Title) : null,
                CaptainName = Blank(captain.Name),
                CaptainModel = Blank(captain.Model),
                Stage = Blank(mission.Persona),
                BranchName = Blank(branchName) ?? Blank(mission.BranchName)
            };

            if (display.VoyageId != null && voyageMissions != null && voyageMissions.Count > 0)
            {
                List<MissionSummary> ordered = new List<MissionSummary>(voyageMissions);
                ordered.Sort((a, b) =>
                {
                    int byCreated = a.CreatedUtc.CompareTo(b.CreatedUtc);
                    return byCreated != 0 ? byCreated : String.CompareOrdinal(a.Id, b.Id);
                });
                int index = ordered.FindIndex(m => String.Equals(m.Id, mission.Id, StringComparison.Ordinal));
                if (index >= 0)
                {
                    display.VoyagePosition = index + 1;
                    display.VoyageMissionCount = ordered.Count;
                }
            }

            return display;
        }

        /// <summary>
        /// The display fields of an Ask Armada turn: the conversation, the user's question (its first line, cut to
        /// <see cref="HarborLaunchDisplay.MaxQuestionChars"/>), and the captain.
        /// </summary>
        /// <param name="threadId">Conversation identifier.</param>
        /// <param name="question">The user's question, or null (a summary or narration turn).</param>
        /// <param name="captain">Captain answering, or null.</param>
        /// <returns>The display fields.</returns>
        public static HarborLaunchDisplay ForAskTurn(string threadId, string? question, Captain? captain)
        {
            return new HarborLaunchDisplay
            {
                AskThreadId = Blank(threadId),
                AskQuestion = RuntimeActivityParser.FirstLine(question, HarborLaunchDisplay.MaxQuestionChars),
                CaptainName = Blank(captain?.Name),
                CaptainModel = Blank(captain?.Model)
            };
        }

        #endregion

        #region Private-Methods

        private static string? Blank(string? value)
        {
            return String.IsNullOrWhiteSpace(value) ? null : value!.Trim();
        }

        #endregion
    }
}
