namespace Armada.Core.Client
{
    using System;

    /// <summary>Stable code and field from an error envelope's <c>Data</c>, when the server sends them.</summary>
    public sealed class ApiErrorEnvelopeData
    {
        /// <summary>Stable machine-readable code, for example <c>RecoveryRevisionConflict</c>.</summary>
        public string? Code { get; set; }

        /// <summary>Request field the error is about, for example <c>UsageSelection.Mode</c>.</summary>
        public string? Field { get; set; }
    }
}
