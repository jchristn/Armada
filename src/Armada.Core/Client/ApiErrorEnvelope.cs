namespace Armada.Core.Client
{
    using System;

    /// <summary>The parts of the REST error envelope (<c>Error</c>, <c>Message</c>, <c>Data</c>) a client shows to a person.</summary>
    public sealed class ApiErrorEnvelope
    {
        /// <summary>Server explanation, written for people.</summary>
        public string? Message { get; set; }

        /// <summary>Structured detail; only the stable code and field are read.</summary>
        public ApiErrorEnvelopeData? Data { get; set; }
    }
}
