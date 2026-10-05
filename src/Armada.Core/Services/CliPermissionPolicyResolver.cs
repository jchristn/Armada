namespace Armada.Core.Services
{
    using System;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Settings;

    /// <summary>
    /// Resolves the CLI tool permission policy of a captain launch. Most specific wins.
    /// <para>Ask turns (and milestone narrations):</para>
    /// <list type="number">
    /// <item>the thread's CliPermissionPolicy;</item>
    /// <item>the captain's CliPermissionPolicy, except that a captain-level Bypass is honored only when
    /// Ask.CaptainAutoApprove is true (Ask turns can be started by any user of the tenant);</item>
    /// <item>when Ask.CaptainAutoApprove is true, the captain's legacy autoApprove option (absent or true is Bypass,
    /// false is Refuse), which reproduces the behavior before CLI tool permissions;</item>
    /// <item>Permissions.AskDefaultPolicy (default ApproveInArmada).</item>
    /// </list>
    /// <para>Missions:</para>
    /// <list type="number">
    /// <item>the vessel's legacy AutoApprove override: true is Bypass;</item>
    /// <item>the captain's CliPermissionPolicy;</item>
    /// <item>the captain's explicit legacy autoApprove option (true is Bypass, false is Refuse);</item>
    /// <item>Permissions.MissionDefaultPolicy (default Bypass, the behavior of a captain without autoApprove before).</item>
    /// </list>
    /// A vessel AutoApprove of false caps a mission's result below Bypass (Bypass becomes Refuse). Finally,
    /// ApproveInArmada falls back to Refuse when the runtime has no prompt hook, the launch has no scoped MCP token, or
    /// the launch runs on a Harbor.
    /// </summary>
    public static class CliPermissionPolicyResolver
    {
        #region Public-Members

        /// <summary>
        /// Name of the Armada MCP tool CLI captains call for permission prompts.
        /// </summary>
        public const string PromptToolName = "cli_permission_prompt";

        /// <summary>
        /// The name Claude Code uses for <see cref="PromptToolName"/> on the scoped "armada" MCP server (the value of
        /// --permission-prompt-tool).
        /// </summary>
        public const string ClaudePromptToolName = "mcp__armada__" + PromptToolName;

        /// <summary>
        /// Start of every policy note (<see cref="CliPermissionResolution.Note"/>).
        /// </summary>
        public const string NotePrefix = "CLI tool permissions: ";

        /// <summary>
        /// Label the Admiral writes before a note in a mission log (the line reads "[time] Armada: CLI tool permissions:
        /// ..."). <see cref="FindMissionLogNote"/> reads it back.
        /// </summary>
        public const string MissionLogNoteLabel = "Armada: ";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether a runtime has a permission prompt hook Armada drives (ApproveInArmada is honored): Claude Code through
        /// --permission-prompt-tool, and API-endpoint captains, whose built-in shell tool runs in-process behind Armada's
        /// own gate.
        /// </summary>
        /// <param name="runtime">Runtime.</param>
        /// <returns>True for Claude Code and ApiEndpoint.</returns>
        public static bool SupportsPromptHook(AgentRuntimeEnum runtime)
        {
            return runtime == AgentRuntimeEnum.ClaudeCode || runtime == AgentRuntimeEnum.ApiEndpoint;
        }

        /// <summary>
        /// Resolve the policy of an Ask turn.
        /// </summary>
        /// <param name="settings">Admiral settings.</param>
        /// <param name="thread">Thread, or null.</param>
        /// <param name="captain">Captain.</param>
        /// <param name="hasSessionToken">Whether the turn's captain gets a thread-scoped MCP token.</param>
        /// <returns>The resolution; never null.</returns>
        public static CliPermissionResolution ResolveForAsk(ArmadaSettings settings, AskThread? thread, Captain captain, bool hasSessionToken)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            CliPermissionResolution resolution = new CliPermissionResolution();
            bool trustCaptain = settings.Ask.CaptainAutoApprove;
            if (thread != null && thread.CliPermissionPolicy.HasValue)
            {
                resolution.Requested = thread.CliPermissionPolicy.Value;
                resolution.Source = CliPermissionPolicySourceEnum.AskThread;
            }
            else if (captain.CliPermissionPolicy.HasValue && (captain.CliPermissionPolicy.Value != CliPermissionPolicyEnum.Bypass || trustCaptain))
            {
                resolution.Requested = captain.CliPermissionPolicy.Value;
                resolution.Source = CliPermissionPolicySourceEnum.Captain;
            }
            else if (trustCaptain && !captain.CliPermissionPolicy.HasValue)
            {
                resolution.Requested = CaptainRuntimeOptions.GetAutoApprove(captain) ? CliPermissionPolicyEnum.Bypass : CliPermissionPolicyEnum.Refuse;
                resolution.Source = CliPermissionPolicySourceEnum.CaptainAutoApprove;
            }
            else
            {
                resolution.Requested = settings.Permissions.AskDefaultPolicy;
                resolution.Source = CliPermissionPolicySourceEnum.ServerDefault;
            }

            ApplyFallback(resolution, captain.Runtime, hasSessionToken, false);
            resolution.Note = BuildNote(resolution, true);
            return resolution;
        }

        /// <summary>
        /// Resolve the policy of a mission launch.
        /// </summary>
        /// <param name="settings">Admiral settings.</param>
        /// <param name="captain">Captain.</param>
        /// <param name="vessel">Mission vessel, or null.</param>
        /// <param name="hasSessionToken">Whether the launch carries a mission-scoped MCP token.</param>
        /// <param name="remoteHarbor">Whether the launch runs on a Harbor.</param>
        /// <returns>The resolution; never null.</returns>
        public static CliPermissionResolution ResolveForMission(ArmadaSettings settings, Captain captain, Vessel? vessel, bool hasSessionToken, bool remoteHarbor)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (captain == null) throw new ArgumentNullException(nameof(captain));

            CliPermissionResolution resolution = new CliPermissionResolution();
            bool? legacy = CaptainRuntimeOptions.GetExplicitAutoApprove(captain.RuntimeOptionsJson);
            if (vessel != null && vessel.AutoApprove == true)
            {
                resolution.Requested = CliPermissionPolicyEnum.Bypass;
                resolution.Source = CliPermissionPolicySourceEnum.VesselAutoApprove;
            }
            else if (captain.CliPermissionPolicy.HasValue)
            {
                resolution.Requested = captain.CliPermissionPolicy.Value;
                resolution.Source = CliPermissionPolicySourceEnum.Captain;
            }
            else if (legacy.HasValue)
            {
                resolution.Requested = legacy.Value ? CliPermissionPolicyEnum.Bypass : CliPermissionPolicyEnum.Refuse;
                resolution.Source = CliPermissionPolicySourceEnum.CaptainAutoApprove;
            }
            else
            {
                resolution.Requested = settings.Permissions.MissionDefaultPolicy;
                resolution.Source = CliPermissionPolicySourceEnum.ServerDefault;
            }

            if (vessel != null && vessel.AutoApprove == false && resolution.Requested == CliPermissionPolicyEnum.Bypass)
            {
                resolution.Requested = CliPermissionPolicyEnum.Refuse;
                resolution.Source = CliPermissionPolicySourceEnum.VesselAutoApprove;
            }

            ApplyFallback(resolution, captain.Runtime, hasSessionToken, remoteHarbor);
            resolution.Note = BuildNote(resolution, false);
            return resolution;
        }

        /// <summary>
        /// The policy note the Admiral wrote into a mission log when it launched the captain (the line
        /// <see cref="MissionLogNoteLabel"/> + <see cref="NotePrefix"/>...), the last one when the text holds several
        /// launches.
        /// </summary>
        /// <param name="log">Mission log text (or its first lines), or null.</param>
        /// <returns>The note, starting with <see cref="NotePrefix"/>, or null when the text has none.</returns>
        public static string? FindMissionLogNote(string? log)
        {
            if (String.IsNullOrEmpty(log)) return null;
            string marker = MissionLogNoteLabel + NotePrefix;
            string? found = null;
            foreach (string raw in log!.Replace("\r\n", "\n").Split('\n'))
            {
                int at = raw.IndexOf(marker, StringComparison.Ordinal);
                if (at < 0) continue;
                string note = raw.Substring(at + MissionLogNoteLabel.Length).Trim();
                if (note.Length > NotePrefix.Length) found = note;
            }

            return found;
        }

        #endregion

        #region Private-Methods

        private static void ApplyFallback(CliPermissionResolution resolution, AgentRuntimeEnum runtime, bool hasSessionToken, bool remoteHarbor)
        {
            resolution.Effective = resolution.Requested;
            resolution.FallbackReason = null;
            if (resolution.Requested != CliPermissionPolicyEnum.ApproveInArmada) return;

            if (remoteHarbor) resolution.FallbackReason = CliPermissionFallbackReasonEnum.RemoteHarbor;
            else if (!SupportsPromptHook(runtime)) resolution.FallbackReason = CliPermissionFallbackReasonEnum.RuntimeUnsupported;
            else if (runtime == AgentRuntimeEnum.ClaudeCode && !hasSessionToken) resolution.FallbackReason = CliPermissionFallbackReasonEnum.NoSessionToken;

            if (resolution.FallbackReason.HasValue) resolution.Effective = CliPermissionPolicyEnum.Refuse;
        }

        private static string BuildNote(CliPermissionResolution resolution, bool ask)
        {
            string from;
            switch (resolution.Source)
            {
                case CliPermissionPolicySourceEnum.AskThread: from = "this conversation's CLI tools setting"; break;
                case CliPermissionPolicySourceEnum.VesselAutoApprove: from = "the vessel's auto-approve override"; break;
                case CliPermissionPolicySourceEnum.Captain: from = "the captain's CLI tool permission policy"; break;
                case CliPermissionPolicySourceEnum.CaptainAutoApprove: from = "the captain's auto-approve option"; break;
                default: from = ask ? "the server default (Permissions.AskDefaultPolicy)" : "the server default (Permissions.MissionDefaultPolicy)"; break;
            }

            string where = ask
                ? "Change it in the conversation header (CLI tools), on the captain, or in Settings > CLI Tool Permissions."
                : "Change it on the captain, with the vessel's auto-approve override, or in Settings > CLI Tool Permissions.";

            string text = NotePrefix + resolution.Effective + " (from " + from + ").";
            if (resolution.FallbackReason.HasValue)
            {
                string why;
                switch (resolution.FallbackReason.Value)
                {
                    case CliPermissionFallbackReasonEnum.RuntimeUnsupported: why = "this captain's runtime has no permission prompt hook Armada can answer"; break;
                    case CliPermissionFallbackReasonEnum.NoSessionToken: why = "the launch has no scoped Armada MCP token (Mcp.MissionScopedTokens is off or the owner is unknown)"; break;
                    default: why = "the captain runs on a Harbor, which cannot route permission prompts to Armada yet"; break;
                }

                text = NotePrefix + "ApproveInArmada requested (from " + from + "), running as Refuse because " + why + ".";
            }

            if (resolution.Effective == CliPermissionPolicyEnum.Refuse)
                text += " Tools that need approval (such as shell commands) are refused. " + where;
            else if (resolution.Effective == CliPermissionPolicyEnum.ApproveInArmada)
                text += " Tools that need approval wait for an approver in Armada (Approvals). " + where;
            else
                text += " The CLI runs with its permission-bypass flag. " + where;
            return text;
        }

        #endregion
    }
}
