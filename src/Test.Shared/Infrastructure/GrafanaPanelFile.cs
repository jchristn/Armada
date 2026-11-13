namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>One Grafana panel with its query targets.</summary>
    public sealed class GrafanaPanelFile
    {
        /// <summary>Panel title.</summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        /// <summary>Panel description shown as the info tooltip.</summary>
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        /// <summary>Query targets.</summary>
        [JsonPropertyName("targets")]
        public List<GrafanaTargetFile> Targets { get; set; } = new List<GrafanaTargetFile>();
    }
}
