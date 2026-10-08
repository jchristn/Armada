namespace Armada.Core.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// File operations in a checkout on the Admiral's own disk, through <see cref="WorkspaceFileEngine"/>.
    /// </summary>
    public class LocalCheckoutFiles : IVesselCheckoutFiles
    {
        #region Private-Members

        private readonly string _Root;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="root">Checkout root on this machine.</param>
        public LocalCheckoutFiles(string root)
        {
            if (String.IsNullOrWhiteSpace(root)) throw new ArgumentNullException(nameof(root));
            _Root = root;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<WorkspaceTreeResult> GetTreeAsync(string? path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(WorkspaceFileEngine.GetTree(_Root, path));
        }

        /// <inheritdoc />
        public Task<WorkspaceFileResponse> GetFileAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return WorkspaceFileEngine.GetFileAsync(_Root, path, token);
        }

        /// <inheritdoc />
        public Task<WorkspaceSaveResult> SaveFileAsync(WorkspaceSaveRequest request, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return WorkspaceFileEngine.SaveFileAsync(_Root, request, token);
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> CreateDirectoryAsync(WorkspaceCreateDirectoryRequest request, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(WorkspaceFileEngine.CreateDirectory(_Root, request));
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> RenameAsync(WorkspaceRenameRequest request, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(WorkspaceFileEngine.Rename(_Root, request));
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> DeleteAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(WorkspaceFileEngine.Delete(_Root, path));
        }

        /// <inheritdoc />
        public Task<WorkspaceSearchResult> SearchAsync(string query, int maxResults, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return WorkspaceFileEngine.SearchAsync(_Root, query, maxResults, token);
        }

        /// <inheritdoc />
        public Task<VesselCheckoutFileInfo> StatAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            string full = WorkspaceFileEngine.ResolveContainedPath(_Root, path);
            VesselCheckoutFileInfo info = new VesselCheckoutFileInfo();
            info.IsDirectory = Directory.Exists(full);
            info.Exists = info.IsDirectory || File.Exists(full);
            if (info.Exists && !info.IsDirectory)
            {
                FileInfo file = new FileInfo(full);
                info.SizeBytes = file.Length;
                info.LastWriteUtc = file.LastWriteTimeUtc;
            }

            return Task.FromResult(info);
        }

        /// <inheritdoc />
        public async Task<string?> ReadTextAsync(string path, long maxBytes, CancellationToken token = default)
        {
            string full = WorkspaceFileEngine.ResolveContainedPath(_Root, path);
            if (!File.Exists(full)) return null;
            if (maxBytes > 0 && new FileInfo(full).Length > maxBytes) return null;
            return await File.ReadAllTextAsync(full, token).ConfigureAwait(false);
        }

        #endregion
    }
}
