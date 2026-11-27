namespace Armada.Helm.Commands
{
    using System;
    using System.Collections.Generic;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Parses <c>--rung</c> specs such as <c>runtime=ClaudeCode,model=opus,tier=Premium</c> into captain matchers.
    /// </summary>
    public static class PersonaRungParser
    {
        /// <summary>
        /// Parse one rung spec.
        /// </summary>
        /// <param name="spec">Comma-separated key=value pairs; keys are runtime, model, endpoint and tier.</param>
        /// <param name="rung">The parsed matcher.</param>
        /// <param name="error">Why parsing failed, or null.</param>
        /// <returns>True when the spec parsed and names at least one field.</returns>
        public static bool TryParse(string? spec, out PersonaCaptainPriority rung, out string? error)
        {
            rung = new PersonaCaptainPriority();
            error = null;
            bool any = false;
            string[] parts = String.IsNullOrWhiteSpace(spec)
                ? new string[0]
                : spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string part in parts)
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || eq == part.Length - 1)
                {
                    error = "'" + part + "' is not key=value";
                    return false;
                }

                string key = part.Substring(0, eq).Trim().ToLowerInvariant();
                string value = part.Substring(eq + 1).Trim();
                switch (key)
                {
                    case "runtime":
                        if (!Enum.TryParse<AgentRuntimeEnum>(value, true, out AgentRuntimeEnum runtime) || !Enum.IsDefined(typeof(AgentRuntimeEnum), runtime))
                        {
                            error = "unknown runtime '" + value + "'";
                            return false;
                        }
                        rung.Runtime = runtime;
                        break;
                    case "model":
                        rung.Model = value;
                        break;
                    case "endpoint":
                        rung.Endpoint = value;
                        break;
                    case "tier":
                        if (!Enum.TryParse<CaptainTierEnum>(value, true, out CaptainTierEnum tier) || !Enum.IsDefined(typeof(CaptainTierEnum), tier))
                        {
                            error = "tier must be Economy, Standard or Premium";
                            return false;
                        }
                        rung.MinimumTier = tier;
                        break;
                    default:
                        error = "unknown key '" + key + "' (use runtime, model, endpoint, tier)";
                        return false;
                }

                any = true;
            }

            if (!any) error = "a rung needs at least one of runtime, model, endpoint or tier";
            return any;
        }

        /// <summary>
        /// Render a matcher as a short spec.
        /// </summary>
        /// <param name="rung">Matcher to render.</param>
        /// <returns>The spec, or "any captain" when no field is set.</returns>
        /// <exception cref="ArgumentNullException">When rung is null.</exception>
        public static string Describe(PersonaCaptainPriority rung)
        {
            if (rung == null) throw new ArgumentNullException(nameof(rung));
            List<string> parts = new List<string>();
            if (rung.Runtime.HasValue) parts.Add("runtime=" + rung.Runtime.Value);
            if (!String.IsNullOrEmpty(rung.Model)) parts.Add("model=" + rung.Model);
            if (!String.IsNullOrEmpty(rung.Endpoint)) parts.Add("endpoint=" + rung.Endpoint);
            if (rung.MinimumTier.HasValue) parts.Add("tier=" + rung.MinimumTier.Value);
            return parts.Count == 0 ? "any captain" : String.Join(",", parts);
        }
    }
}
