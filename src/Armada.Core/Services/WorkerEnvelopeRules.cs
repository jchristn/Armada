namespace Armada.Core.Services
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// Rules for the Worker eligibility envelope (docs/MODEL_HETEROGENEITY.md, 3.3): validation, tightening a vessel
    /// default for one mission (never loosening it), the generated description given to the Architect and the Judge,
    /// and whether a captain's model profile meets an envelope.
    /// </summary>
    public static class WorkerEnvelopeRules
    {
        #region Public-Members

        /// <summary>
        /// Event type recorded for each envelope eligibility decision on a Worker mission.
        /// </summary>
        public const string EventType = "mission.worker_envelope";

        /// <summary>
        /// Event type recorded when the Architect's envelope for a mission was looser than the vessel default (clamped)
        /// or invalid (ignored).
        /// </summary>
        public const string ClampedEventType = "mission.worker_envelope_clamped";

        /// <summary>
        /// Required tool: the model can call tools as served (<see cref="ModelProfileCapabilities.ToolsAvailable"/>).
        /// </summary>
        public const string ToolCalls = "tools";

        /// <summary>
        /// Required tool: the model can use MCP tools as served (<see cref="ModelProfileCapabilities.McpAvailable"/>).
        /// </summary>
        public const string Mcp = "mcp";

        /// <summary>
        /// Every required tool name an envelope may list.
        /// </summary>
        public static readonly IReadOnlyList<string> KnownTools = new List<string> { ToolCalls, Mcp };

        /// <summary>
        /// Largest MaxFiles or MaxSteps value.
        /// </summary>
        public const int MaxCount = 1000;

        /// <summary>
        /// Largest MinServedContextTokens value.
        /// </summary>
        public const int MaxContextTokens = 100000000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether an envelope sets no field (the same as no envelope).
        /// </summary>
        /// <param name="envelope">Envelope, or null.</param>
        /// <returns>True when the envelope is null or sets nothing.</returns>
        public static bool IsEmpty(WorkerEnvelope? envelope)
        {
            return envelope == null
                || (!envelope.MaxFiles.HasValue && !envelope.MaxSteps.HasValue && !envelope.MinServedContextTokens.HasValue
                    && envelope.RequiredTools.All(t => String.IsNullOrWhiteSpace(t)));
        }

        /// <summary>
        /// Validate an envelope and return a normalized copy (tool names lower-cased, de-duplicated and sorted), or null
        /// when it sets nothing.
        /// </summary>
        /// <param name="envelope">Envelope, or null.</param>
        /// <returns>Normalized copy, or null.</returns>
        /// <exception cref="ArgumentException">A count is out of range or a tool name is unknown.</exception>
        public static WorkerEnvelope? Normalize(WorkerEnvelope? envelope)
        {
            if (IsEmpty(envelope)) return null;
            WorkerEnvelope source = envelope!;
            if (source.MaxFiles.HasValue && (source.MaxFiles.Value < 1 || source.MaxFiles.Value > MaxCount))
                throw new ArgumentException("workerEnvelope.maxFiles must be between 1 and " + MaxCount + ".");
            if (source.MaxSteps.HasValue && (source.MaxSteps.Value < 1 || source.MaxSteps.Value > MaxCount))
                throw new ArgumentException("workerEnvelope.maxSteps must be between 1 and " + MaxCount + ".");
            if (source.MinServedContextTokens.HasValue && (source.MinServedContextTokens.Value < 1 || source.MinServedContextTokens.Value > MaxContextTokens))
                throw new ArgumentException("workerEnvelope.minServedContextTokens must be between 1 and " + MaxContextTokens + ".");

            SortedSet<string> tools = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string raw in source.RequiredTools)
            {
                if (String.IsNullOrWhiteSpace(raw)) continue;
                string tool = raw.Trim().ToLowerInvariant();
                if (!KnownTools.Contains(tool))
                    throw new ArgumentException("workerEnvelope.requiredTools has unknown tool '" + raw.Trim() + "'; known tools: " + String.Join(", ", KnownTools) + ".");
                tools.Add(tool);
            }

            return new WorkerEnvelope
            {
                MaxFiles = source.MaxFiles,
                MaxSteps = source.MaxSteps,
                MinServedContextTokens = source.MinServedContextTokens,
                RequiredTools = tools.ToList()
            };
        }

        /// <summary>
        /// Combine two envelopes into the tightest of both: fewer files and steps, the larger context minimum and every
        /// required tool. Either may be null. Neither input is validated; normalize them first.
        /// </summary>
        /// <param name="first">First envelope, or null.</param>
        /// <param name="second">Second envelope, or null.</param>
        /// <returns>The combined envelope, or null when both set nothing.</returns>
        public static WorkerEnvelope? Tightest(WorkerEnvelope? first, WorkerEnvelope? second)
        {
            if (IsEmpty(first)) return IsEmpty(second) ? null : Copy(second!);
            if (IsEmpty(second)) return Copy(first!);
            SortedSet<string> tools = new SortedSet<string>(first!.RequiredTools.Concat(second!.RequiredTools)
                .Where(t => !String.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()), StringComparer.Ordinal);
            return new WorkerEnvelope
            {
                MaxFiles = MinOf(first.MaxFiles, second.MaxFiles),
                MaxSteps = MinOf(first.MaxSteps, second.MaxSteps),
                MinServedContextTokens = MaxOf(first.MinServedContextTokens, second.MinServedContextTokens),
                RequiredTools = tools.ToList()
            };
        }

        /// <summary>
        /// Every way a requested envelope is looser than a baseline (the vessel default): more files or steps, a smaller
        /// context minimum, a limit the baseline sets but the request drops, or a required tool it leaves out.
        /// </summary>
        /// <param name="baseline">Baseline envelope, or null (nothing can be looser than no envelope).</param>
        /// <param name="requested">Requested envelope, or null.</param>
        /// <returns>One English reason per loosened field; empty when the request only tightens.</returns>
        public static List<string> Loosenings(WorkerEnvelope? baseline, WorkerEnvelope? requested)
        {
            List<string> reasons = new List<string>();
            if (IsEmpty(baseline)) return reasons;
            WorkerEnvelope request = requested ?? new WorkerEnvelope();
            if (baseline!.MaxFiles.HasValue && (!request.MaxFiles.HasValue || request.MaxFiles.Value > baseline.MaxFiles.Value))
                reasons.Add("maxFiles " + Show(request.MaxFiles) + " is looser than the vessel default " + baseline.MaxFiles.Value);
            if (baseline.MaxSteps.HasValue && (!request.MaxSteps.HasValue || request.MaxSteps.Value > baseline.MaxSteps.Value))
                reasons.Add("maxSteps " + Show(request.MaxSteps) + " is looser than the vessel default " + baseline.MaxSteps.Value);
            if (baseline.MinServedContextTokens.HasValue && (!request.MinServedContextTokens.HasValue || request.MinServedContextTokens.Value < baseline.MinServedContextTokens.Value))
                reasons.Add("minServedContextTokens " + Show(request.MinServedContextTokens) + " is looser than the vessel default " + baseline.MinServedContextTokens.Value);
            HashSet<string> requestedTools = new HashSet<string>(request.RequiredTools.Where(t => !String.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant()), StringComparer.Ordinal);
            foreach (string tool in baseline.RequiredTools)
            {
                if (String.IsNullOrWhiteSpace(tool)) continue;
                if (!requestedTools.Contains(tool.Trim().ToLowerInvariant()))
                    reasons.Add("requiredTools drops '" + tool.Trim().ToLowerInvariant() + "' that the vessel default requires");
            }
            return reasons;
        }

        /// <summary>
        /// Tighten a vessel default for one mission. The request is validated, then clamped so it is never looser than the
        /// default; each clamped field is reported.
        /// </summary>
        /// <param name="vesselDefault">Vessel default, or null.</param>
        /// <param name="requested">Requested mission envelope, or null to use the default.</param>
        /// <param name="clamped">One English reason per field that was looser than the default and was clamped.</param>
        /// <returns>The mission's envelope (the tightest of both), or null when neither sets anything.</returns>
        /// <exception cref="ArgumentException">The request has an out-of-range count or an unknown tool.</exception>
        public static WorkerEnvelope? Tighten(WorkerEnvelope? vesselDefault, WorkerEnvelope? requested, out List<string> clamped)
        {
            WorkerEnvelope? baseline = Normalize(vesselDefault);
            WorkerEnvelope? request = Normalize(requested);
            clamped = request == null ? new List<string>() : Loosenings(baseline, request);
            return Tightest(baseline, request);
        }

        /// <summary>
        /// Validate a mission's own envelope against its vessel default and reject a loosened one. Used where a person or
        /// client sets the envelope (dispatch, mission update); the Architect's plan is clamped instead
        /// (<see cref="Tighten"/>).
        /// </summary>
        /// <param name="vesselDefault">Vessel default, or null.</param>
        /// <param name="requested">Requested mission envelope, or null.</param>
        /// <returns>The normalized request, or null when it sets nothing.</returns>
        /// <exception cref="ArgumentException">The request is invalid or looser than the vessel default.</exception>
        public static WorkerEnvelope? ValidateMissionEnvelope(WorkerEnvelope? vesselDefault, WorkerEnvelope? requested)
        {
            WorkerEnvelope? request = Normalize(requested);
            if (request == null) return null;
            WorkerEnvelope? baseline;
            try
            {
                baseline = Normalize(vesselDefault);
            }
            catch (ArgumentException)
            {
                baseline = null;
            }
            List<string> loosened = Loosenings(baseline, request);
            if (loosened.Count > 0)
                throw new ArgumentException("workerEnvelope may only tighten the vessel's default Worker envelope, never loosen it: " + String.Join("; ", loosened) + ".");
            return request;
        }

        /// <summary>
        /// Whether a mission's own envelope may still change: it is Pending, or Assigned to a captain that has not
        /// started it (no start time, no process). Dispatch reads the envelope when it assigns the Worker, so changing
        /// it after the captain started would not affect the running work.
        /// </summary>
        /// <param name="mission">Mission.</param>
        /// <returns>True when the envelope may change.</returns>
        /// <exception cref="ArgumentNullException">The mission is null.</exception>
        public static bool CanChangeMissionEnvelope(Mission mission)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (mission.Status == MissionStatusEnum.Pending) return true;
            return mission.Status == MissionStatusEnum.Assigned && !mission.StartedUtc.HasValue && !mission.ProcessId.HasValue;
        }

        /// <summary>
        /// Apply a mission update's envelope (REST PUT, MCP and WebSocket <c>update_mission</c>). The same value as the
        /// stored one is a no-op on any status. A different value is validated like dispatch
        /// (<see cref="ValidateMissionEnvelope"/>: invalid or looser than the vessel default is rejected) and is only
        /// accepted while <see cref="CanChangeMissionEnvelope"/>; an envelope with no field set clears it.
        /// </summary>
        /// <param name="mission">Stored mission (its status and current envelope).</param>
        /// <param name="vesselDefault">Default envelope of the mission's vessel after the update, or null.</param>
        /// <param name="requested">Requested envelope; an empty one clears the mission's envelope.</param>
        /// <returns>The envelope to store, or null for none.</returns>
        /// <exception cref="ArgumentNullException">The mission or request is null.</exception>
        /// <exception cref="ArgumentException">The request is invalid or looser than the vessel default.</exception>
        /// <exception cref="InvalidOperationException">The envelope would change on a mission that has started.</exception>
        public static WorkerEnvelope? ValidateMissionEnvelopeUpdate(Mission mission, WorkerEnvelope? vesselDefault, WorkerEnvelope requested)
        {
            if (mission == null) throw new ArgumentNullException(nameof(mission));
            if (requested == null) throw new ArgumentNullException(nameof(requested));
            WorkerEnvelope? normalized = Normalize(requested);
            if (String.Equals(ToJson(normalized), ToJson(SafeNormalize(mission.WorkerEnvelope)), StringComparison.Ordinal))
                return mission.WorkerEnvelope;
            if (!CanChangeMissionEnvelope(mission))
                throw new InvalidOperationException("workerEnvelope can only change while the mission is Pending or Assigned and not started; mission "
                    + mission.Id + " is " + mission.Status + ".");
            return ValidateMissionEnvelope(vesselDefault, normalized);
        }

        /// <summary>
        /// Short generated description of an envelope for the Architect and Judge prompts, or an empty string when the
        /// envelope sets nothing.
        /// </summary>
        /// <param name="envelope">Envelope, or null.</param>
        /// <returns>One ASCII paragraph, or empty.</returns>
        public static string Describe(WorkerEnvelope? envelope)
        {
            if (IsEmpty(envelope)) return "";
            WorkerEnvelope e = envelope!;
            List<string> limits = new List<string>();
            if (e.MaxFiles.HasValue) limits.Add("at most " + e.MaxFiles.Value + (e.MaxFiles.Value == 1 ? " file" : " files") + " changed");
            if (e.MaxSteps.HasValue) limits.Add("at most " + e.MaxSteps.Value + (e.MaxSteps.Value == 1 ? " step" : " steps"));
            if (e.MinServedContextTokens.HasValue) limits.Add("Worker served context at least " + e.MinServedContextTokens.Value.ToString("N0", CultureInfo.InvariantCulture) + " tokens");
            List<string> tools = e.RequiredTools.Where(t => !String.IsNullOrWhiteSpace(t)).ToList();
            if (tools.Count > 0) limits.Add("Worker must have " + String.Join(" and ", tools.Select(ToolLabel)));
            StringBuilder sb = new StringBuilder();
            sb.Append("Worker eligibility envelope: ").Append(String.Join("; ", limits)).Append('.');
            if (e.MaxFiles.HasValue || e.MaxSteps.HasValue)
                sb.Append(" Each Worker mission must fit these limits; a change that needs more files or steps is split into more missions.");
            if (e.MinServedContextTokens.HasValue || tools.Count > 0)
                sb.Append(" Only captains whose model profile meets the envelope run the Worker stage.");
            return sb.ToString();
        }

        /// <summary>
        /// The envelope that applies to a mission: its own envelope tightened onto the vessel default. Invalid stored
        /// values are ignored rather than thrown.
        /// </summary>
        /// <param name="vesselDefault">Vessel default, or null.</param>
        /// <param name="missionEnvelopes">The mission's own envelope and, for a later stage, the Worker stage's.</param>
        /// <returns>The effective envelope, or null when nothing is set.</returns>
        public static WorkerEnvelope? Effective(WorkerEnvelope? vesselDefault, params WorkerEnvelope?[] missionEnvelopes)
        {
            WorkerEnvelope? result = SafeNormalize(vesselDefault);
            if (missionEnvelopes != null)
            {
                foreach (WorkerEnvelope? envelope in missionEnvelopes)
                    result = Tightest(result, SafeNormalize(envelope));
            }
            return result;
        }

        /// <summary>
        /// The <c>{WorkerEnvelope}</c> prompt placeholder value for a persona: empty when no envelope is set, otherwise a
        /// leading blank line, a heading, the generated description and what the persona does with it (the Architect
        /// slices for it; the Judge names the exceeded limit so the rework reader can act on it).
        /// </summary>
        /// <param name="envelope">Effective envelope, or null.</param>
        /// <param name="persona">Persona the prompt is for, or null for Worker.</param>
        /// <returns>Placeholder value.</returns>
        public static string PromptSection(WorkerEnvelope? envelope, string? persona)
        {
            string description = Describe(envelope);
            if (description.Length == 0) return "";
            string guidance;
            if (PersonaCatalog.Matches(persona, PersonaCatalog.Architect))
                guidance = " Slice every Worker mission to fit it. You may tighten it for one mission with a `workerEnvelope` object in the plan "
                    + "(maxFiles, maxSteps, minServedContextTokens, requiredTools), never loosen it; a looser value is clamped to the vessel default.";
            else if (PersonaCatalog.Matches(persona, PersonaCatalog.Judge))
                guidance = " When the Worker's change exceeds a limit, say which limit and by how much in your finding, and how the rework "
                    + "fits inside it (for example which files to leave for a separate mission), so the rework stays within the envelope.";
            else
                guidance = " Keep the change within these limits; report BLOCKED if the work cannot fit them.";
            return "\n\n## Worker Eligibility Envelope\n" + description + guidance;
        }

        /// <summary>
        /// Make sure an Architect or Judge prompt carries the envelope section. A customized template written before
        /// the <c>{WorkerEnvelope}</c> placeholder existed renders without it; the rendered section is then appended
        /// after the prompt (the stored template is never rewritten). Other personas, an empty section, and a prompt
        /// that already contains the section are returned unchanged.
        /// </summary>
        /// <param name="prompt">Rendered persona prompt.</param>
        /// <param name="persona">Mission persona.</param>
        /// <param name="section">Rendered <c>{WorkerEnvelope}</c> value (<see cref="PromptSection"/>), or null.</param>
        /// <param name="appended">True when the section was appended.</param>
        /// <returns>The prompt, with the section appended when it was missing.</returns>
        public static string EnsurePromptSection(string prompt, string? persona, string? section, out bool appended)
        {
            appended = false;
            string text = prompt ?? "";
            if (String.IsNullOrWhiteSpace(section)) return text;
            if (!PersonaCatalog.Matches(persona, PersonaCatalog.Architect) && !PersonaCatalog.Matches(persona, PersonaCatalog.Judge)) return text;
            if (text.Contains(section.Trim(), StringComparison.Ordinal)) return text;
            appended = true;
            return text.TrimEnd() + section;
        }

        /// <summary>
        /// Why a captain may not run a Worker mission under an envelope, or null when its model profile meets it. A
        /// captain with no profile, or a profile that does not record a field the envelope needs, is not eligible. File
        /// and step limits never exclude a captain (profiles record no capacity for them).
        /// </summary>
        /// <param name="envelope">Effective envelope, or null.</param>
        /// <param name="captain">Captain.</param>
        /// <param name="profile">The captain's model profile, or null when it has none or it could not be read.</param>
        /// <returns>A short English reason, or null when eligible.</returns>
        /// <exception cref="ArgumentNullException">The captain is null.</exception>
        public static string? IneligibilityReason(WorkerEnvelope? envelope, Captain captain, ModelProfile? profile)
        {
            if (captain == null) throw new ArgumentNullException(nameof(captain));
            if (!ConstrainsCaptains(envelope)) return null;
            if (String.IsNullOrEmpty(captain.ModelProfileId)) return "no model profile (the Worker envelope needs one)";
            if (profile == null) return "model profile " + captain.ModelProfileId + " not found";
            WorkerEnvelope e = envelope!;
            if (e.MinServedContextTokens.HasValue)
            {
                int? served = profile.Identity.ServedContextTokens;
                if (!served.HasValue) return "model profile records no served context (the Worker envelope needs at least " + e.MinServedContextTokens.Value + " tokens)";
                if (served.Value < e.MinServedContextTokens.Value) return "served context " + served.Value + " tokens is below the Worker envelope minimum " + e.MinServedContextTokens.Value;
            }
            foreach (string raw in e.RequiredTools)
            {
                if (String.IsNullOrWhiteSpace(raw)) continue;
                string tool = raw.Trim().ToLowerInvariant();
                bool? available = tool == ToolCalls ? profile.Capabilities.ToolsAvailable
                    : tool == Mcp ? profile.Capabilities.McpAvailable
                    : null;
                if (!available.HasValue) return "model profile does not record " + ToolLabel(tool) + " (required by the Worker envelope)";
                if (!available.Value) return "model profile has no " + ToolLabel(tool) + " (required by the Worker envelope)";
            }
            return null;
        }

        /// <summary>
        /// Whether an envelope can exclude a captain (it sets a served context minimum or required tools).
        /// </summary>
        /// <param name="envelope">Envelope, or null.</param>
        /// <returns>True when the envelope constrains captains.</returns>
        public static bool ConstrainsCaptains(WorkerEnvelope? envelope)
        {
            return envelope != null
                && (envelope.MinServedContextTokens.HasValue || envelope.RequiredTools.Any(t => !String.IsNullOrWhiteSpace(t)));
        }

        /// <summary>
        /// Serialize an envelope for storage, or null when it sets nothing.
        /// </summary>
        /// <param name="envelope">Envelope, or null.</param>
        /// <returns>JSON, or null.</returns>
        public static string? ToJson(WorkerEnvelope? envelope)
        {
            return IsEmpty(envelope) ? null : JsonSerializer.Serialize(envelope);
        }

        /// <summary>
        /// Read a stored envelope, or null when the value is empty or unreadable.
        /// </summary>
        /// <param name="json">Stored JSON, or null.</param>
        /// <returns>Envelope, or null.</returns>
        public static WorkerEnvelope? FromJson(string? json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            try
            {
                WorkerEnvelope? envelope = JsonSerializer.Deserialize<WorkerEnvelope>(json);
                return IsEmpty(envelope) ? null : envelope;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        #endregion

        #region Private-Methods

        private static WorkerEnvelope? SafeNormalize(WorkerEnvelope? envelope)
        {
            try
            {
                return Normalize(envelope);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static WorkerEnvelope Copy(WorkerEnvelope source)
        {
            return new WorkerEnvelope
            {
                MaxFiles = source.MaxFiles,
                MaxSteps = source.MaxSteps,
                MinServedContextTokens = source.MinServedContextTokens,
                RequiredTools = source.RequiredTools.Where(t => !String.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant())
                    .Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList()
            };
        }

        private static int? MinOf(int? a, int? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;
            return Math.Min(a.Value, b.Value);
        }

        private static int? MaxOf(int? a, int? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;
            return Math.Max(a.Value, b.Value);
        }

        private static string Show(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "(no limit)";
        }

        private static string ToolLabel(string tool)
        {
            return tool == ToolCalls ? "tool calls" : tool == Mcp ? "MCP tools" : tool;
        }

        #endregion
    }
}
