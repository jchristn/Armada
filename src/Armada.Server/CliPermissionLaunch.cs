namespace Armada.Server
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Runtimes;
    using Armada.Runtimes.Interfaces;

    /// <summary>
    /// Applies an effective CLI permission policy to a runtime instance before launch. Refuse and Bypass are carried by
    /// the launched captain's auto-approve option (see <see cref="CaptainRuntimeOptions.WithEffectiveAutoApprove"/>);
    /// ApproveInArmada additionally points Claude Code's --permission-prompt-tool at Armada's cli_permission_prompt
    /// tool on the scoped "armada" MCP server, and gates an API-endpoint captain's built-in shell tool (run_process)
    /// in-process.
    /// </summary>
    public static class CliPermissionLaunch
    {
        #region Public-Methods

        /// <summary>
        /// Configure the runtime for the policy.
        /// </summary>
        /// <param name="runtime">Runtime instance for this launch.</param>
        /// <param name="effective">Effective policy (after fallback).</param>
        /// <param name="promptTimeoutSeconds">Seconds a permission prompt may wait.</param>
        /// <returns>True when the runtime will send its permission prompts to Armada.</returns>
        public static bool Apply(IAgentRuntime runtime, CliPermissionPolicyEnum effective, int promptTimeoutSeconds)
        {
            return Apply(runtime, effective, promptTimeoutSeconds, null);
        }

        /// <summary>
        /// Configure the runtime for the policy, with the in-process prompt used by API-endpoint captains.
        /// </summary>
        /// <param name="runtime">Runtime instance for this launch.</param>
        /// <param name="effective">Effective policy (after fallback).</param>
        /// <param name="promptTimeoutSeconds">Seconds a permission prompt may wait.</param>
        /// <param name="inProcessPrompt">Answers an API-endpoint captain's run_process prompt (tool, arguments JSON,
        /// cancellation), or null (ApproveInArmada then refuses).</param>
        /// <returns>True when the runtime will send its permission prompts to Armada.</returns>
        public static bool Apply(IAgentRuntime runtime, CliPermissionPolicyEnum effective, int promptTimeoutSeconds, Func<string, string, CancellationToken, Task<CliPermissionPromptOutcome>>? inProcessPrompt)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (runtime is ApiAgentRuntime api)
            {
                api.ShellPolicy = effective;
                api.PermissionPrompt = inProcessPrompt;
                return effective == CliPermissionPolicyEnum.ApproveInArmada && inProcessPrompt != null;
            }

            if (effective != CliPermissionPolicyEnum.ApproveInArmada) return false;
            if (!(runtime is ClaudeCodeRuntime claude)) return false;
            claude.PermissionPromptTool = CliPermissionPolicyResolver.ClaudePromptToolName;
            claude.PermissionPromptTimeoutSeconds = promptTimeoutSeconds;
            return true;
        }

        #endregion
    }
}
