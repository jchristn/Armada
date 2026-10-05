namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;

    /// <summary>
    /// Single source of truth for per-runtime capabilities that both the captain model (and through it the REST API,
    /// dashboard, and TUI) and the runtime adapters report. Keeping the answer here means the UI never offers an
    /// operation that the runtime adapter then refuses.
    /// </summary>
    public static class AgentRuntimeCapabilities
    {
        #region Public-Members

        /// <summary>
        /// Runtimes that can run Armada planning (and objective refinement) sessions, in display order. Planning relaunches
        /// a CLI against the preserved transcript, so only CLI runtimes qualify: API-endpoint captains run an in-process
        /// tool loop with no planning transcript flow, and a Custom runtime has no shared contract to drive.
        /// </summary>
        public static IReadOnlyList<AgentRuntimeEnum> PlanningSessionRuntimes { get; } = new List<AgentRuntimeEnum>
        {
            AgentRuntimeEnum.ClaudeCode,
            AgentRuntimeEnum.Codex,
            AgentRuntimeEnum.Gemini,
            AgentRuntimeEnum.Cursor,
            AgentRuntimeEnum.Mux,
            AgentRuntimeEnum.OpenCode
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when captains of this runtime can run planning and objective refinement sessions.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>True when supported.</returns>
        public static bool SupportsPlanningSessions(AgentRuntimeEnum runtime)
        {
            foreach (AgentRuntimeEnum supported in PlanningSessionRuntimes)
            {
                if (supported == runtime) return true;
            }

            return false;
        }

        /// <summary>
        /// User-facing reason a runtime cannot run planning sessions, or null when it can. The runtime list is built
        /// from <see cref="PlanningSessionRuntimes"/>, so the message cannot drift from the capability.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>The reason, or null when supported.</returns>
        public static string? PlanningSessionUnsupportedReason(AgentRuntimeEnum runtime)
        {
            if (SupportsPlanningSessions(runtime)) return null;

            List<string> names = new List<string>();
            foreach (AgentRuntimeEnum supported in PlanningSessionRuntimes)
            {
                names.Add(supported.ToString());
            }

            string list = names.Count > 1
                ? String.Join(", ", names.GetRange(0, names.Count - 1)) + ", and " + names[names.Count - 1]
                : String.Join(String.Empty, names);
            return "Planning sessions currently support only the built-in " + list + " runtimes; " + runtime + " captains cannot run them.";
        }

        #endregion
    }
}
