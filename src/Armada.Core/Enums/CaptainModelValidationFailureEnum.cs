namespace Armada.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Why a captain's model or runtime configuration failed validation on create or update.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CaptainModelValidationFailureEnum
    {
        /// <summary>
        /// An API-endpoint captain does not reference a model endpoint.
        /// </summary>
        EndpointRequired,

        /// <summary>
        /// The referenced model endpoint does not exist.
        /// </summary>
        EndpointNotFound,

        /// <summary>
        /// The referenced model endpoint is not an Inference endpoint.
        /// </summary>
        EndpointNotInference,

        /// <summary>
        /// The referenced model endpoint is disabled.
        /// </summary>
        EndpointDisabled,

        /// <summary>
        /// The runtime could not be created to validate the model.
        /// </summary>
        RuntimeUnavailable,

        /// <summary>
        /// The runtime rejected the model (it exited with an error).
        /// </summary>
        ModelRejected,

        /// <summary>
        /// The validation run did not finish in time.
        /// </summary>
        TimedOut,

        /// <summary>
        /// The captain's runtime options are not valid JSON.
        /// </summary>
        InvalidRuntimeOptions,

        /// <summary>
        /// A Mux captain has no named endpoint.
        /// </summary>
        NamedEndpointRequired,

        /// <summary>
        /// The Mux CLI reported an output contract version Armada does not support.
        /// </summary>
        UnsupportedContractVersion,

        /// <summary>
        /// The Mux endpoint probe failed.
        /// </summary>
        EndpointProbeFailed,

        /// <summary>
        /// The Mux endpoint is not tool-enabled.
        /// </summary>
        EndpointNotToolEnabled
    }
}
