namespace Armada.Runtimes.Usage
{
    using System.Text.Json.Serialization;
    /// <summary>Host-only OAuth fields. Never serialized into a probe result.</summary>
    internal class UsageOAuthCredentials
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }
        [JsonPropertyName("access_token")]
        public string? GoogleAccessToken { get; set; }
        public UsageOAuthCredentials? Token { get; set; }
        public UsageOAuthCredentials? ClaudeAiOauth { get; set; }
        public UsageOAuthCredentials? OauthAccount { get; set; }
        public string? AccountUuid { get; set; }
        public string? OrganizationUuid { get; set; }
        public string? Active { get; set; }
    }
}
