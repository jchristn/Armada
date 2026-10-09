namespace Armada.Core.Services
{
    using System.Security.Cryptography;
    using System.Text;
    using Armada.Core.Models;

    /// <summary>
    /// The file side of Workspace, over a checkout root on the machine this code runs on. Every path is relative to the
    /// root; absolute paths, paths that leave the root, paths through symbolic links or junctions, and the .git folder
    /// and the hidden build folders are refused. The Admiral uses it for a working directory on its own disk, and a
    /// Harbor uses it for a checkout on its host when the Admiral asks over the link, so the same rules apply on both.
    /// Thread-safe: the class holds no state.
    /// </summary>
    public static class WorkspaceFileEngine
    {
        #region Public-Members

        /// <summary>
        /// Largest file Workspace opens for editing, in bytes.
        /// </summary>
        public const int EditableTextMaxBytes = 512 * 1024;

        /// <summary>
        /// Largest preview returned for a file, in bytes.
        /// </summary>
        public const int PreviewMaxBytes = 64 * 1024;

        /// <summary>
        /// Largest file searched, in bytes.
        /// </summary>
        public const int SearchFileMaxBytes = 256 * 1024;

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _HiddenDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            "node_modules",
            "bin",
            "obj",
            "dist",
            "coverage"
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// The full path of a checkout root, which must exist.
        /// </summary>
        /// <param name="rootPath">Root path.</param>
        /// <returns>The full path.</returns>
        /// <exception cref="DirectoryNotFoundException">Thrown when the root is empty or does not exist.</exception>
        public static string RequireRoot(string? rootPath)
        {
            if (String.IsNullOrWhiteSpace(rootPath))
                throw new DirectoryNotFoundException("No working directory configured for this vessel.");

            string full = Path.GetFullPath(rootPath);
            if (!Directory.Exists(full))
                throw new DirectoryNotFoundException("The vessel working directory does not exist.");
            return full;
        }

        /// <summary>
        /// List one directory.
        /// </summary>
        /// <param name="rootPath">Checkout root.</param>
        /// <param name="path">Relative directory path, or null for the root.</param>
        /// <returns>The listing (VesselId is left empty for the caller to set).</returns>
        public static WorkspaceTreeResult GetTree(string rootPath, string? path)
        {
            string root = RequireRoot(rootPath);
            string directoryPath = ResolvePath(root, path, mustExist: true, expectDirectory: true);

            DirectoryInfo directory = new DirectoryInfo(directoryPath);
            List<WorkspaceTreeEntry> entries = new List<WorkspaceTreeEntry>();
            foreach (DirectoryInfo childDirectory in directory.GetDirectories())
            {
                if (ShouldHideEntry(childDirectory.Name, childDirectory.Attributes))
                    continue;

                entries.Add(new WorkspaceTreeEntry
                {
                    Name = childDirectory.Name,
                    RelativePath = ToRelativePath(root, childDirectory.FullName),
                    IsDirectory = true,
                    IsEditable = false,
                    SizeBytes = null,
                    LastWriteUtc = childDirectory.LastWriteTimeUtc
                });
            }

            foreach (FileInfo childFile in directory.GetFiles())
            {
                if (ShouldHideEntry(childFile.Name, childFile.Attributes))
                    continue;

                entries.Add(new WorkspaceTreeEntry
                {
                    Name = childFile.Name,
                    RelativePath = ToRelativePath(root, childFile.FullName),
                    IsDirectory = false,
                    IsEditable = IsEditableTextFile(childFile.FullName, childFile.Length),
                    SizeBytes = childFile.Length,
                    LastWriteUtc = childFile.LastWriteTimeUtc
                });
            }

            entries = entries
                .OrderByDescending(e => e.IsDirectory)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string currentPath = ToRelativePath(root, directoryPath);
            string? parentPath = currentPath.Length < 1
                ? null
                : ToRelativeParentPath(currentPath);

            return new WorkspaceTreeResult
            {
                RootPath = root,
                CurrentPath = currentPath,
                ParentPath = parentPath,
                Entries = entries
            };
        }

        /// <summary>
        /// Read one file.
        /// </summary>
        /// <param name="rootPath">Checkout root.</param>
        /// <param name="path">Relative file path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The file (VesselId is left empty for the caller to set).</returns>
        public static async Task<WorkspaceFileResponse> GetFileAsync(string rootPath, string path, CancellationToken token = default)
        {
            string root = RequireRoot(rootPath);
            string filePath = ResolvePath(root, path, mustExist: true, expectDirectory: false);
            FileInfo file = new FileInfo(filePath);

            byte[] bytes = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
            bool isBinary = IsBinary(bytes);
            bool isLarge = bytes.LongLength > EditableTextMaxBytes;
            bool isEditable = !isBinary && !isLarge;
            bool previewTruncated = false;
            string content = string.Empty;

            if (!isBinary)
            {
                byte[] previewBytes = bytes;
                if (bytes.LongLength > PreviewMaxBytes)
                {
                    previewBytes = bytes.Take(PreviewMaxBytes).ToArray();
                    previewTruncated = true;
                }

                content = DecodeText(previewBytes);
                if (content.Length > 0 && bytes.LongLength > PreviewMaxBytes)
                    content += "\n\n[Workspace preview truncated]";
            }

            return new WorkspaceFileResponse
            {
                Path = ToRelativePath(root, filePath),
                Name = file.Name,
                Content = content,
                ContentHash = ComputeHash(bytes),
                IsEditable = isEditable,
                IsBinary = isBinary,
                IsLarge = isLarge,
                PreviewTruncated = previewTruncated,
                SizeBytes = bytes.LongLength,
                LastWriteUtc = file.LastWriteTimeUtc,
                Language = GetLanguageHint(file.Extension)
            };
        }

        /// <summary>
        /// Save one text file with optimistic concurrency (the expected hash of the content the editor opened).
        /// </summary>
        /// <param name="rootPath">Checkout root.</param>
        /// <param name="request">Save request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        public static async Task<WorkspaceSaveResult> SaveFileAsync(string rootPath, WorkspaceSaveRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));

            string root = RequireRoot(rootPath);
            string filePath = ResolvePath(root, request.Path, mustExist: false, expectDirectory: false);
            string parentDirectory = Path.GetDirectoryName(filePath)
                ?? throw new InvalidOperationException("Workspace file path did not resolve to a parent directory.");

            if (!Directory.Exists(parentDirectory))
                throw new DirectoryNotFoundException("Parent directory does not exist.");

            bool created = !File.Exists(filePath);
            if (!created)
            {
                byte[] existingBytes = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
                if (IsBinary(existingBytes))
                    throw new InvalidOperationException("Binary files cannot be edited in Workspace.");

                string currentHash = ComputeHash(existingBytes);
                if (!String.Equals(currentHash, request.ExpectedHash ?? String.Empty, StringComparison.Ordinal))
                {
                    throw new WorkspaceConflictException("The file changed on disk after it was opened. Reload the file before saving.");
                }
            }
            else if (!String.IsNullOrEmpty(request.ExpectedHash))
            {
                throw new WorkspaceConflictException("The file does not exist anymore. Reload the tree before saving.");
            }

            string normalizedContent = NormalizeLineEndingsForSave(filePath, request.Content ?? String.Empty);
            byte[] newBytes = new UTF8Encoding(false).GetBytes(normalizedContent);
            await File.WriteAllBytesAsync(filePath, newBytes, token).ConfigureAwait(false);

            FileInfo file = new FileInfo(filePath);
            return new WorkspaceSaveResult
            {
                Path = ToRelativePath(root, filePath),
                ContentHash = ComputeHash(newBytes),
                SizeBytes = file.Length,
                LastWriteUtc = file.LastWriteTimeUtc,
                Created = created
            };
        }

        /// <summary>
        /// Create one directory.
        /// </summary>
        /// <param name="rootPath">Checkout root.</param>
        /// <param name="request">Request.</param>
        /// <returns>The result.</returns>
        public static WorkspaceOperationResult CreateDirectory(string rootPath, WorkspaceCreateDirectoryRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));

            string root = RequireRoot(rootPath);
            string directoryPath = ResolvePath(root, request.Path, mustExist: false, expectDirectory: true);
            if (File.Exists(directoryPath))
                throw new InvalidOperationException("A file already exists at that path.");

            Directory.CreateDirectory(directoryPath);
            return new WorkspaceOperationResult
            {
                Path = ToRelativePath(root, directoryPath),
                Status = "created"
            };
        }

        /// <summary>
        /// Rename or move one file or directory.
        /// </summary>
        /// <param name="rootPath">Checkout root.</param>
        /// <param name="request">Request.</param>
        /// <returns>The result.</returns>
        public static WorkspaceOperationResult Rename(string rootPath, WorkspaceRenameRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path) || String.IsNullOrWhiteSpace(request.NewPath))
                throw new ArgumentException("Path and NewPath are required.", nameof(request));

            string root = RequireRoot(rootPath);
            string sourcePath = ResolvePath(root, request.Path, mustExist: true, expectDirectory: null);
            string destinationPath = ResolvePath(root, request.NewPath, mustExist: false, expectDirectory: null);

            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                throw new InvalidOperationException("A file or directory already exists at the destination path.");

            string? destinationParent = Path.GetDirectoryName(destinationPath);
            if (String.IsNullOrEmpty(destinationParent) || !Directory.Exists(destinationParent))
                throw new DirectoryNotFoundException("Destination parent directory does not exist.");

            if (Directory.Exists(sourcePath))
                Directory.Move(sourcePath, destinationPath);
            else
                File.Move(sourcePath, destinationPath);

            return new WorkspaceOperationResult
            {
                Path = ToRelativePath(root, sourcePath),
                NewPath = ToRelativePath(root, destinationPath),
                Status = "renamed"
            };
        }

        /// <summary>
        /// Delete one file or directory.
        /// </summary>
        /// <param name="rootPath">Checkout root.</param>
        /// <param name="path">Relative path.</param>
        /// <returns>The result.</returns>
        public static WorkspaceOperationResult Delete(string rootPath, string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            string root = RequireRoot(rootPath);
            string targetPath = ResolvePath(root, path, mustExist: true, expectDirectory: null);

            if (Directory.Exists(targetPath))
                Directory.Delete(targetPath, true);
            else
                File.Delete(targetPath);

            return new WorkspaceOperationResult
            {
                Path = ToRelativePath(root, targetPath),
                Status = "deleted"
            };
        }

        /// <summary>
        /// Search the text files under the root for a substring (case-insensitive).
        /// </summary>
        /// <param name="rootPath">Checkout root.</param>
        /// <param name="query">Text to find.</param>
        /// <param name="maxResults">Largest number of matches.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matches.</returns>
        public static async Task<WorkspaceSearchResult> SearchAsync(string rootPath, string query, int maxResults, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query is required.", nameof(query));

            string root = RequireRoot(rootPath);
            List<WorkspaceSearchMatch> matches = new List<WorkspaceSearchMatch>();
            bool truncated = false;
            int limit = maxResults < 1 ? 1 : maxResults;

            foreach (string filePath in EnumerateVisibleFiles(root, token))
            {
                token.ThrowIfCancellationRequested();

                FileInfo file = new FileInfo(filePath);
                if (file.Length > SearchFileMaxBytes)
                    continue;

                byte[] bytes = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
                if (IsBinary(bytes))
                    continue;

                string text = DecodeText(bytes);
                string[] lines = text.Replace("\r\n", "\n").Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    matches.Add(new WorkspaceSearchMatch
                    {
                        Path = ToRelativePath(root, filePath),
                        LineNumber = i + 1,
                        Preview = lines[i].Trim()
                    });

                    if (matches.Count >= limit)
                    {
                        truncated = true;
                        break;
                    }
                }

                if (truncated)
                    break;
            }

            return new WorkspaceSearchResult
            {
                Query = query,
                TotalMatches = matches.Count,
                Truncated = truncated,
                Matches = matches
            };
        }

        /// <summary>
        /// Resolve a relative path inside a root for reading results the caller expects there (for example check-run
        /// artifacts in build output folders): absolute paths, paths that leave the root, and paths through symbolic links
        /// or junctions are refused; the hidden build folders are allowed.
        /// </summary>
        /// <param name="rootPath">Checkout root (full path).</param>
        /// <param name="relativePath">Relative path.</param>
        /// <returns>The full path.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown when the path is not allowed.</exception>
        public static string ResolveContainedPath(string rootPath, string? relativePath)
        {
            string root = RequireRoot(rootPath);
            string relative = NormalizeRequestedPath(relativePath);
            foreach (string segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
                    throw new UnauthorizedAccessException("The .git directory is not accessible through Workspace.");
            }

            string candidate = relative.Length < 1
                ? root
                : Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!PathContainment.IsInside(root, candidate, allowRoot: true))
                throw new UnauthorizedAccessException("Requested path is outside the workspace root.");

            GuardAgainstReparsePoints(root, candidate);
            return candidate;
        }

        /// <summary>
        /// Whether a byte sample looks like binary content.
        /// </summary>
        /// <param name="bytes">Bytes.</param>
        /// <returns>True for binary.</returns>
        public static bool IsBinary(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 1)
                return false;

            int sampleLength = Math.Min(bytes.Length, 2048);
            int controlCount = 0;
            for (int i = 0; i < sampleLength; i++)
            {
                byte value = bytes[i];
                if (value == 0)
                    return true;

                if (value < 8 || (value > 13 && value < 32))
                    controlCount++;
            }

            return controlCount > sampleLength / 8;
        }

        /// <summary>
        /// Normalize a request path to a forward-slash relative path; absolute paths are refused.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>The relative path, or empty for the root.</returns>
        /// <exception cref="UnauthorizedAccessException">Thrown for an absolute path.</exception>
        public static string NormalizeRequestedPath(string? path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return String.Empty;

            string normalized = path.Trim().Replace('\\', '/').Trim('/');
            if (Path.IsPathRooted(normalized))
                throw new UnauthorizedAccessException("Absolute paths are not allowed.");

            return normalized;
        }

        #endregion

        #region Private-Methods

        private static string ResolvePath(string rootPath, string? requestedPath, bool mustExist, bool? expectDirectory)
        {
            string relativePath = NormalizeRequestedPath(requestedPath);
            if (relativePath.Length > 0)
            {
                string[] segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                foreach (string segment in segments)
                {
                    if (segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("The .git directory is not accessible through Workspace.");
                    if (_HiddenDirectoryNames.Contains(segment))
                        throw new UnauthorizedAccessException("That path is not available in Workspace.");
                }
            }

            string candidate = relativePath.Length < 1
                ? rootPath
                : Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!PathContainment.IsInside(rootPath, candidate, allowRoot: true))
                throw new UnauthorizedAccessException("Requested path is outside the workspace root.");

            GuardAgainstReparsePoints(rootPath, candidate);

            if (mustExist && !File.Exists(candidate) && !Directory.Exists(candidate))
                throw new FileNotFoundException("Workspace path not found.");

            if (expectDirectory == true && File.Exists(candidate))
                throw new InvalidOperationException("Requested path is a file, not a directory.");

            if (expectDirectory == false && Directory.Exists(candidate))
                throw new InvalidOperationException("Requested path is a directory, not a file.");

            return candidate;
        }

        private static void GuardAgainstReparsePoints(string rootPath, string candidate)
        {
            if (candidate.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
                return;

            string relative = Path.GetRelativePath(rootPath, candidate);
            if (relative == ".")
                return;

            string current = rootPath;
            string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string rawSegment in segments)
            {
                if (String.IsNullOrWhiteSpace(rawSegment))
                    continue;

                current = Path.Combine(current, rawSegment);
                if (!File.Exists(current) && !Directory.Exists(current))
                    continue;

                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Workspace paths that traverse symlinks or junctions are not supported.");
            }
        }

        private static string ToRelativePath(string rootPath, string fullPath)
        {
            string relative = Path.GetRelativePath(rootPath, fullPath);
            if (relative == ".")
                return String.Empty;

            return relative.Replace('\\', '/');
        }

        private static string? ToRelativeParentPath(string currentPath)
        {
            string normalized = currentPath.Replace('\\', '/').Trim('/');
            int lastSlash = normalized.LastIndexOf('/');
            if (lastSlash < 0)
                return String.Empty;

            return normalized.Substring(0, lastSlash);
        }

        private static bool ShouldHideEntry(string name, FileAttributes attributes)
        {
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return true;

            return _HiddenDirectoryNames.Contains(name);
        }

        private static bool ShouldHideResolvedPath(string rootPath, string filePath)
        {
            string relative = ToRelativePath(rootPath, filePath);
            if (relative.Length < 1)
                return false;

            string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return segments.Any(segment => _HiddenDirectoryNames.Contains(segment));
        }

        private static IEnumerable<string> EnumerateVisibleFiles(string rootPath, CancellationToken token)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(rootPath);

            while (pending.Count > 0)
            {
                token.ThrowIfCancellationRequested();

                string current = pending.Pop();

                IEnumerable<string> directories;
                try
                {
                    directories = Directory.EnumerateDirectories(current);
                }
                catch
                {
                    continue;
                }

                foreach (string directory in directories)
                {
                    token.ThrowIfCancellationRequested();

                    DirectoryInfo info = new DirectoryInfo(directory);
                    if (ShouldHideEntry(info.Name, info.Attributes))
                        continue;

                    pending.Push(info.FullName);
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(current);
                }
                catch
                {
                    continue;
                }

                foreach (string file in files)
                {
                    token.ThrowIfCancellationRequested();

                    FileInfo info = new FileInfo(file);
                    if (ShouldHideEntry(info.Name, info.Attributes))
                        continue;

                    if (ShouldHideResolvedPath(rootPath, info.FullName))
                        continue;

                    yield return info.FullName;
                }
            }
        }

        private static bool IsEditableTextFile(string filePath, long sizeBytes)
        {
            if (sizeBytes > EditableTextMaxBytes)
                return false;

            if (!File.Exists(filePath))
                return true;

            byte[] bytes = File.ReadAllBytes(filePath);
            return !IsBinary(bytes);
        }

        private static string DecodeText(byte[] bytes)
        {
            using MemoryStream memory = new MemoryStream(bytes);
            using StreamReader reader = new StreamReader(memory, Encoding.UTF8, true);
            return reader.ReadToEnd();
        }

        private static string ComputeHash(byte[] bytes)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(bytes);
            return Convert.ToHexString(hash);
        }

        private static string GetLanguageHint(string extension)
        {
            return extension.Trim().ToLowerInvariant() switch
            {
                ".cs" => "csharp",
                ".csproj" => "xml",
                ".json" => "json",
                ".md" => "markdown",
                ".ts" => "typescript",
                ".tsx" => "typescript",
                ".js" => "javascript",
                ".jsx" => "javascript",
                ".html" => "html",
                ".css" => "css",
                ".sql" => "sql",
                ".yml" => "yaml",
                ".yaml" => "yaml",
                ".xml" => "xml",
                ".sh" => "shell",
                ".bat" => "bat",
                ".ps1" => "powershell",
                _ => "plaintext"
            };
        }

        private static string NormalizeLineEndingsForSave(string filePath, string content)
        {
            if (!File.Exists(filePath))
                return content.Replace("\r\n", "\n");

            string existingText = File.ReadAllText(filePath);
            bool usesCrLf = existingText.Contains("\r\n", StringComparison.Ordinal);
            string normalized = content.Replace("\r\n", "\n");
            return usesCrLf ? normalized.Replace("\n", "\r\n") : normalized;
        }

        #endregion
    }
}
