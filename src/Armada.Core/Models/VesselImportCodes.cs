namespace Armada.Core.Models
{
    /// <summary>
    /// Stable codes used in vessel import hints and item outcome reasons. Clients localize by code.
    /// </summary>
    public static class VesselImportCodes
    {
        /// <summary>
        /// Hint: no requested path is visible to the Admiral (for example a containerized Admiral without a mount).
        /// </summary>
        public const string PathNotVisibleToAdmiral = "PathNotVisibleToAdmiral";

        /// <summary>
        /// Hint: discovery stopped at the candidate cap.
        /// </summary>
        public const string CandidateLimitReached = "CandidateLimitReached";

        /// <summary>
        /// Outcome reason: a vessel with the same working directory or remote URL already exists.
        /// </summary>
        public const string VesselAlreadyExists = "VesselAlreadyExists";

        /// <summary>
        /// Outcome reason: the operator did not select the candidate.
        /// </summary>
        public const string NotSelected = "NotSelected";

        /// <summary>
        /// Outcome reason: the candidate's classification cannot be imported (NotFound, NotGit, AccessDenied,
        /// ArmadaManaged).
        /// </summary>
        public const string NotImportable = "NotImportable";

        /// <summary>
        /// Outcome reason: the directory no longer exists at import time.
        /// </summary>
        public const string PathMissing = "PathMissing";

        /// <summary>
        /// Outcome reason: creating the vessel failed; see the outcome message.
        /// </summary>
        public const string CreateFailed = "CreateFailed";

        /// <summary>
        /// Outcome reason: the background import was cancelled before this candidate was processed.
        /// </summary>
        public const string Cancelled = "Cancelled";

        /// <summary>
        /// Error: a requested path is outside the allowed import roots (HTTP 403).
        /// </summary>
        public const string PathNotAllowed = "PathNotAllowed";

        /// <summary>
        /// Error: discovery on a Harbor was requested but is not supported yet (HTTP 400).
        /// </summary>
        public const string HarborNotSupported = "HarborNotSupported";

        /// <summary>
        /// Error: the request is missing required input or contains an invalid value (HTTP 400).
        /// </summary>
        public const string InvalidRequest = "InvalidRequest";

        /// <summary>
        /// Error: the batch does not exist in the caller's tenant (HTTP 404).
        /// </summary>
        public const string BatchNotFound = "BatchNotFound";

        /// <summary>
        /// Error: the batch is already being imported (HTTP 409).
        /// </summary>
        public const string BatchBusy = "BatchBusy";

        /// <summary>
        /// Error: the browsed directory does not exist (HTTP 404).
        /// </summary>
        public const string DirectoryNotFound = "DirectoryNotFound";

        /// <summary>
        /// Error: categorization was enabled without a categorization captain (HTTP 400).
        /// </summary>
        public const string CategorizationCaptainRequired = "CategorizationCaptainRequired";

        /// <summary>
        /// Error: the categorization captain does not exist in the caller's tenant (HTTP 400).
        /// </summary>
        public const string CategorizationCaptainNotFound = "CategorizationCaptainNotFound";
    }
}
