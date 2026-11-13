namespace Armada.Core.BlackBox.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Blobject.Core;

    /// <summary>
    /// An external black box store over a Blobject client (Disk, S3 and S3-compatible, AzureBlob, GoogleCloud, Cifs, Nfs).
    /// Objects are stored at their StorageKey (BlackBoxKeyLayout). Reads fetch the whole object and slice it (objects are
    /// bounded by maxEvidenceObjectBytes); the Disk target reads ranges straight from the file. The Disk target refuses
    /// keys that resolve outside its root directory and keys whose path crosses a symbolic link. Failures surface as
    /// <see cref="BlackBoxStoreUnavailableException"/> (the writer then applies the database fallback) and absent objects
    /// as <see cref="BlackBoxObjectMissingException"/>. Thread-safe.
    /// </summary>
    public class BlobjectBlackBoxStore : IBlackBoxStore, IDisposable
    {
        #region Public-Members

        /// <inheritdoc />
        public BlackBoxStorageTargetEnum Target { get; }

        /// <summary>
        /// The Disk target's root directory (absolute, with a trailing separator), or null for other targets.
        /// </summary>
        public string? DiskRoot { get; }

        #endregion

        #region Private-Members

        private readonly BlobClientBase _Client;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="target">Target this store serves.</param>
        /// <param name="client">Blobject client (owned: disposed with the store).</param>
        /// <param name="diskRoot">Root directory for the Disk target (enables root and symlink checks and ranged file reads).</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null, or diskRoot is null for the Disk target.</exception>
        public BlobjectBlackBoxStore(BlackBoxStorageTargetEnum target, BlobClientBase client, string? diskRoot = null)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
            Target = target;
            if (target == BlackBoxStorageTargetEnum.Disk)
            {
                if (String.IsNullOrEmpty(diskRoot)) throw new ArgumentNullException(nameof(diskRoot));
                string full = Path.GetFullPath(diskRoot);
                DiskRoot = full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
            }
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task WriteAsync(BlackBoxObjectWrite write, CancellationToken token = default)
        {
            if (write == null) throw new ArgumentNullException(nameof(write));
            string key = KeyOf(write.Reference);
            if (DiskRoot != null) ResolveDiskPath(key);
            try
            {
                await _Client.WriteAsync(key, String.IsNullOrEmpty(write.ContentType) ? "application/octet-stream" : write.ContentType, write.Data ?? Array.Empty<byte>(), token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new BlackBoxStoreUnavailableException("The " + Target + " store could not write " + key + ": " + e.Message, e);
            }
        }

        /// <inheritdoc />
        public async Task<Stream> OpenReadAsync(BlackBoxObjectRef reference, long offset, long? length, CancellationToken token = default)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset), "Offset must not be negative.");
            if (length.HasValue && length.Value < 1) throw new ArgumentOutOfRangeException(nameof(length), "Length must be at least 1.");
            string key = KeyOf(reference);
            if (DiskRoot != null) return await ReadDiskRangeAsync(key, offset, length, token).ConfigureAwait(false);

            byte[] data;
            try
            {
                data = await _Client.GetAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (IsNotFound(e) || !await ExistsQuietAsync(key, token).ConfigureAwait(false))
                    throw new BlackBoxObjectMissingException("The bytes of " + reference.Name + " are missing from the " + Target + " store.");
                throw new BlackBoxStoreUnavailableException("The " + Target + " store could not read " + key + ": " + e.Message, e);
            }
            if (data == null) throw new BlackBoxObjectMissingException("The bytes of " + reference.Name + " are missing from the " + Target + " store.");
            return new MemoryStream(Slice(data, offset, length), false);
        }

        /// <inheritdoc />
        public async Task<bool> ExistsAsync(BlackBoxObjectRef reference, CancellationToken token = default)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            string key = KeyOf(reference);
            if (DiskRoot != null) return File.Exists(ResolveDiskPath(key));
            try
            {
                return await _Client.ExistsAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (IsNotFound(e)) return false;
                throw new BlackBoxStoreUnavailableException("The " + Target + " store could not check " + key + ": " + e.Message, e);
            }
        }

        /// <inheritdoc />
        public async Task DeleteAsync(BlackBoxObjectRef reference, CancellationToken token = default)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            string key = KeyOf(reference);
            if (DiskRoot != null) ResolveDiskPath(key);
            try
            {
                await _Client.DeleteAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                if (IsNotFound(e)) return;
                throw new BlackBoxStoreUnavailableException("The " + Target + " store could not delete " + key + ": " + e.Message, e);
            }
        }

        /// <inheritdoc />
        public async Task<BlackBoxStoreDeleteResult> DeleteManyAsync(BlackBoxObjectRefSet references, CancellationToken token = default)
        {
            if (references == null) throw new ArgumentNullException(nameof(references));
            BlackBoxStoreDeleteResult result = new BlackBoxStoreDeleteResult { Requested = references.References.Count };
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (BlackBoxObjectRef reference in references.References)
            {
                string key;
                try
                {
                    key = KeyOf(reference);
                    if (DiskRoot != null) ResolveDiskPath(key);
                }
                catch (Exception e) when (e is ArgumentException || e is BlackBoxStoreUnavailableException)
                {
                    Fail(result, reference.Name, e.Message);
                    continue;
                }
                names[key] = reference.Name;
            }
            if (names.Count == 0) return result;
            try
            {
                DeleteManyResult deleted = await _Client.DeleteManyAsync(names.Keys.ToList(), token).ConfigureAwait(false);
                HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
                foreach (DeleteResult item in deleted.Results)
                {
                    if (item == null || item.Key == null || !names.TryGetValue(item.Key, out string? name)) continue;
                    reported.Add(item.Key);
                    if (item.Success) result.Deleted++;
                    else Fail(result, name, item.Error ?? "delete failed");
                }
                foreach (KeyValuePair<string, string> pending in names.Where(n => !reported.Contains(n.Key)))
                {
                    await DeleteAsync(new BlackBoxObjectRef { Target = Target, Name = pending.Value, StorageKey = pending.Key }, token).ConfigureAwait(false);
                    result.Deleted++;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                result.Deleted = 0;
                result.Failed = 0;
                result.FailedNames.Clear();
                result.FirstError = null;
                foreach (string name in names.Values) Fail(result, name, "The " + Target + " store could not delete objects: " + e.Message);
            }
            return result;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<BlackBoxStoredKey> EnumerateAsync(string prefix, [EnumeratorCancellation] CancellationToken token = default)
        {
            string normalized = (prefix ?? String.Empty).Replace('\\', '/').TrimStart('/');
            if (DiskRoot != null)
            {
                foreach (BlackBoxStoredKey key in EnumerateDisk(normalized, token)) yield return key;
                yield break;
            }

            IAsyncEnumerator<BlobMetadata> items;
            try
            {
                items = _Client.EnumerateAsync(new EnumerationFilter { Prefix = normalized }, token).GetAsyncEnumerator(token);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new BlackBoxStoreUnavailableException("The " + Target + " store could not list " + normalized + ": " + e.Message, e);
            }
            try
            {
                while (true)
                {
                    bool more;
                    try
                    {
                        more = await items.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        throw new BlackBoxStoreUnavailableException("The " + Target + " store could not list " + normalized + ": " + e.Message, e);
                    }
                    if (!more) yield break;
                    BlobMetadata item = items.Current;
                    if (item == null || item.IsFolder || String.IsNullOrEmpty(item.Key)) continue;
                    if (!item.Key.StartsWith(normalized, StringComparison.Ordinal)) continue;
                    yield return new BlackBoxStoredKey { Key = item.Key, SizeBytes = item.ContentLength, LastModifiedUtc = Utc(item.LastUpdateUtc) ?? Utc(item.CreatedUtc) };
                }
            }
            finally
            {
                await items.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        public async Task<BlackBoxStoreHealth> CheckHealthAsync(CancellationToken token = default)
        {
            Stopwatch sw = Stopwatch.StartNew();
            BlackBoxStoreHealth health = new BlackBoxStoreHealth { Target = Target, CheckedUtc = DateTime.UtcNow };
            try
            {
                bool ok = await _Client.ValidateConnectivity(token).ConfigureAwait(false);
                health.Healthy = ok;
                health.Message = ok ? Target + " store reachable." : Target + " store did not pass its connectivity check (wrong endpoint, credentials, bucket, container or share?).";
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                health.Healthy = false;
                health.Message = Target + " store unreachable: " + e.Message;
            }
            health.LatencyMs = sw.ElapsedMilliseconds;
            return health;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            if (_Client is IDisposable disposable) disposable.Dispose();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Resolve a key to its file under the Disk root, refusing keys that escape the root and paths that cross a
        /// symbolic link (or other reparse point) below it.
        /// </summary>
        /// <param name="key">Object key.</param>
        /// <returns>The absolute file path.</returns>
        /// <exception cref="InvalidOperationException">Thrown when this store is not a Disk store.</exception>
        /// <exception cref="BlackBoxStoreUnavailableException">Thrown when the key is refused.</exception>
        public string ResolveDiskPath(string key)
        {
            if (DiskRoot == null) throw new InvalidOperationException("Only the Disk store maps keys to files.");
            if (String.IsNullOrWhiteSpace(key)) throw new BlackBoxStoreUnavailableException("The Disk store refused an empty key.");
            string[] segments = key.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(s => s == "." || s == "..") || Path.IsPathRooted(key) || key.Contains(':'))
                throw new BlackBoxStoreUnavailableException("The Disk store refused key " + key + ": it would resolve outside " + DiskRoot + ".");
            string path = Path.GetFullPath(Path.Combine(DiskRoot, Path.Combine(segments)));
            StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(DiskRoot, comparison))
                throw new BlackBoxStoreUnavailableException("The Disk store refused key " + key + ": it resolves outside " + DiskRoot + ".");
            string current = DiskRoot.TrimEnd(Path.DirectorySeparatorChar);
            foreach (string segment in segments)
            {
                current = Path.Combine(current, segment);
                FileSystemInfo info = new FileInfo(current);
                if (!info.Exists) info = new DirectoryInfo(current);
                if (!info.Exists) break;
                if (info.LinkTarget != null || (info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                    throw new BlackBoxStoreUnavailableException("The Disk store refused key " + key + ": " + current + " is a symbolic link.");
            }
            return path;
        }

        #endregion

        #region Private-Methods

        private string KeyOf(BlackBoxObjectRef? reference)
        {
            if (reference == null) throw new ArgumentNullException(nameof(reference));
            if (String.IsNullOrEmpty(reference.StorageKey)) throw new ArgumentException("An external black box object needs a storage key (" + reference.Name + ").", nameof(reference));
            string key = reference.StorageKey.Replace('\\', '/');
            // The Disk store checks the raw key (a rooted key is refused); object stores have no root to escape.
            return DiskRoot != null ? key : key.TrimStart('/');
        }

        private async Task<Stream> ReadDiskRangeAsync(string key, long offset, long? length, CancellationToken token)
        {
            string path = ResolveDiskPath(key);
            if (!File.Exists(path)) throw new BlackBoxObjectMissingException("The bytes of " + key + " are missing from the Disk store.");
            try
            {
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true))
                {
                    long total = file.Length;
                    if (offset >= total) return new MemoryStream(Array.Empty<byte>(), false);
                    long take = length.HasValue ? Math.Min(length.Value, total - offset) : total - offset;
                    if (take > Int32.MaxValue) take = Int32.MaxValue;
                    byte[] buffer = new byte[take];
                    file.Seek(offset, SeekOrigin.Begin);
                    int read = 0;
                    while (read < take)
                    {
                        int n = await file.ReadAsync(buffer.AsMemory(read, (int)take - read), token).ConfigureAwait(false);
                        if (n == 0) break;
                        read += n;
                    }
                    if (read < take) Array.Resize(ref buffer, read);
                    return new MemoryStream(buffer, false);
                }
            }
            catch (FileNotFoundException)
            {
                throw new BlackBoxObjectMissingException("The bytes of " + key + " are missing from the Disk store.");
            }
            catch (DirectoryNotFoundException)
            {
                throw new BlackBoxObjectMissingException("The bytes of " + key + " are missing from the Disk store.");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new BlackBoxStoreUnavailableException("The Disk store could not read " + key + ": " + e.Message, e);
            }
        }

        private IEnumerable<BlackBoxStoredKey> EnumerateDisk(string prefix, CancellationToken token)
        {
            string root = DiskRoot!;
            string start = root;
            int slash = prefix.LastIndexOf('/');
            if (slash > 0) start = Path.Combine(root, Path.Combine(prefix.Substring(0, slash).Split('/', StringSplitOptions.RemoveEmptyEntries)));
            if (!Directory.Exists(start)) yield break;
            EnumerationOptions options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = true
            };
            foreach (string file in Directory.EnumerateFiles(start, "*", options))
            {
                token.ThrowIfCancellationRequested();
                string key = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
                if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
                FileInfo info = new FileInfo(file);
                yield return new BlackBoxStoredKey { Key = key, SizeBytes = info.Length, LastModifiedUtc = info.LastWriteTimeUtc };
            }
        }

        private async Task<bool> ExistsQuietAsync(string key, CancellationToken token)
        {
            try
            {
                return await _Client.ExistsAsync(key, token).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return true;
            }
        }

        private static bool IsNotFound(Exception e)
        {
            for (Exception? current = e; current != null; current = current.InnerException)
            {
                if (current is FileNotFoundException || current is DirectoryNotFoundException || current is KeyNotFoundException) return true;
                if (current is Amazon.S3.AmazonS3Exception s3 && s3.StatusCode == HttpStatusCode.NotFound) return true;
                if (current is Azure.RequestFailedException azure && azure.Status == 404) return true;
                if (current is Google.GoogleApiException google && google.HttpStatusCode == HttpStatusCode.NotFound) return true;
            }
            return false;
        }

        private static byte[] Slice(byte[] data, long offset, long? length)
        {
            if (offset == 0 && (!length.HasValue || length.Value >= data.LongLength)) return data;
            if (offset >= data.LongLength) return Array.Empty<byte>();
            long take = length.HasValue ? Math.Min(length.Value, data.LongLength - offset) : data.LongLength - offset;
            byte[] slice = new byte[take];
            Array.Copy(data, offset, slice, 0, take);
            return slice;
        }

        private static DateTime? Utc(DateTime? value)
        {
            if (!value.HasValue || value.Value == default) return null;
            if (value.Value.Kind == DateTimeKind.Local) return value.Value.ToUniversalTime();
            return DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
        }

        private static void Fail(BlackBoxStoreDeleteResult result, string name, string error)
        {
            result.Failed++;
            if (result.FailedNames.Count < Armada.Core.Services.CollectionLimits.MaxRequestItems) result.FailedNames.Add(name);
            if (result.FirstError == null) result.FirstError = error;
        }

        #endregion
    }
}
