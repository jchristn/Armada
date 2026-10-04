namespace Armada.Tui.Services.Credentials
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Credential store in a JSON file readable and writable only by the owner (mode 0600 on Unix; the file is created
    /// with that mode and re-restricted on every write). The fallback when no OS keychain is available. Thread-safe.
    /// </summary>
    public class FileCredentialStore : ICredentialStore
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get { return "file"; }
        }

        /// <summary>
        /// File path.
        /// </summary>
        public string FilePath { get; }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="filePath">File path; null uses <see cref="TuiPaths.CredentialsFile"/>.</param>
        public FileCredentialStore(string? filePath = null)
        {
            FilePath = String.IsNullOrWhiteSpace(filePath) ? TuiPaths.CredentialsFile() : Path.GetFullPath(filePath!);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<string?> GetAsync(string key, CancellationToken token = default)
        {
            lock (_Lock)
            {
                Dictionary<string, string> all = Read();
                return Task.FromResult(all.TryGetValue(key ?? "", out string? value) ? value : null);
            }
        }

        /// <inheritdoc />
        public Task<bool> SetAsync(string key, string secret, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            lock (_Lock)
            {
                Dictionary<string, string> all = Read();
                all[key] = secret ?? "";
                return Task.FromResult(Write(all));
            }
        }

        /// <inheritdoc />
        public Task DeleteAsync(string key, CancellationToken token = default)
        {
            lock (_Lock)
            {
                Dictionary<string, string> all = Read();
                if (all.Remove(key ?? "")) Write(all);
                return Task.CompletedTask;
            }
        }

        #endregion

        #region Private-Methods

        private Dictionary<string, string> Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return new Dictionary<string, string>(StringComparer.Ordinal);
                string json = File.ReadAllText(FilePath);
                Dictionary<string, string>? parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                return parsed != null ? new Dictionary<string, string>(parsed, StringComparer.Ordinal) : new Dictionary<string, string>(StringComparer.Ordinal);
            }
            catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }

        private bool Write(Dictionary<string, string> all)
        {
            try
            {
                string? dir = Path.GetDirectoryName(FilePath);
                if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);
                string json = JsonSerializer.Serialize(all, new JsonSerializerOptions { WriteIndented = true });
                FileStreamOptions options = new FileStreamOptions();
                options.Mode = FileMode.Create;
                options.Access = FileAccess.Write;
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (FileStream stream = new FileStream(FilePath, options))
                using (StreamWriter writer = new StreamWriter(stream))
                {
                    writer.Write(json);
                }

                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        #endregion
    }
}
