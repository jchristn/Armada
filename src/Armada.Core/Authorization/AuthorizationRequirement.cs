namespace Armada.Core.Authorization
{
    using System;

    /// <summary>
    /// The explicit authorization requirement declared for one REST route (method plus route template) or one MCP
    /// tool: the resource type and operation it performs, and the permission level a caller needs.
    /// </summary>
    public sealed class AuthorizationRequirement
    {
        #region Public-Members

        /// <summary>
        /// Resource type the route or tool acts on (for example Fleet, Mission, Server).
        /// </summary>
        public string ResourceType { get; }

        /// <summary>
        /// Operation performed on the resource type.
        /// </summary>
        public ResourceOperationEnum Operation { get; }

        /// <summary>
        /// Permission level the caller must hold.
        /// </summary>
        public PermissionLevel Level { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="resourceType">Resource type.</param>
        /// <param name="operation">Operation.</param>
        /// <param name="level">Required permission level.</param>
        /// <exception cref="ArgumentNullException">Thrown when the resource type is null or empty.</exception>
        public AuthorizationRequirement(string resourceType, ResourceOperationEnum operation, PermissionLevel level)
        {
            if (String.IsNullOrEmpty(resourceType)) throw new ArgumentNullException(nameof(resourceType));
            ResourceType = resourceType;
            Operation = operation;
            Level = level;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Human-readable form, for example "Fleet:Read (Authenticated)".
        /// </summary>
        /// <returns>String.</returns>
        public override string ToString()
        {
            return ResourceType + ":" + Operation + " (" + Level + ")";
        }

        #endregion
    }
}
