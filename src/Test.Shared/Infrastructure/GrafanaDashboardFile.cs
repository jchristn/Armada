namespace Test.Shared.Infrastructure
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>The parts of a provisioned Grafana dashboard file that contract tests check.</summary>
    public sealed class GrafanaDashboardFile
    {
        /// <summary>Dashboard UID.</summary>
        [JsonPropertyName("uid")]
        public string Uid { get; set; } = "";

        /// <summary>Dashboard title.</summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        /// <summary>Top-level panels.</summary>
        [JsonPropertyName("panels")]
        public List<GrafanaPanelFile> Panels { get; set; } = new List<GrafanaPanelFile>();
    }
}
