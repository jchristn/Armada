namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Provider types supported for workflow input references.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum WorkflowInputReferenceProviderEnum
    {
        /// <summary>
        /// Environment variable available to the Armada process.
        /// </summary>
        EnvironmentVariable = 0,

        /// <summary>
        /// File path that must exist on disk.
        /// </summary>
        FilePath = 1,

        /// <summary>
        /// Directory path that must exist on disk.
        /// </summary>
        DirectoryPath = 2,

        /// <summary>
        /// Secret identifier resolved through AWS Secrets Manager.
        /// </summary>
        AwsSecretsManager = 3,

        /// <summary>
        /// Secret identifier resolved through Azure Key Vault.
        /// </summary>
        AzureKeyVaultSecret = 4,

        /// <summary>
        /// Secret path resolved through HashiCorp Vault.
        /// </summary>
        HashiCorpVault = 5,

        /// <summary>
        /// Secret reference resolved through 1Password.
        /// </summary>
        OnePassword = 6
    }
}
