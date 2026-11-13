namespace Test.Shared.Infrastructure
{
    using System.Text.Json.Serialization;

    /// <summary>One Grafana Prometheus query target.</summary>
    public sealed class GrafanaTargetFile
    {
        /// <summary>PromQL expression.</summary>
        [JsonPropertyName("expr")]
        public string Expr { get; set; } = "";
    }
}
