namespace Armada.Core.Services
{
    using System;

    /// <summary>A structured policy-update conflict distinct from validation and persistence failures.</summary>
    public sealed class UsageSelectionConfigurationException : InvalidOperationException
    {
        /// <summary>Stable localizable reason code.</summary>
        public string Code { get; }
        /// <summary>Create a non-secret configuration conflict.</summary>
        public UsageSelectionConfigurationException(string code, string message) : base(message) { Code = code; }
    }
}
