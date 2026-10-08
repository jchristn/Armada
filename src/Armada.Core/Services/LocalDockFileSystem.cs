namespace Armada.Core.Services
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Dock file operations on this machine's disk.
    /// </summary>
    public class LocalDockFileSystem : IDockFileSystem
    {
        #region Public-Methods

        /// <inheritdoc />
        public Task<bool> DirectoryExistsAsync(string path, CancellationToken token = default)
        {
            return Task.FromResult(!String.IsNullOrEmpty(path) && Directory.Exists(path));
        }

        /// <inheritdoc />
        public async Task<string?> ReadTextAsync(string path, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            return await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task WriteTextAsync(string path, string content, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            string? directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, content ?? String.Empty, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<bool> AddGitExcludeEntryAsync(string worktreePath, string entry, CancellationToken token = default)
        {
            string? excludePath = GitExcludeFile.ResolvePath(worktreePath);
            if (String.IsNullOrEmpty(excludePath)) return false;
            await GitExcludeFile.AddEntryAsync(excludePath, entry, token).ConfigureAwait(false);
            return true;
        }

        #endregion
    }
}
