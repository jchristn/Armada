namespace Armada.Core.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Models;

    /// <summary>
    /// File operations in a vessel's checkout on a Harbor, carried out by the Harbor over its link (file requests with a
    /// root). The Harbor checks that the root is its own checkout of the vessel (or inside its docks folder) and applies
    /// the Workspace rules there; a failure comes back with a code and is raised here as the exception the same failure
    /// raises on the Admiral's disk.
    /// </summary>
    public class HarborCheckoutFiles : IVesselCheckoutFiles
    {
        #region Public-Members

        /// <summary>
        /// How long to wait for the Harbor to answer one request.
        /// </summary>
        public int TimeoutMs { get; set; } = 60000;

        #endregion

        #region Private-Members

        private readonly HarborConnectionManager _Manager;
        private readonly string _HarborId;
        private readonly string _Root;
        private readonly Vessel _Vessel;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="manager">Harbor connections.</param>
        /// <param name="harborId">Harbor identifier.</param>
        /// <param name="root">The checkout's path on the Harbor host.</param>
        /// <param name="vessel">The vessel (the Harbor checks the root is its checkout of it).</param>
        public HarborCheckoutFiles(HarborConnectionManager manager, string harborId, string root, Vessel vessel)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            if (String.IsNullOrWhiteSpace(root)) throw new ArgumentNullException(nameof(root));
            _HarborId = harborId;
            _Root = root;
            _Vessel = vessel ?? throw new ArgumentNullException(nameof(vessel));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<WorkspaceTreeResult> GetTreeAsync(string? path, CancellationToken token = default)
        {
            HarborFileResult result = await SendAsync(NewRequest(HarborFileOperationEnum.List, path), token).ConfigureAwait(false);
            return result.Tree ?? throw new InvalidOperationException(Describe() + " returned no listing.");
        }

        /// <inheritdoc />
        public async Task<WorkspaceFileResponse> GetFileAsync(string path, CancellationToken token = default)
        {
            HarborFileResult result = await SendAsync(NewRequest(HarborFileOperationEnum.ReadFile, path), token).ConfigureAwait(false);
            return result.File ?? throw new InvalidOperationException(Describe() + " returned no file.");
        }

        /// <inheritdoc />
        public async Task<WorkspaceSaveResult> SaveFileAsync(WorkspaceSaveRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));
            HarborFileRequest message = NewRequest(HarborFileOperationEnum.SaveFile, request.Path);
            message.Content = request.Content ?? String.Empty;
            message.ExpectedHash = request.ExpectedHash;
            HarborFileResult result = await SendAsync(message, token).ConfigureAwait(false);
            return result.Saved ?? throw new InvalidOperationException(Describe() + " returned no save result.");
        }

        /// <inheritdoc />
        public async Task<WorkspaceOperationResult> CreateDirectoryAsync(WorkspaceCreateDirectoryRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));
            HarborFileResult result = await SendAsync(NewRequest(HarborFileOperationEnum.CreateDirectory, request.Path), token).ConfigureAwait(false);
            return result.Entry ?? throw new InvalidOperationException(Describe() + " returned no result.");
        }

        /// <inheritdoc />
        public async Task<WorkspaceOperationResult> RenameAsync(WorkspaceRenameRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path) || String.IsNullOrWhiteSpace(request.NewPath))
                throw new ArgumentException("Path and NewPath are required.", nameof(request));
            HarborFileRequest message = NewRequest(HarborFileOperationEnum.Rename, request.Path);
            message.NewPath = request.NewPath;
            HarborFileResult result = await SendAsync(message, token).ConfigureAwait(false);
            return result.Entry ?? throw new InvalidOperationException(Describe() + " returned no result.");
        }

        /// <inheritdoc />
        public async Task<WorkspaceOperationResult> DeleteAsync(string path, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));
            HarborFileResult result = await SendAsync(NewRequest(HarborFileOperationEnum.Delete, path), token).ConfigureAwait(false);
            return result.Entry ?? throw new InvalidOperationException(Describe() + " returned no result.");
        }

        /// <inheritdoc />
        public async Task<WorkspaceSearchResult> SearchAsync(string query, int maxResults, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query is required.", nameof(query));
            HarborFileRequest message = NewRequest(HarborFileOperationEnum.Search, null);
            message.Query = query;
            message.MaxResults = maxResults;
            HarborFileResult result = await SendAsync(message, token).ConfigureAwait(false);
            return result.Search ?? throw new InvalidOperationException(Describe() + " returned no search result.");
        }

        /// <inheritdoc />
        public async Task<VesselCheckoutFileInfo> StatAsync(string path, CancellationToken token = default)
        {
            HarborFileResult result = await SendAsync(NewRequest(HarborFileOperationEnum.Stat, path), token).ConfigureAwait(false);
            return new VesselCheckoutFileInfo
            {
                Exists = result.Exists,
                IsDirectory = result.IsDirectory,
                SizeBytes = result.SizeBytes,
                LastWriteUtc = result.LastWriteUtc
            };
        }

        /// <inheritdoc />
        public async Task<string?> ReadTextAsync(string path, long maxBytes, CancellationToken token = default)
        {
            HarborFileRequest message = NewRequest(HarborFileOperationEnum.Read, path);
            message.MaxBytes = maxBytes;
            HarborFileResult result = await SendAsync(message, token).ConfigureAwait(false);
            if (!result.Exists || result.Truncated) return null;
            return result.Content ?? String.Empty;
        }

        /// <summary>
        /// Raise the exception a failed file result stands for (the type the same failure has on the Admiral's disk).
        /// </summary>
        /// <param name="result">The failed result.</param>
        /// <param name="harbor">The Harbor, for the message.</param>
        /// <returns>The exception.</returns>
        public static Exception ToException(HarborFileResult result, string harbor)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            string message = String.IsNullOrWhiteSpace(result.Message) ? "Harbor " + harbor + " could not complete the request." : result.Message!;
            switch (result.ErrorCode)
            {
                case HarborFileErrorCodeEnum.Refused:
                    return new UnauthorizedAccessException(message);
                case HarborFileErrorCodeEnum.NotFound:
                    return new FileNotFoundException(message);
                case HarborFileErrorCodeEnum.Conflict:
                    return new WorkspaceConflictException(message);
                case HarborFileErrorCodeEnum.Invalid:
                    return new InvalidOperationException(message);
                default:
                    return new IOException("Harbor " + harbor + ": " + message);
            }
        }

        #endregion

        #region Private-Methods

        private HarborFileRequest NewRequest(HarborFileOperationEnum operation, string? path)
        {
            return new HarborFileRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Operation = operation,
                Root = _Root,
                Path = path ?? String.Empty,
                VesselId = _Vessel.Id,
                VesselName = _Vessel.Name,
                RepoUrl = String.IsNullOrWhiteSpace(_Vessel.RepoUrl) ? null : _Vessel.RepoUrl
            };
        }

        private string Describe()
        {
            return "Harbor " + _Manager.Describe(_HarborId);
        }

        private async Task<HarborFileResult> SendAsync(HarborFileRequest request, CancellationToken token)
        {
            HarborFileResult? result = await _Manager.SendFileAsync(_HarborId, request, TimeoutMs, token).ConfigureAwait(false);
            if (result == null)
                throw new InvalidOperationException(Describe() + " did not answer a file " + request.Operation + " in " + _Root + " within " + (TimeoutMs / 1000) + " seconds.");
            if (!result.Success) throw ToException(result, _Manager.Describe(_HarborId));
            return result;
        }

        #endregion
    }
}
