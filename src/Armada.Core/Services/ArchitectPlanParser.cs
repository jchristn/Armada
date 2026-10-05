namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Models;

    /// <summary>
    /// Reads the structured plan an Architect captain emits as a fenced code block whose info string is
    /// <c>armada-plan</c>:
    /// <code>
    /// ```armada-plan
    /// {"missions":[{"title":"...","description":"...","dependsOn":null,"waitForOtherMissions":false}]}
    /// ```
    /// </code>
    /// The block is deserialized into <see cref="ArchitectPlan"/>. When no such block is present, or it does not
    /// deserialize into at least one titled mission, the caller falls back to the legacy <c>[ARMADA:MISSION]</c>
    /// marker format (documented as a fallback).
    /// </summary>
    public static class ArchitectPlanParser
    {
        #region Public-Members

        /// <summary>
        /// The fenced code block info string that marks an Architect plan.
        /// </summary>
        public const string FenceInfo = "armada-plan";

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Try to read an Architect plan from output. The last well-formed plan block wins (an Architect that revises
        /// its plan emits the final one last). Missions without a title, with a placeholder title such as "&lt;title&gt;", and repeated titles are
        /// dropped; a
        /// <c>dependsOn</c> that does not name an earlier kept mission is cleared.
        /// </summary>
        /// <param name="output">Architect output.</param>
        /// <param name="plan">The plan, when found.</param>
        /// <returns>True when a plan with at least one titled mission was found.</returns>
        public static bool TryParse(string? output, out ArchitectPlan? plan)
        {
            plan = null;
            if (String.IsNullOrWhiteSpace(output)) return false;

            foreach (string block in ExtractPlanBlocks(output))
            {
                ArchitectPlan? candidate;
                try
                {
                    candidate = JsonSerializer.Deserialize<ArchitectPlan>(block, _Options);
                }
                catch (JsonException)
                {
                    continue;
                }

                ArchitectPlan? normalized = Normalize(candidate);
                if (normalized != null) plan = normalized;
            }

            return plan != null;
        }

        /// <summary>
        /// One-sentence output contract for Architect prompts.
        /// </summary>
        /// <returns>Contract text.</returns>
        public static string FormatInstructions()
        {
            return "Respond only with real mission definitions: preferably one fenced ```" + FenceInfo + " block containing " +
                "{\"missions\":[{\"title\":...,\"description\":...,\"dependsOn\":<earlier mission number or null>," +
                "\"waitForOtherMissions\":<true|false>}]}, otherwise real [ARMADA:MISSION] blocks.";
        }

        #endregion

        #region Private-Methods

        private static List<string> ExtractPlanBlocks(string output)
        {
            List<string> blocks = new List<string>();
            string[] lines = output.Replace("\r\n", "\n").Split('\n');
            StringBuilder? current = null;

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (current == null)
                {
                    if (line.StartsWith("```", StringComparison.Ordinal)
                        && String.Equals(line.Substring(3).Trim(), FenceInfo, StringComparison.OrdinalIgnoreCase))
                    {
                        current = new StringBuilder();
                    }

                    continue;
                }

                if (line.StartsWith("```", StringComparison.Ordinal))
                {
                    blocks.Add(current.ToString());
                    current = null;
                    continue;
                }

                current.AppendLine(rawLine);
            }

            return blocks;
        }

        private static bool IsPlaceholder(string title)
        {
            // The prompt's own example uses angle-bracket placeholders such as "<title>"; an echoed example is not a plan.
            string trimmed = title.Trim();
            return trimmed.StartsWith("<", StringComparison.Ordinal) && trimmed.EndsWith(">", StringComparison.Ordinal);
        }

        private static ArchitectPlan? Normalize(ArchitectPlan? candidate)
        {
            if (candidate == null || candidate.Missions == null) return null;

            // Map each original 1-based position to its position after untitled entries are dropped, so dependsOn
            // keeps naming the mission the Architect meant.
            Dictionary<int, int> positionMap = new Dictionary<int, int>();
            HashSet<string> seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ArchitectPlan normalized = new ArchitectPlan();
            for (int i = 0; i < candidate.Missions.Count; i++)
            {
                ArchitectPlanMission? mission = candidate.Missions[i];
                if (mission == null || String.IsNullOrWhiteSpace(mission.Title)) continue;
                if (IsPlaceholder(mission.Title)) continue;
                if (!seenTitles.Add(mission.Title.Trim())) continue;

                ArchitectPlanMission copy = new ArchitectPlanMission();
                copy.Title = mission.Title.Trim();
                copy.Description = String.IsNullOrWhiteSpace(mission.Description) ? copy.Title : mission.Description.Trim();
                copy.WaitForOtherMissions = mission.WaitForOtherMissions;

                int originalPosition = i + 1;
                if (mission.DependsOn.HasValue
                    && mission.DependsOn.Value < originalPosition
                    && positionMap.TryGetValue(mission.DependsOn.Value, out int mapped))
                {
                    copy.DependsOn = mapped;
                }

                normalized.Missions.Add(copy);
                positionMap[originalPosition] = normalized.Missions.Count;
            }

            return normalized.Missions.Count > 0 ? normalized : null;
        }

        #endregion
    }
}
