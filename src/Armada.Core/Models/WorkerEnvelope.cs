namespace Armada.Core.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Worker eligibility envelope (docs/MODEL_HETEROGENEITY.md, 3.3): the size a Worker mission is sliced for and the
    /// model profile a captain needs to run it. A vessel carries a default; a mission may carry a tighter one, never a
    /// looser one. Every field is optional; an envelope with no field set is the same as no envelope.
    /// </summary>
    public class WorkerEnvelope
    {
        #region Public-Members

        /// <summary>
        /// Most files one Worker mission should change (1 to 1000), or null for no limit. Guides the Architect's
        /// slicing and the Judge; model profiles record no file capacity, so it does not change eligibility.
        /// </summary>
        [JsonPropertyName("maxFiles")]
        public int? MaxFiles { get; set; } = null;

        /// <summary>
        /// Most steps one Worker mission should take (1 to 1000), or null for no limit. Guides the Architect's slicing
        /// and the Judge; model profiles record no step capacity, so it does not change eligibility.
        /// </summary>
        [JsonPropertyName("maxSteps")]
        public int? MaxSteps { get; set; } = null;

        /// <summary>
        /// Smallest served context, in tokens, a captain's model profile must record (1 to 100,000,000), or null for
        /// no minimum. A profile with no served context is not eligible when this is set.
        /// </summary>
        [JsonPropertyName("minServedContextTokens")]
        public int? MinServedContextTokens { get; set; } = null;

        /// <summary>
        /// Capabilities a captain's model profile must record as available: <c>tools</c> (tool calls) and
        /// <c>mcp</c> (MCP tools). Empty for none. A profile that does not record a required capability is not eligible.
        /// </summary>
        [JsonPropertyName("requiredTools")]
        public List<string> RequiredTools
        {
            get => _RequiredTools;
            set => _RequiredTools = value ?? new List<string>();
        }

        #endregion

        #region Private-Members

        private List<string> _RequiredTools = new List<string>();

        #endregion
    }
}
