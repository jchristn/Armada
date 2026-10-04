namespace Armada.Core.Services
{
    using System.Diagnostics;
    using System.Security.Cryptography;
    using System.Text;
    using Armada.Core.Models;
    using Armada.Core.Services.Interfaces;

    /// <summary>
    /// Safe workspace browsing and editing over a vessel working directory.
    /// </summary>
    public class WorkspaceService : IWorkspaceService
    {
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

        private const int EditableTextMaxBytes = 512 * 1024;
        private const int PreviewMaxBytes = 64 * 1024;
        private const int SearchFileMaxBytes = 256 * 1024;
        private const int _GitCommandTimeoutSeconds = 30;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<WorkspaceTreeResult> GetTreeAsync(Vessel vessel, string? path = null, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            string rootPath = GetWorkspaceRoot(vessel);
            string directoryPath = ResolvePath(rootPath, path, mustExist: true, expectDirectory: true);

            DirectoryInfo directory = new DirectoryInfo(directoryPath);
            List<WorkspaceTreeEntry> entries = new List<WorkspaceTreeEntry>();
            foreach (DirectoryInfo childDirectory in directory.GetDirectories())
            {
                if (ShouldHideEntry(childDirectory.Name, childDirectory.Attributes))
                    continue;

                entries.Add(new WorkspaceTreeEntry
                {
                    Name = childDirectory.Name,
                    RelativePath = ToRelativePath(rootPath, childDirectory.FullName),
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
                    RelativePath = ToRelativePath(rootPath, childFile.FullName),
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

            string currentPath = ToRelativePath(rootPath, directoryPath);
            string? parentPath = currentPath.Length < 1
                ? null
                : ToRelativeParentPath(currentPath);

            WorkspaceTreeResult result = new WorkspaceTreeResult
            {
                VesselId = vessel.Id,
                RootPath = rootPath,
                CurrentPath = currentPath,
                ParentPath = parentPath,
                Entries = entries
            };

            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<WorkspaceFileResponse> GetFileAsync(Vessel vessel, string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            string rootPath = GetWorkspaceRoot(vessel);
            string filePath = ResolvePath(rootPath, path, mustExist: true, expectDirectory: false);
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
                VesselId = vessel.Id,
                Path = ToRelativePath(rootPath, filePath),
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

        /// <inheritdoc />
        public async Task<WorkspaceSaveResult> SaveFileAsync(Vessel vessel, WorkspaceSaveRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));

            token.ThrowIfCancellationRequested();

            string rootPath = GetWorkspaceRoot(vessel);
            string filePath = ResolvePath(rootPath, request.Path, mustExist: false, expectDirectory: false);
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
                Path = ToRelativePath(rootPath, filePath),
                ContentHash = ComputeHash(newBytes),
                SizeBytes = file.Length,
                LastWriteUtc = file.LastWriteTimeUtc,
                Created = created
            };
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> CreateDirectoryAsync(Vessel vessel, WorkspaceCreateDirectoryRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path)) throw new ArgumentException("Path is required.", nameof(request));

            token.ThrowIfCancellationRequested();

            string rootPath = GetWorkspaceRoot(vessel);
            string directoryPath = ResolvePath(rootPath, request.Path, mustExist: false, expectDirectory: true);
            if (File.Exists(directoryPath))
                throw new InvalidOperationException("A file already exists at that path.");

            Directory.CreateDirectory(directoryPath);
            return Task.FromResult(new WorkspaceOperationResult
            {
                Path = ToRelativePath(rootPath, directoryPath),
                Status = "created"
            });
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> RenameAsync(Vessel vessel, WorkspaceRenameRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Path) || String.IsNullOrWhiteSpace(request.NewPath))
                throw new ArgumentException("Path and NewPath are required.", nameof(request));

            token.ThrowIfCancellationRequested();

            string rootPath = GetWorkspaceRoot(vessel);
            string sourcePath = ResolvePath(rootPath, request.Path, mustExist: true, expectDirectory: null);
            string destinationPath = ResolvePath(rootPath, request.NewPath, mustExist: false, expectDirectory: null);

            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                throw new InvalidOperationException("A file or directory already exists at the destination path.");

            string? destinationParent = Path.GetDirectoryName(destinationPath);
            if (String.IsNullOrEmpty(destinationParent) || !Directory.Exists(destinationParent))
                throw new DirectoryNotFoundException("Destination parent directory does not exist.");

            if (Directory.Exists(sourcePath))
                Directory.Move(sourcePath, destinationPath);
            else
                File.Move(sourcePath, destinationPath);

            return Task.FromResult(new WorkspaceOperationResult
            {
                Path = ToRelativePath(rootPath, sourcePath),
                NewPath = ToRelativePath(rootPath, destinationPath),
                Status = "renamed"
            });
        }

        /// <inheritdoc />
        public Task<WorkspaceOperationResult> DeleteAsync(Vessel vessel, string path, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Path is required.", nameof(path));

            token.ThrowIfCancellationRequested();

            string rootPath = GetWorkspaceRoot(vessel);
            string targetPath = ResolvePath(rootPath, path, mustExist: true, expectDirectory: null);

            if (Directory.Exists(targetPath))
                Directory.Delete(targetPath, true);
            else
                File.Delete(targetPath);

            return Task.FromResult(new WorkspaceOperationResult
            {
                Path = ToRelativePath(rootPath, targetPath),
                Status = "deleted"
            });
        }

        /// <inheritdoc />
        public async Task<WorkspaceSearchResult> SearchAsync(Vessel vessel, string query, int maxResults = 200, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentException("Query is required.", nameof(query));

            token.ThrowIfCancellationRequested();

            string rootPath = GetWorkspaceRoot(vessel);
            List<WorkspaceSearchMatch> matches = new List<WorkspaceSearchMatch>();
            bool truncated = false;

            foreach (string filePath in EnumerateVisibleFiles(rootPath, token))
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
                        Path = ToRelativePath(rootPath, filePath),
                        LineNumber = i + 1,
                        Preview = lines[i].Trim()
                    });

                    if (matches.Count >= maxResults)
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

        /// <inheritdoc />
        public async Task<WorkspaceChangesResult> GetChangesAsync(Vessel vessel, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            string rootPath = GetWorkspaceRoot(vessel);
            try
            {
                try
                {
                    await RunGitCommandAsync(rootPath, token, "fetch", "origin", "--quiet").ConfigureAwait(false);
                }
                catch
                {
                }

                string output = await RunGitCommandAsync(rootPath, token, "status", "--porcelain=v1", "--branch", "--untracked-files=all").ConfigureAwait(false);
                return ParseGitStatus(output);
            }
            catch (Exception ex)
            {
                return new WorkspaceChangesResult
                {
                    BranchName = String.Empty,
                    IsDirty = false,
                    CommitsAhead = 0,
                    CommitsBehind = 0,
                    Changes = new List<WorkspaceChangeEntry>(),
                    Error = ex.Message
                };
            }
        }

        /// <inheritdoc />
        public async Task<WorkspaceStatusResult> GetStatusAsync(
            Vessel vessel,
            IReadOnlyList<WorkspaceActiveMission>? activeMissions = null,
            CancellationToken token = default)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            token.ThrowIfCancellationRequested();

            string? normalizedRoot = null;
            if (!String.IsNullOrWhiteSpace(vessel.WorkingDirectory))
            {
                normalizedRoot = Path.GetFullPath(vessel.WorkingDirectory);
            }

            if (String.IsNullOrWhiteSpace(normalizedRoot) || !Directory.Exists(normalizedRoot))
            {
                return new WorkspaceStatusResult
                {
                    VesselId = vessel.Id,
                    HasWorkingDirectory = false,
                    RootPath = normalizedRoot,
                    ActiveMissionCount = activeMissions?.Count ?? 0,
                    ActiveMissions = activeMissions?.ToList() ?? new List<WorkspaceActiveMission>(),
                    Error = "No working directory configured or directory does not exist."
                };
            }

            WorkspaceChangesResult changes = await GetChangesAsync(vessel, token).ConfigureAwait(false);
            return new WorkspaceStatusResult
            {
                VesselId = vessel.Id,
                HasWorkingDirectory = true,
                RootPath = normalizedRoot,
                BranchName = String.IsNullOrWhiteSpace(changes.BranchName) ? null : changes.BranchName,
                IsDirty = changes.IsDirty,
                CommitsAhead = changes.CommitsAhead,
                CommitsBehind = changes.CommitsBehind,
                ActiveMissionCount = activeMissions?.Count ?? 0,
                ActiveMissions = activeMissions?.ToList() ?? new List<WorkspaceActiveMission>(),
                Error = changes.Error
            };
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        public async Task<WorkspaceExecResult> ExecAsync(Vessel vessel, WorkspaceExecRequest request, CancellationToken token = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (String.IsNullOrWhiteSpace(request.Command)) throw new ArgumentException("Command is required.", nameof(request));

            string rootPath = GetWorkspaceRoot(vessel);
            int timeoutSeconds = request.TimeoutSeconds < 1 ? 1 : (request.TimeoutSeconds > 600 ? 600 : request.TimeoutSeconds);

            WorkspaceExecResult result = new WorkspaceExecResult
            {
                Command = request.Command,
                WorkingDirectory = rootPath
            };

            ProcessStartInfo psi = new ProcessStartInfo
            {
                WorkingDirectory = rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd.exe";
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(request.Command);
            }
            else
            {
                psi.FileName = "/bin/sh";
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(request.Command);
            }

            const int maxOutputChars = 256 * 1024;
            Stopwatch stopwatch = Stopwatch.StartNew();
            using Process process = new Process { StartInfo = psi };

            StringBuilder stdout = new StringBuilder();
            StringBuilder stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null && stdout.Length < maxOutputChars) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null && stderr.Length < maxOutputChars) stderr.AppendLine(e.Data); };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                try
                {
                    await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                    result.ExitCode = process.ExitCode;
                }
                catch (OperationCanceledException)
                {
                    result.TimedOut = !token.IsCancellationRequested;
                    try { process.Kill(entireProcessTree: true); } catch { }
                    try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                    result.ExitCode = -1;
                }
            }
            catch (Exception ex)
            {
                stderr.AppendLine("Failed to start command: " + ex.Message);
                result.ExitCode = -1;
            }
            finally
            {
                stopwatch.Stop();
            }

            result.Stdout = stdout.ToString();
            result.Stderr = stderr.ToString();
            result.DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
            return result;
        }

        /// <inheritdoc />
        public async Task<WorkspaceDiffResult> GetDiffAsync(Vessel vessel, string? path = null, CancellationToken token = default)
        {
            string rootPath = GetWorkspaceRoot(vessel);
            WorkspaceDiffResult result = new WorkspaceDiffResult
            {
                Path = String.IsNullOrWhiteSpace(path) ? null : path
            };

            try
            {
                // Fast guard: only diff when this exact path is the root of a git repository. Without it,
                // a non-repo path makes `git diff HEAD` walk up to any ancestor repository and scan it
                // (potentially huge and slow) before failing -- e.g. under a temp directory whose parent
                // happens to be a repo. rev-parse is fast and lets us fail closed immediately.
                string topLevel;
                try
                {
                    topLevel = (await RunGitCommandAsync(rootPath, token, "rev-parse", "--show-toplevel").ConfigureAwait(false)).Trim();
                }
                catch
                {
                    result.Error = "Not a git repository.";
                    return result;
                }

                if (String.IsNullOrWhiteSpace(topLevel)
                    || !PathCanonicalizer.AreEquivalent(rootPath, topLevel))
                {
                    result.Error = "Not a git repository rooted at this path.";
                    return result;
                }

                string diff;
                if (!String.IsNullOrWhiteSpace(path))
                {
                    string normalized = NormalizeRequestedPath(path);
                    if (normalized.Length == 0 || normalized.StartsWith(".git", StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("That path is not accessible.");
                    diff = await RunGitCommandAsync(rootPath, token, "diff", "HEAD", "--", normalized).ConfigureAwait(false);
                }
                else
                {
                    diff = await RunGitCommandAsync(rootPath, token, "diff", "HEAD").ConfigureAwait(false);
                }

                result.Diff = diff ?? String.Empty;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        private static string GetWorkspaceRoot(Vessel vessel)
        {
            if (vessel == null) throw new ArgumentNullException(nameof(vessel));
            if (String.IsNullOrWhiteSpace(vessel.WorkingDirectory))
                throw new DirectoryNotFoundException("No working directory configured for this vessel.");

            string rootPath = Path.GetFullPath(vessel.WorkingDirectory);
            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException("The vessel working directory does not exist.");

            return rootPath;
        }

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

            string normalizedRoot = EnsureTrailingSeparator(rootPath);
            string normalizedCandidate = EnsureTrailingSeparator(candidate);
            bool exactRoot = candidate.Equals(rootPath, StringComparison.OrdinalIgnoreCase);
            if (!exactRoot && !normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
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

        private static string NormalizeRequestedPath(string? path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return String.Empty;

            string normalized = path.Trim().Replace('\\', '/').Trim('/');
            if (Path.IsPathRooted(normalized))
                throw new UnauthorizedAccessException("Absolute paths are not allowed.");

            return normalized;
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

        private static string EnsureTrailingSeparator(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar)
                ? path
                : path + Path.DirectorySeparatorChar;
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

        private static bool IsBinary(byte[] bytes)
        {
            if (bytes.Length < 1)
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

        private static async Task<string> RunGitCommandAsync(string workingDirectory, CancellationToken token, params string[] args)
        {
            ProcessStartInfo psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            // Never prompt for credentials or invoke a pager -- either would hang the request.
            psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
            psi.Environment["GIT_PAGER"] = "cat";
            foreach (string arg in args) psi.ArgumentList.Add(arg);

            using Process process = Process.Start(psi)
                ?? throw new InvalidOperationException("Unable to start git.");

            // Bound every git invocation so a wedged git can never hang the workspace endpoints.
            using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_GitCommandTimeoutSeconds));

            try
            {
                string output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
                string error = await process.StandardError.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException("git exited with code " + process.ExitCode + ": " + error.Trim());

                return output;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new InvalidOperationException("git command timed out after " + _GitCommandTimeoutSeconds + " seconds.");
            }
        }

        private static WorkspaceChangesResult ParseGitStatus(string output)
        {
            WorkspaceChangesResult result = new WorkspaceChangesResult();
            string[] lines = output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (string line in lines)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    ParseBranchHeader(result, line.Substring(3));
                    continue;
                }

                if (line.Length < 3)
                    continue;

                string status = line.Substring(0, 2).Trim();
                string pathText = line.Substring(3).Trim();
                string? originalPath = null;
                string path = pathText;
                int renameIndex = pathText.IndexOf(" -> ", StringComparison.Ordinal);
                if (renameIndex >= 0)
                {
                    originalPath = pathText.Substring(0, renameIndex).Trim().Replace('\\', '/');
                    path = pathText.Substring(renameIndex + 4).Trim();
                }

                result.Changes.Add(new WorkspaceChangeEntry
                {
                    Path = path.Replace('\\', '/'),
                    Status = status,
                    OriginalPath = originalPath
                });
            }

            result.IsDirty = result.Changes.Count > 0;
            return result;
        }

        private static void ParseBranchHeader(WorkspaceChangesResult result, string header)
        {
            string branchName = header;
            int relationIndex = header.IndexOf("...", StringComparison.Ordinal);
            if (relationIndex >= 0)
            {
                branchName = header.Substring(0, relationIndex);
            }
            else
            {
                int statusIndex = header.IndexOf(' ');
                if (statusIndex >= 0)
                    branchName = header.Substring(0, statusIndex);
            }

            result.BranchName = branchName.Trim();

            int aheadIndex = header.IndexOf("ahead ", StringComparison.OrdinalIgnoreCase);
            if (aheadIndex >= 0)
            {
                string aheadText = ExtractNumber(header, aheadIndex + 6);
                int.TryParse(aheadText, out int ahead);
                result.CommitsAhead = ahead;
            }

            int behindIndex = header.IndexOf("behind ", StringComparison.OrdinalIgnoreCase);
            if (behindIndex >= 0)
            {
                string behindText = ExtractNumber(header, behindIndex + 7);
                int.TryParse(behindText, out int behind);
                result.CommitsBehind = behind;
            }
        }

        private static string ExtractNumber(string text, int startIndex)
        {
            StringBuilder builder = new StringBuilder();
            for (int i = startIndex; i < text.Length; i++)
            {
                if (!Char.IsDigit(text[i]))
                    break;
                builder.Append(text[i]);
            }

            return builder.ToString();
        }

        #endregion
    }
}
