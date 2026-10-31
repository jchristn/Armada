namespace Armada.Server
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Armada.Core.Models;
    using Armada.Core.Services;

    public partial class BatteryAccessService
    {
        private const int MaxPackTextBytes = 1024 * 1024;
        private readonly object _PackLock = new object();
        private static readonly UTF8Encoding _PackUtf8 = new UTF8Encoding(false, true);

        /// <summary>Read a pack and validate its current on-disk manifest.</summary>
        public BatteryPackDetail GetPack(AuthContext caller, string? packId)
        {
            RequirePerson(caller);
            lock (_PackLock) return ReadPack(PackDirectory(packId));
        }

        /// <summary>Read a regular UTF-8 text file, refusing binary and oversized content.</summary>
        public BatteryPackTextFile ReadPackFile(AuthContext caller, string? packId, string? path)
        {
            RequirePerson(caller);
            lock (_PackLock)
            {
                string file = PackPath(PackDirectory(packId), path);
                return new BatteryPackTextFile { Path = path!, Content = ReadText(file) };
            }
        }

        /// <summary>Create a server-wide pack. Only global admins may mutate packs.</summary>
        public BatteryPackDetail CreatePack(AuthContext caller, BatteryPackEditRequest? request)
        {
            RequirePackAdmin(caller);
            if (request == null) throw new ArgumentException("A request body is required.");
            CheckManifestJson(request.ManifestJson);
            lock (_PackLock)
            {
                string directory = PackDirectory(request.PackId, false);
                if (Directory.Exists(directory) || File.Exists(directory)) throw new InvalidOperationException("Pack already exists.");
                Directory.CreateDirectory(directory);
                Directory.CreateDirectory(Path.Combine(directory, "fixture"));
                Directory.CreateDirectory(Path.Combine(directory, "tasks"));
                File.WriteAllText(Path.Combine(directory, BatteryPackLoader.ManifestFileName), request.ManifestJson, _PackUtf8);
                return ReadPack(directory);
            }
        }

        /// <summary>Save parseable JSON even when the manifest has validation errors.</summary>
        public BatteryPackDetail UpdatePackManifest(AuthContext caller, string? packId, string? json)
        {
            RequirePackAdmin(caller);
            CheckManifestJson(json);
            lock (_PackLock)
            {
                string directory = PackDirectory(packId);
                RequireIdlePack(directory);
                // Check the whole tree before the validator recursively visits fixture files.
                Tree(directory);
                File.WriteAllText(PackPath(directory, BatteryPackLoader.ManifestFileName), json, _PackUtf8);
                return ReadPack(directory);
            }
        }

        /// <summary>Create or replace a text file; manifests must use the dedicated endpoint.</summary>
        public BatteryPackDetail WritePackFile(AuthContext caller, string? packId, string? path, string? content)
        {
            RequirePackAdmin(caller);
            CheckText(content);
            lock (_PackLock)
            {
                string directory = PackDirectory(packId);
                RequireIdlePack(directory);
                string file = EditableFile(directory, path);
                Tree(directory);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, content, _PackUtf8);
                return ReadPack(directory);
            }
        }

        /// <summary>Delete one file, preserving the manifest.</summary>
        public BatteryPackDetail DeletePackFile(AuthContext caller, string? packId, string? path)
        {
            RequirePackAdmin(caller);
            lock (_PackLock)
            {
                string directory = PackDirectory(packId);
                RequireIdlePack(directory);
                string file = EditableFile(directory, path);
                if (!File.Exists(file)) throw new KeyNotFoundException("Pack file not found.");
                Tree(directory);
                File.Delete(file);
                return ReadPack(directory);
            }
        }

        /// <summary>Delete the entire pack after checking for symlinks.</summary>
        public object DeletePack(AuthContext caller, string? packId)
        {
            RequirePackAdmin(caller);
            lock (_PackLock)
            {
                string directory = PackDirectory(packId);
                RequireIdlePack(directory);
                Tree(directory);
                Directory.Delete(directory, true);
                return new { Deleted = true };
            }
        }

        /// <summary>Validate without writing.</summary>
        public BatteryPackInfo ValidatePack(AuthContext caller, string? packId) => GetPack(caller, packId).Info;

        private void RequireIdlePack(string directory)
        {
            if (_Service.IsPackInUse(directory)) throw new InvalidOperationException("Pack is in use by a running battery. Wait for it to finish before editing or deleting it.");
        }

        private static void RequirePackAdmin(AuthContext caller)
        {
            RequirePerson(caller);
            if (!caller.IsAdmin) throw new UnauthorizedAccessException("Only a global admin can manage server-wide battery packs.");
        }

        private string PackDirectory(string? id, bool mustExist = true)
        {
            if (id == null || !Regex.IsMatch(id, "\\A[a-z0-9][a-z0-9._-]*\\z"))
                throw new ArgumentException("packId must be a safe lowercase slug.");
            string root = _Service.BatteryDirectory ?? throw new InvalidOperationException("Configure Evaluation.BatteryDirectory before creating packs.");
            string directory = PackPath(root, id);
            if (mustExist && !Directory.Exists(directory)) throw new KeyNotFoundException("Battery pack not found.");
            return directory;
        }

        private static string PackPath(string root, string? relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains('\\') || relative.Contains(':'))
                throw new ArgumentException("A relative path inside the pack is required.");
            string[] parts = relative.Split('/');
            if (parts.Any(p => p == ".." || p == "." || p.Length == 0)) throw new ArgumentException("Invalid pack path.");
            string current = Path.GetFullPath(root);
            RefuseLink(current);
            foreach (string part in parts)
            {
                current = Path.Combine(current, part);
                RefuseLink(current);
            }
            return current;
        }

        private static void RefuseLink(string path)
        {
            // LinkTarget also identifies dangling links; do not rely on File.Exists alone.
            if (new FileInfo(path).LinkTarget != null || new DirectoryInfo(path).LinkTarget != null)
                throw new ArgumentException("Symbolic links are not allowed in managed packs.");
            if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Reparse points are not allowed in managed packs.");
        }

        private static string EditableFile(string directory, string? path)
        {
            string file = PackPath(directory, path);
            if (String.Equals(Path.GetFileName(file), BatteryPackLoader.ManifestFileName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use the manifest editor for battery-pack.json; it cannot be deleted separately.");
            if (!path!.StartsWith("fixture/", StringComparison.Ordinal) && !path.StartsWith("tasks/", StringComparison.Ordinal))
                throw new ArgumentException("Editable files must be under fixture/ or tasks/.");
            return file;
        }

        private static void CheckText(string? text)
        {
            if (text == null || _PackUtf8.GetByteCount(text) > MaxPackTextBytes || text.Any(c => Char.IsControl(c) && c != '\n' && c != '\r' && c != '\t'))
                throw new ArgumentException("Only UTF-8 text up to 1 MiB is supported.");
        }

        private static void CheckManifestJson(string? json)
        {
            CheckText(json);
            try { using JsonDocument document = JsonDocument.Parse(json!); }
            catch (JsonException) { throw new ArgumentException("Manifest must be valid JSON."); }
        }

        private static string ReadText(string file)
        {
            if (!File.Exists(file)) throw new KeyNotFoundException("Pack file not found.");
            using FileStream stream = File.OpenRead(file);
            if (stream.Length > MaxPackTextBytes) throw new ArgumentException("Text files must be at most 1 MiB.");
            byte[] bytes = new byte[stream.Length];
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0) break;
                offset += read;
            }
            try
            {
                string text = _PackUtf8.GetString(bytes, 0, offset).TrimStart('\uFEFF');
                CheckText(text);
                return text;
            }
            catch (DecoderFallbackException) { throw new ArgumentException("Binary files cannot be edited as UTF-8 text."); }
        }

        private static List<BatteryPackFile> Tree(string directory)
        {
            List<BatteryPackFile> files = new List<BatteryPackFile>();
            int entries = 0;
            void Visit(string current)
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(current).OrderBy(p => p, StringComparer.Ordinal))
                {
                    if (++entries > CollectionLimits.MaxConfigItems) throw new ArgumentException("Pack tree exceeds the management limit of 256 entries.");
                    RefuseLink(entry);
                    if (Directory.Exists(entry)) Visit(entry);
                    else files.Add(new BatteryPackFile { Path = Path.GetRelativePath(directory, entry).Replace('\\', '/'), Size = new FileInfo(entry).Length });
                }
            }
            Visit(directory);
            return files;
        }

        private static BatteryPackDetail ReadPack(string directory)
        {
            List<BatteryPackFile> files = Tree(directory);
            return new BatteryPackDetail
            {
                Id = Path.GetFileName(directory), Files = files,
                ManifestJson = ReadText(PackPath(directory, BatteryPackLoader.ManifestFileName)),
                Info = BatteryPackLoader.Load(directory).ToInfo()
            };
        }
    }
}
