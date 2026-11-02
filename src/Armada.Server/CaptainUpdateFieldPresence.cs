namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Armada.Core.Enums;

    /// <summary>
    /// Records which editable captain fields a captain update body carries. Deserialize the same request body into
    /// this class (with the same serializer options as the <see cref="Armada.Core.Models.Captain"/> body): a setter
    /// runs only for a property present in the JSON, including one sent as null, so an omitted field can be told
    /// apart from a field cleared on purpose. <see cref="EntityUpdateMerger.MergeCaptain(Armada.Core.Models.Captain, Armada.Core.Models.Captain, CaptainUpdateFieldPresence)"/>
    /// keeps the stored value of every omitted field.
    /// </summary>
    public class CaptainUpdateFieldPresence
    {
        #region Public-Members

        /// <summary>Active eligibility (presence only).</summary>
        public bool? Active { get => null; set => Mark(nameof(Active)); }

        /// <summary>Captain name (presence only).</summary>
        public string? Name { get => null; set => Mark(nameof(Name)); }

        /// <summary>Runtime (presence only).</summary>
        public AgentRuntimeEnum? Runtime { get => null; set => Mark(nameof(Runtime)); }

        /// <summary>Model override (presence only).</summary>
        public string? Model { get => null; set => Mark(nameof(Model)); }

        /// <summary>Inference endpoint id (presence only).</summary>
        public string? ModelEndpointId { get => null; set => Mark(nameof(ModelEndpointId)); }

        /// <summary>System instructions (presence only).</summary>
        public string? SystemInstructions { get => null; set => Mark(nameof(SystemInstructions)); }

        /// <summary>Allowed personas (presence only).</summary>
        public string? AllowedPersonas { get => null; set => Mark(nameof(AllowedPersonas)); }

        /// <summary>Preferred persona (presence only).</summary>
        public string? PreferredPersona { get => null; set => Mark(nameof(PreferredPersona)); }

        /// <summary>Reasoning effort (presence only).</summary>
        public ReasoningEffortEnum? ReasoningEffort { get => null; set => Mark(nameof(ReasoningEffort)); }

        /// <summary>Capability tier (presence only).</summary>
        public CaptainTierEnum? Tier { get => null; set => Mark(nameof(Tier)); }

        /// <summary>Runtime options JSON (presence only).</summary>
        public string? RuntimeOptionsJson { get => null; set => Mark(nameof(RuntimeOptionsJson)); }

        /// <summary>Usage capture enabled switch (presence only; any JSON shape counts as present).</summary>
        public object? UsageCaptureEnabled { get => null; set => Mark(nameof(UsageCaptureEnabled)); }

        /// <summary>Usage capture interval in seconds (presence only).</summary>
        public object? UsageCaptureIntervalSeconds { get => null; set => Mark(nameof(UsageCaptureIntervalSeconds)); }

        /// <summary>Usage capture mode (presence only).</summary>
        public object? UsageCaptureMode { get => null; set => Mark(nameof(UsageCaptureMode)); }

        /// <summary>Credential Harbor selection (presence only).</summary>
        public object? UsageCaptureHarborId { get => null; set => Mark(nameof(UsageCaptureHarborId)); }
        /// <summary>Whether the usage command was supplied.</summary>
        public object? UsageCaptureCommand { get => null; set => Mark(nameof(UsageCaptureCommand)); }

        #endregion

        #region Private-Members

        private readonly HashSet<string> _Present = new HashSet<string>(StringComparer.Ordinal);
        private bool _AllPresent = false;

        #endregion

        #region Public-Methods

        /// <summary>
        /// A presence record that reports every field as present (full-replacement semantics).
        /// </summary>
        /// <returns>The record.</returns>
        public static CaptainUpdateFieldPresence All()
        {
            CaptainUpdateFieldPresence presence = new CaptainUpdateFieldPresence();
            presence._AllPresent = true;
            return presence;
        }

        /// <summary>
        /// Read which captain fields a JSON body carries.
        /// </summary>
        /// <param name="json">Request body.</param>
        /// <param name="options">Serializer options used for the captain body.</param>
        /// <returns>The presence record; an empty or null body reports no fields.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> is null.</exception>
        public static CaptainUpdateFieldPresence FromJson(string? json, JsonSerializerOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (String.IsNullOrWhiteSpace(json)) return new CaptainUpdateFieldPresence();
            return JsonSerializer.Deserialize<CaptainUpdateFieldPresence>(json, options) ?? new CaptainUpdateFieldPresence();
        }

        /// <summary>
        /// Whether the body carried the named field.
        /// </summary>
        /// <param name="field">Captain property name, for example nameof(Captain.AllowedPersonas).</param>
        /// <returns>True when present.</returns>
        public bool Has(string field)
        {
            return _AllPresent || _Present.Contains(field);
        }

        #endregion

        #region Private-Methods

        private void Mark(string field)
        {
            _Present.Add(field);
        }

        #endregion
    }
}
