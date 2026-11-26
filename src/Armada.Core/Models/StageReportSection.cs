namespace Armada.Core.Models
{
    using System.ComponentModel;
    using System.ComponentModel.DataAnnotations;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One named report section of a submitted result.
    /// </summary>
    public class StageReportSection
    {
        /// <summary>
        /// Section name without hashes, for example "Coverage Added".
        /// </summary>
        [JsonPropertyName("heading")]
        [Required]
        [Description("Section name without the leading hashes, for example Coverage Added.")]
        public string Heading { get; set; } = "";

        /// <summary>
        /// Section text.
        /// </summary>
        [JsonPropertyName("body")]
        [Required]
        [Description("Section text.")]
        public string Body { get; set; } = "";
    }
}
