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
            AgentRuntimeEnum.OpenCode,
            AgentRuntimeEnum.Pi
        };

        /// <summary>
        /// Per-runtime account usage capture capabilities, in runtime order, beside the boolean
        /// <see cref="SupportsUsageCapture"/>. Describes the source (active, passive or estimated) and the
        /// reason a runtime has none; it never promises credentials or quota numbers, and conditional
        /// capability (a Custom command) still answers Unsupported without one.
        /// </summary>
        public static IReadOnlyList<UsageCaptureCapability> UsageCaptureCapabilities { get; } = new List<UsageCaptureCapability>
        {
            new UsageCaptureCapability(AgentRuntimeEnum.ClaudeCode, UsageCaptureKindEnum.Active),
            new UsageCaptureCapability(AgentRuntimeEnum.Codex, UsageCaptureKindEnum.Active),
            new UsageCaptureCapability(AgentRuntimeEnum.Gemini, UsageCaptureKindEnum.Active),
            new UsageCaptureCapability(AgentRuntimeEnum.Cursor, "Cursor has no verified account limit interface; its provider rejects without structured quota data."),
            new UsageCaptureCapability(AgentRuntimeEnum.Mux, "Mux runs locally and its provider rate-limit headers are not observable by Armada; per-run stats are accounting, not remaining capacity."),
            new UsageCaptureCapability(AgentRuntimeEnum.OpenCode, "OpenCode local stats are session accounting; no remaining quota source exists for its configured provider."),
            new UsageCaptureCapability(AgentRuntimeEnum.ApiEndpoint, UsageCaptureKindEnum.Passive),
            new UsageCaptureCapability(AgentRuntimeEnum.Custom, UsageCaptureKindEnum.Active),
            new UsageCaptureCapability(AgentRuntimeEnum.Pi, "Pi has no usage capture adapter; no verified account limit source exists for it.")
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
        /// True when the runtime has a usable account usage capture source at all: an active read of a
        /// verified source, a passive header harvest, an operator-configured estimate, or (Custom) the
        /// administrator-chosen executable contract. Conditional capability does not promise credentials
        /// or quota numbers: an observation the runtime cannot make still answers Unsupported.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>True when a capture source exists.</returns>
        public static bool SupportsUsageCapture(AgentRuntimeEnum runtime)
        {
            return UsageCapture(runtime).Supported;
        }

        /// <summary>
        /// The runtime's account usage capture capability: what kind of source it has, and the reason it
        /// has none when it does not.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>The capability descriptor.</returns>
        public static UsageCaptureCapability UsageCapture(AgentRuntimeEnum runtime)
        {
            foreach (UsageCaptureCapability capability in UsageCaptureCapabilities)
            {
                if (capability.Runtime == runtime) return capability;
            }

            return new UsageCaptureCapability(runtime, "The runtime has no verified account usage capture source.");
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
