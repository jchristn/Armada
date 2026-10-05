namespace Armada.Tui.Approvals
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Tui.Services;

    /// <summary>
    /// Localized text for CLI tool permission requests shared by the Approvals center and the Ask transcript: the row
    /// title (tool and command), where the captain runs (captain, vessel, mission or conversation), the expiry
    /// countdown, the status, and the one-line explanation of a refused tool call from the thread's typed
    /// <see cref="CliPermissionResolution"/>. Every string goes through the shared catalog. Thread-safe (stateless).
    /// </summary>
    public static class CliPermissionText
    {
        #region Public-Methods

        /// <summary>
        /// Row title: "Bash: git push origin main" (the tool alone when there is no summary).
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>Title.</returns>
        public static string Title(CliPermissionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string tool = String.IsNullOrEmpty(request.ToolName) ? "tool" : request.ToolName;
            string summary = OneLine(request.SummaryText);
            return summary.Length > 0 ? tool + ": " + summary : tool;
        }

        /// <summary>
        /// Where the prompt comes from: "Captain claude-1  Vessel web  Mission Fix tables" (or "Conversation ...").
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="request">Request.</param>
        /// <returns>Text (empty when nothing is known).</returns>
        public static string Where(ITextLocalizer? loc, CliPermissionRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            List<string> parts = new List<string>();
            string? captain = First(request.CaptainName, request.CaptainId);
            if (captain != null) parts.Add(L(loc, "Captain") + " " + captain);
            string? vessel = First(request.VesselName, request.VesselId);
            if (vessel != null) parts.Add(L(loc, "Vessel") + " " + vessel);
            string? mission = First(request.MissionTitle, request.MissionId);
            if (mission != null) parts.Add(L(loc, "Mission") + " " + mission);
            else
            {
                string? thread = First(request.ThreadTitle, request.ThreadId);
                if (thread != null) parts.Add(L(loc, "Conversation") + " " + thread);
            }

            return String.Join("  ", parts);
        }

        /// <summary>
        /// Remaining time as m:ss (h:mm:ss over an hour), or 0:00 once expired.
        /// </summary>
        /// <param name="expiresUtc">Expiry.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Countdown.</returns>
        public static string Countdown(DateTime expiresUtc, DateTime nowUtc)
        {
            TimeSpan left = expiresUtc - nowUtc;
            if (left <= TimeSpan.Zero) return "0:00";
            int total = (int)Math.Ceiling(left.TotalSeconds);
            int hours = total / 3600;
            int minutes = (total % 3600) / 60;
            int seconds = total % 60;
            if (hours > 0) return hours.ToString(CultureInfo.InvariantCulture) + ":" + minutes.ToString("00", CultureInfo.InvariantCulture) + ":" + seconds.ToString("00", CultureInfo.InvariantCulture);
            return minutes.ToString(CultureInfo.InvariantCulture) + ":" + seconds.ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// "Expires in 4:12", or "Expired" once past.
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="expiresUtc">Expiry.</param>
        /// <param name="nowUtc">Now.</param>
        /// <returns>Text.</returns>
        public static string ExpiresIn(ITextLocalizer? loc, DateTime expiresUtc, DateTime nowUtc)
        {
            if (expiresUtc <= nowUtc) return L(loc, "Expired");
            return L(loc, "Expires in {{time}}", "time", Countdown(expiresUtc, nowUtc));
        }

        /// <summary>
        /// Status label.
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="status">Status.</param>
        /// <returns>Label.</returns>
        public static string Status(ITextLocalizer? loc, CliPermissionRequestStatusEnum status)
        {
            switch (status)
            {
                case CliPermissionRequestStatusEnum.Pending: return L(loc, "Pending");
                case CliPermissionRequestStatusEnum.Allowed: return L(loc, "Allowed");
                case CliPermissionRequestStatusEnum.Denied: return L(loc, "Denied");
                case CliPermissionRequestStatusEnum.Expired: return L(loc, "Expired");
                case CliPermissionRequestStatusEnum.Cancelled: return L(loc, "Cancelled");
                default: return status.ToString();
            }
        }

        /// <summary>
        /// Rule scope label.
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="scope">Scope.</param>
        /// <returns>Label.</returns>
        public static string Scope(ITextLocalizer? loc, CliPermissionRuleScopeEnum scope)
        {
            switch (scope)
            {
                case CliPermissionRuleScopeEnum.Captain: return L(loc, "This captain");
                case CliPermissionRuleScopeEnum.Vessel: return L(loc, "This vessel");
                case CliPermissionRuleScopeEnum.Global: return L(loc, "Everywhere (global)");
                default: return scope.ToString();
            }
        }

        /// <summary>
        /// Label of a policy (the dashboard's <c>policyLabel</c>): Refuse, Approve in Armada, Bypass, or Inherit for null.
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="policy">Policy, or null for Inherit.</param>
        /// <returns>Label.</returns>
        public static string Policy(ITextLocalizer? loc, CliPermissionPolicyEnum? policy)
        {
            switch (policy)
            {
                case CliPermissionPolicyEnum.Refuse: return L(loc, "Refuse");
                case CliPermissionPolicyEnum.ApproveInArmada: return L(loc, "Approve in Armada");
                case CliPermissionPolicyEnum.Bypass: return L(loc, "Bypass");
                case null: return L(loc, "Inherit");
                default: return policy.Value.ToString();
            }
        }

        /// <summary>
        /// The strong warning every Bypass choice must confirm (the dashboard's <c>bypassWarning</c>).
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <returns>Warning.</returns>
        public static string BypassWarning(ITextLocalizer? loc)
        {
            return L(loc, "Bypass lets the captain run any command on the Admiral host as the Armada service user without asking. Shell commands, file edits, and network fetches all run immediately with no approval and no rules applied. Only choose Bypass for captains and work you fully trust.");
        }

        /// <summary>
        /// "Effective: Approve in Armada (from the server default)." with the fallback sentence when there is one (the
        /// dashboard's <c>resolutionSummary</c>).
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="resolution">Resolution, or null.</param>
        /// <param name="includeFallback">Append the fallback sentence.</param>
        /// <returns>Summary, or empty when there is no resolution.</returns>
        public static string Effective(ITextLocalizer? loc, CliPermissionResolution? resolution, bool includeFallback)
        {
            if (resolution == null) return "";
            Dictionary<string, object?> args = LocalizationArgs.Of("policy", Policy(loc, resolution.Effective), "source", Source(loc, resolution.Source));
            string text = loc != null
                ? loc.T("Effective: {{policy}} (from {{source}}).", args)
                : "Effective: " + Policy(null, resolution.Effective) + " (from " + Source(null, resolution.Source) + ").";
            if (includeFallback && resolution.FallbackReason != null) text += " " + Fallback(loc, resolution.FallbackReason.Value);
            return text;
        }

        /// <summary>
        /// Rule action label (Allow or Deny).
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="action">Action.</param>
        /// <returns>Label.</returns>
        public static string RuleAction(ITextLocalizer? loc, CliPermissionRuleActionEnum action)
        {
            return action == CliPermissionRuleActionEnum.Deny ? L(loc, "Deny") : L(loc, "Allow");
        }

        /// <summary>
        /// Rule scope name for the rules list and form (Global, Vessel, Captain).
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="scope">Scope.</param>
        /// <returns>Label.</returns>
        public static string ScopeName(ITextLocalizer? loc, CliPermissionRuleScopeEnum scope)
        {
            switch (scope)
            {
                case CliPermissionRuleScopeEnum.Vessel: return L(loc, "Vessel");
                case CliPermissionRuleScopeEnum.Captain: return L(loc, "Captain");
                default: return L(loc, "Global");
            }
        }

        /// <summary>
        /// What a rule applies to: "Global", "Vessel web", or "Captain claude-1" (names when known, ids otherwise),
        /// with " (all tenants)" for a global admin's rule with no tenant.
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="rule">Rule.</param>
        /// <param name="vesselName">Vessel name for the rule's vessel, or null.</param>
        /// <param name="captainName">Captain name for the rule's captain, or null.</param>
        /// <returns>Text.</returns>
        public static string Target(ITextLocalizer? loc, CliPermissionRule rule, string? vesselName, string? captainName)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            string text;
            if (rule.Scope == CliPermissionRuleScopeEnum.Vessel) text = L(loc, "Vessel {{name}}", "name", First(vesselName, rule.VesselId) ?? "-");
            else if (rule.Scope == CliPermissionRuleScopeEnum.Captain) text = L(loc, "Captain {{name}}", "name", First(captainName, rule.CaptainId) ?? "-");
            else text = L(loc, "Global");
            if (String.IsNullOrEmpty(rule.TenantId)) text += " " + L(loc, "(all tenants)");
            return text;
        }

        /// <summary>
        /// One-line explanation of a tool call the CLI refused for lack of permission, from the thread's resolution:
        /// Refuse names the policy source (and the fallback reason) and where to change it; ApproveInArmada points at
        /// the permission card.
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="resolution">The thread's CLI permission resolution, or null when unknown.</param>
        /// <returns>Explanation.</returns>
        public static string DeniedExplanation(ITextLocalizer? loc, CliPermissionResolution? resolution)
        {
            if (resolution == null) return L(loc, "Refused: the CLI did not have permission to run this tool.");
            if (resolution.Effective == CliPermissionPolicyEnum.ApproveInArmada) return L(loc, "Denied in Armada (see the permission card).");
            if (resolution.Effective == CliPermissionPolicyEnum.Bypass) return L(loc, "Refused by the CLI's own permission check.");
            string text = L(loc, "Refused: CLI tools run with policy Refuse (from {{source}}). Change it in the conversation header (CLI tools), on the captain, or in Settings > CLI Tool Permissions.", "source", Source(loc, resolution.Source));
            if (resolution.FallbackReason != null) text += " " + Fallback(loc, resolution.FallbackReason.Value);
            return text;
        }

        /// <summary>
        /// Label of a policy source.
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="source">Source.</param>
        /// <returns>Label.</returns>
        public static string Source(ITextLocalizer? loc, CliPermissionPolicySourceEnum source)
        {
            switch (source)
            {
                case CliPermissionPolicySourceEnum.AskThread: return L(loc, "this conversation");
                case CliPermissionPolicySourceEnum.VesselAutoApprove: return L(loc, "the vessel's auto-approve setting");
                case CliPermissionPolicySourceEnum.Captain: return L(loc, "the captain");
                case CliPermissionPolicySourceEnum.CaptainAutoApprove: return L(loc, "the captain's auto-approve setting");
                case CliPermissionPolicySourceEnum.ServerDefault: return L(loc, "the server default");
                default: return source.ToString();
            }
        }

        /// <summary>
        /// Sentence explaining why Approve in Armada fell back to Refuse.
        /// </summary>
        /// <param name="loc">Localizer, or null for English.</param>
        /// <param name="reason">Reason.</param>
        /// <returns>Sentence.</returns>
        public static string Fallback(ITextLocalizer? loc, CliPermissionFallbackReasonEnum reason)
        {
            switch (reason)
            {
                case CliPermissionFallbackReasonEnum.RuntimeUnsupported: return L(loc, "Approve in Armada fell back to Refuse because this runtime cannot ask Armada for permission.");
                case CliPermissionFallbackReasonEnum.NoSessionToken: return L(loc, "Approve in Armada fell back to Refuse because the turn had no Armada session token.");
                case CliPermissionFallbackReasonEnum.RemoteHarbor: return L(loc, "Approve in Armada fell back to Refuse because the captain runs on a remote harbor.");
                default: return reason.ToString();
            }
        }

        #endregion

        #region Private-Methods

        private static string L(ITextLocalizer? loc, string text)
        {
            return loc == null ? text : loc.T(text);
        }

        private static string L(ITextLocalizer? loc, string text, string key, string value)
        {
            if (loc != null) return loc.T(text, LocalizationArgs.Of(key, value));
            return text.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        }

        private static string? First(string? a, string? b)
        {
            if (!String.IsNullOrWhiteSpace(a)) return a!.Trim();
            if (!String.IsNullOrWhiteSpace(b)) return b!.Trim();
            return null;
        }

        private static string OneLine(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return "";
            return String.Join(" ", text!.Split(new char[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        #endregion
    }
}
