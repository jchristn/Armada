namespace Armada.Core.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;

    /// <summary>
    /// Dock file operations on a Harbor's disk, carried out by the Harbor over its link. The Harbor only allows paths
    /// inside its docks directory.
    /// </summary>
    public class HarborDockFileSystem : IDockFileSystem
    {
        #region Private-Members

        private const int _TimeoutMs = 60000;
        private readonly HarborConnectionManager _Manager;
        private readonly string _HarborId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate for one Harbor.
        /// </summary>
        /// <param name="manager">Harbor connection manager.</param>
        /// <param name="harborId">Harbor identifier.</param>
        public HarborDockFileSystem(HarborConnectionManager manager, string harborId)
        {
            _Manager = manager ?? throw new ArgumentNullException(nameof(manager));
            if (String.IsNullOrWhiteSpace(harborId)) throw new ArgumentNullException(nameof(harborId));
            _HarborId = harborId;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<bool> DirectoryExistsAsync(string path, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) return false;
            HarborFileResult result = await SendAsync(HarborFileOperationEnum.Stat, path, null, token).ConfigureAwait(false);
            if (!result.Success) return false;
            return result.Exists && result.IsDirectory;
        }

        /// <inheritdoc />
        public async Task<string?> ReadTextAsync(string path, CancellationToken token = default)
        {
            HarborFileResult result = await SendAsync(HarborFileOperationEnum.Read, path, null, token).ConfigureAwait(false);
            if (!result.Success) throw new IOException("Harbor " + _HarborId + " could not read " + path + ": " + (result.Message ?? "no reason given"));
            return result.Exists ? (result.Content ?? String.Empty) : null;
        }

        /// <inheritdoc />
        public async Task WriteTextAsync(string path, string content, CancellationToken token = default)
        {
            HarborFileResult result = await SendAsync(HarborFileOperationEnum.Write, path, content ?? String.Empty, token).ConfigureAwait(false);
            if (!result.Success) throw new IOException("Harbor " + _HarborId + " could not write " + path + ": " + (result.Message ?? "no reason given"));
        }

        /// <inheritdoc />
        public async Task<bool> AddGitExcludeEntryAsync(string worktreePath, string entry, CancellationToken token = default)
        {
            HarborFileResult result = await SendAsync(HarborFileOperationEnum.AddGitExclude, worktreePath, entry, token).ConfigureAwait(false);
            return result.Success;
        }

        #endregion

        #region Private-Methods

        private async Task<HarborFileResult> SendAsync(HarborFileOperationEnum operation, string path, string? content, CancellationToken token)
        {
            HarborFileRequest request = new HarborFileRequest
            {
                RequestId = Guid.NewGuid().ToString("N"),
                Operation = operation,
                Path = path ?? String.Empty,
                Content = content
            };

            HarborFileResult? result = await _Manager.SendFileAsync(_HarborId, request, _TimeoutMs, token).ConfigureAwait(false);
            if (result == null) throw new TimeoutException("Harbor " + _HarborId + " did not answer a file " + operation + " for " + path + " within " + (_TimeoutMs / 1000) + " seconds.");
            return result;
        }

        #endregion
    }
}
