namespace Armada.Server.Mcp
{
    /// <summary>
    /// Arguments for approving a deployment: the deployment identifier and an optional comment.
    /// </summary>
    public class DeploymentApprovalArgs
    {
        /// <summary>
        /// Deployment identifier.
        /// </summary>
        public string DeploymentId { get; set; } = string.Empty;

        /// <summary>
        /// Optional approval comment.
        /// </summary>
        public string? Comment { get; set; } = null;
    }
}
