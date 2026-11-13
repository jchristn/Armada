namespace Armada.Core.Storage
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Settings;

    /// <summary>Collects bounded logical file lengths without reading contents or following links. Each invocation owns its state.</summary>
    public class StorageFileCollector
    {
        /// <summary>Measures exclusive configured roots. More specific roots own their subtree. Call on a background worker.</summary>
        /// <exception cref="ArgumentNullException">Roots or settings are null.</exception>
        public Task<StorageHostReport> CollectAsync(IEnumerable<StorageLocation> roots, StorageSettings settings, CancellationToken token = default)
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return Task.Run(() => Collect(roots, settings, token), token);
        }

        /// <summary>Whether a normalized path belongs to a root, using the host's filesystem comparison rules.</summary>
        public static bool IsUnder(string path, string root)
        {
            StringComparison comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return String.Equals(path, root, comparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison);
        }

        private StorageHostReport Collect(IEnumerable<StorageLocation> roots, StorageSettings settings, CancellationToken token)
        {
            StorageHostReport report = new StorageHostReport { Name = Environment.MachineName, Status = "Complete", IntervalSeconds = settings.IntervalSeconds };
            Stopwatch watch = Stopwatch.StartNew();
            StringComparer comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            HashSet<string> paths = new HashSet<string>(comparer);
            foreach (StorageLocation root in roots)
            {
                token.ThrowIfCancellationRequested();
                if (String.IsNullOrWhiteSpace(root.Path)) continue;
                try
                {
                    string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Path));
                    if (!paths.Add(path)) continue;
                    if (report.Locations.Count >= settings.MaxLocations)
                    {
                        report.Warnings.Add("LocationLimit");
                        report.Status = "Partial";
                        break;
                    }
                    report.Locations.Add(new StorageLocation { Path = path, Category = root.Category, EntityId = root.EntityId, Activity = root.Activity });
                }
                catch (ArgumentException) { AddReportWarning(report, "InvalidPath"); report.Status = "Partial"; }
            }
            report.Locations = report.Locations.OrderByDescending(r => r.Path.Length).ToList();
            long examined = 0;
            foreach (StorageLocation location in report.Locations)
            {
                token.ThrowIfCancellationRequested();
                if (examined >= settings.MaxEntries || watch.Elapsed.TotalSeconds >= settings.TimeoutSeconds)
                {
                    location.Status = "Unavailable";
                    location.Warnings.Add("BudgetExceeded");
                    report.Status = "Partial";
                    continue;
                }
                try
                {
                    if (HasLinkedAncestor(location.Path))
                    {
                        location.Status = "Skipped";
                        location.Warnings.Add("LinkSkipped");
                        report.Status = "Partial";
                        continue;
                    }
                    FileAttributes attributes = File.GetAttributes(location.Path);
                    location.Status = "Complete";
                    location.Bytes = 0;
                    Stack<string> pending = new Stack<string>();
                    pending.Push(location.Path);
                    while (pending.Count > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        if (++examined > settings.MaxEntries || watch.Elapsed.TotalSeconds >= settings.TimeoutSeconds)
                        {
                            Warn(location, "BudgetExceeded");
                            break;
                        }
                        string path = pending.Pop();
                        try
                        {
                            attributes = File.GetAttributes(path);
                            if ((attributes & FileAttributes.ReparsePoint) != 0) { Warn(location, "LinkSkipped"); continue; }
                            if ((attributes & FileAttributes.Directory) != 0)
                            {
                                // Enumerate lazily so a directory containing millions of entries cannot allocate a giant array.
                                foreach (string child in Directory.EnumerateFileSystemEntries(path))
                                {
                                    token.ThrowIfCancellationRequested();
                                    if (report.Locations.Any(other => other.Path.Length > location.Path.Length && IsUnder(child, other.Path))) continue;
                                    if (examined + pending.Count >= settings.MaxEntries || watch.Elapsed.TotalSeconds >= settings.TimeoutSeconds)
                                    {
                                        Warn(location, "BudgetExceeded");
                                        break;
                                    }
                                    pending.Push(child);
                                }
                            }
                            else
                            {
                                FileInfo file = new FileInfo(path);
                                long size = file.Length;
                                DateTime written = file.LastWriteTimeUtc;
                                location.Bytes += size;
                                location.FileCount++;
                                if (!location.OldestWriteUtc.HasValue || written < location.OldestWriteUtc) location.OldestWriteUtc = written;
                                if (!location.NewestWriteUtc.HasValue || written > location.NewestWriteUtc) location.NewestWriteUtc = written;
                                location.LargestFiles.Add(new StorageFile { Path = path, Bytes = size, LastWriteUtc = written });
                                location.LargestFiles = location.LargestFiles.OrderByDescending(f => f.Bytes).Take(settings.LargestFileLimit).ToList();
                            }
                        }
                        catch (IOException) { Warn(location, "FileChangedOrUnreadable"); }
                        catch (UnauthorizedAccessException) { Warn(location, "AccessDenied"); }
                    }
                }
                catch (FileNotFoundException) { location.Status = "Missing"; }
                catch (DirectoryNotFoundException) { location.Status = "Missing"; }
                catch (IOException) { location.Status = "Unavailable"; location.Warnings.Add("Unreadable"); }
                catch (UnauthorizedAccessException) { location.Status = "Unavailable"; location.Warnings.Add("AccessDenied"); }
                if (location.Status != "Complete" && location.Status != "Missing") report.Status = "Partial";
            }
            try
            {
                List<DriveInfo> drives = DriveInfo.GetDrives().OrderByDescending(d => d.RootDirectory.FullName.Length).ToList();
                HashSet<string> measuredMounts = new HashSet<string>(comparer);
                foreach (StorageLocation location in report.Locations)
                {
                    DriveInfo? drive = drives.FirstOrDefault(d => IsUnder(location.Path, Path.TrimEndingDirectorySeparator(d.RootDirectory.FullName)));
                    if (drive == null || !measuredMounts.Add(drive.RootDirectory.FullName)) continue;
                    if (report.Volumes.Count >= StorageHostReport.MaxVolumes) { AddReportWarning(report, "VolumeLimit"); break; }
                    StorageVolume volume = new StorageVolume { Path = drive.RootDirectory.FullName };
                    try { volume.TotalBytes = drive.TotalSize; volume.AvailableBytes = drive.AvailableFreeSpace; volume.Status = "Complete"; }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    report.Volumes.Add(volume);
                }
            }
            catch (IOException) { AddReportWarning(report, "VolumesUnavailable"); }
            report.MeasuredUtc = DateTime.UtcNow;
            return report;
        }

        /// <summary>Checks each existing path component for links before discovering children.</summary>
        /// <exception cref="IOException">A component is missing or unreadable.</exception>
        /// <exception cref="UnauthorizedAccessException">A component is inaccessible.</exception>
        public static bool HasLinkedAncestor(string path)
        {
            string? current = path;
            while (!String.IsNullOrEmpty(current))
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
                current = Path.GetDirectoryName(current);
            }
            return false;
        }

        private static void Warn(StorageLocation location, string code)
        {
            location.Status = "Partial";
            if (location.Warnings.Count < 10 && !location.Warnings.Contains(code)) location.Warnings.Add(code);
        }

        private static void AddReportWarning(StorageHostReport report, string code)
        {
            if (report.Warnings.Count < StorageHostReport.MaxWarnings && !report.Warnings.Contains(code)) report.Warnings.Add(code);
        }
    }
}
