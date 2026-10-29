namespace Armada.Core.BlackBox
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Armada.Core.Models;

    /// <summary>
    /// The MCP tools a black box recovery path or recommendation may run, and the equivalent REST and CLI command text
    /// for each. Recovery runs refuse any other tool. Pure and thread-safe.
    /// </summary>
    public static class BlackBoxActionCommands
    {
        #region Public-Members

        /// <summary>
        /// Tools a recorded action may name. Each is an existing Armada MCP tool, authorized as if the caller had called
        /// it directly.
        /// </summary>
        public static IReadOnlyList<string> SupportedTools { get; } = new List<string>
        {
            "retry_landing", "restart_mission", "cancel_mission", "unstick_dock", "repair_dock", "update_vessel", "update_mission"
        };

        /// <summary>
        /// Tools that can lose work (uncommitted changes in a worktree, an in-flight mission).
        /// </summary>
        public static IReadOnlyList<string> DataLossTools { get; } = new List<string> { "cancel_mission", "unstick_dock" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a tool may be named by a recorded action.
        /// </summary>
        /// <param name="tool">Tool name.</param>
        /// <returns>True when supported.</returns>
        public static bool IsSupported(string? tool)
        {
            return !String.IsNullOrEmpty(tool) && SupportedTools.Contains(tool!, StringComparer.Ordinal);
        }

        /// <summary>
        /// The REST request equivalent to an action, or null when there is none.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>The REST command text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when action is null.</exception>
        public static string? Rest(BlackBoxAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            string? missionId = Arg(action, "missionId");
            string? dockId = Arg(action, "dockId");
            string? vesselId = Arg(action, "vesselId");
            switch (action.Tool)
            {
                case "retry_landing": return missionId == null ? null : "POST /api/v1/missions/" + missionId + "/retry-landing";
                case "restart_mission": return missionId == null ? null : "POST /api/v1/missions/" + missionId + "/restart";
                case "cancel_mission": return missionId == null ? null : "DELETE /api/v1/missions/" + missionId;
                case "unstick_dock": return dockId == null ? null : "POST /api/v1/docks/" + dockId + "/unstick";
                case "repair_dock": return dockId == null ? null : "POST /api/v1/docks/" + dockId + "/repair";
                case "update_vessel": return vesselId == null ? null : "PUT /api/v1/vessels/" + vesselId + " " + Body(action, "vesselId");
                case "update_mission": return missionId == null ? null : "PUT /api/v1/missions/" + missionId + " " + Body(action, "missionId");
                default: return null;
            }
        }

        /// <summary>
        /// The Helm CLI command equivalent to an action, or null when the CLI has none.
        /// </summary>
        /// <param name="action">Action.</param>
        /// <returns>The CLI command text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when action is null.</exception>
        public static string? Cli(BlackBoxAction action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            string? missionId = Arg(action, "missionId");
            switch (action.Tool)
            {
                case "restart_mission": return missionId == null ? null : "armada mission restart " + missionId;
                case "cancel_mission": return missionId == null ? null : "armada mission cancel " + missionId;
                default: return null;
            }
        }

        #endregion

        #region Private-Methods

        private static string? Arg(BlackBoxAction action, string name)
        {
            return action.Arguments.TryGetValue(name, out string? value) && !String.IsNullOrWhiteSpace(value) ? value : null;
        }

        private static string Body(BlackBoxAction action, string idName)
        {
            IEnumerable<string> fields = action.Arguments.Where(a => a.Key != idName).Select(a => "\"" + a.Key + "\": " + BlackBoxRuleTemplate.JsonLiteral(a.Value));
            return "{" + String.Join(", ", fields) + "}";
        }

        #endregion
    }
}
