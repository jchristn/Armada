namespace Armada.Core.Hosting
{
    using System;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Recoverable policy settings writes compatible with single-file Docker mounts. A protected rollback
    /// journal prevents an interrupted in-place write from becoming the next startup's settings document.
    /// </summary>
    public static class UsageSelectionSettingsFileStore
    {
        private static readonly JsonSerializerOptions _JournalOptions = new JsonSerializerOptions { IncludeFields = false };

        /// <summary>Whether settings load must wait for or recover a policy write. Never read/log the journal as diagnostics.</summary>
        public static bool HasPendingRecovery(string path) => File.Exists(JournalPath(path));

        /// <summary>Restore an interrupted policy write before parsing settings; an active writer is allowed to finish first.</summary>
        public static async Task<bool> RecoverPendingAsync(string path, CancellationToken token = default)
        {
            if (!HasPendingRecovery(path)) return false;
            using FileStream lease = await AcquireAsync(path, token).ConfigureAwait(false);
            return await RecoverUnderLeaseAsync(path, token).ConfigureAwait(false);
        }

        /// <summary>Persist settings with rollback protection, preserving a bind-mounted inode and existing permissions.</summary>
        /// <param name="path">Actual configured settings file.</param>
        /// <param name="content">Validated complete settings JSON; may contain secrets and is never logged.</param>
        /// <param name="token">Cancellation before the commit; after commit the caller must publish the policy.</param>
        public static async Task SaveAsync(string path, string content, CancellationToken token = default)
            => await SaveForTestingAsync(path, content, null, token).ConfigureAwait(false);

        internal static async Task SaveForTestingAsync(string path, string content, Action? afterWrite, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(content);
            using FileStream lease = await AcquireAsync(path, token).ConfigureAwait(false);
            await RecoverUnderLeaseAsync(path, token).ConfigureAwait(false);
            UsageSelectionSettingsRecovery journal = new UsageSelectionSettingsRecovery
            {
                OriginalExists = File.Exists(path),
                OriginalContent = File.Exists(path) ? await File.ReadAllTextAsync(path, token).ConfigureAwait(false) : String.Empty
            };
            if (journal.OriginalExists) Armada.Core.Settings.ArmadaSettings.Parse(journal.OriginalContent);
            string recoveryJson = JsonSerializer.Serialize(journal, _JournalOptions);
            string temporary = JournalPath(path) + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream recovery = new FileStream(temporary, Options(FileMode.CreateNew)))
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(recoveryJson);
                    await recovery.WriteAsync(bytes, token).ConfigureAwait(false);
                    await recovery.FlushAsync(token).ConfigureAwait(false);
                    recovery.Flush(true);
                }
                // The journal is an ordinary adjacent file, so it can be atomically published even
                // when the settings document itself is a single-file Docker bind mount.
                File.Move(temporary, JournalPath(path));
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            try
            {
                token.ThrowIfCancellationRequested();
                if (!journal.OriginalExists)
                {
                    using FileStream create = new FileStream(path, Options(FileMode.CreateNew));
                }
                SettingsFileStore.SaveInPlace(path, content);
                afterWrite?.Invoke();
                File.Delete(JournalPath(path));
            }
            catch
            {
                // Do not lose the durable rollback journal if restoring the old bytes is also impossible.
                await RestoreAsync(path, journal, CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }

        private static async Task<bool> RecoverUnderLeaseAsync(string path, CancellationToken token)
        {
            string journalPath = JournalPath(path);
            if (!File.Exists(journalPath)) return false;
            string json = await File.ReadAllTextAsync(journalPath, token).ConfigureAwait(false);
            UsageSelectionSettingsRecovery recovery = JsonSerializer.Deserialize<UsageSelectionSettingsRecovery>(json, _JournalOptions)
                ?? throw new IOException("Settings recovery document is missing.");
            if (recovery.OriginalExists)
            {
                // A damaged journal must never replace settings with another invalid document.
                Armada.Core.Settings.ArmadaSettings.Parse(recovery.OriginalContent);
            }
            await RestoreAsync(path, recovery, token).ConfigureAwait(false);
            return true;
        }

        private static Task RestoreAsync(string path, UsageSelectionSettingsRecovery recovery, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (recovery.OriginalExists) SettingsFileStore.SaveInPlace(path, recovery.OriginalContent, backupsKept: 0);
            else if (File.Exists(path)) File.Delete(path);
            File.Delete(JournalPath(path));
            return Task.CompletedTask;
        }

        private static async Task<FileStream> AcquireAsync(string path, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("Settings path is required.", nameof(path));
            string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            Directory.CreateDirectory(directory);
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try { return new FileStream(JournalPath(path) + ".lock", Options(FileMode.OpenOrCreate)); }
                catch (IOException) when (DateTime.UtcNow < deadline) { await Task.Delay(20, token).ConfigureAwait(false); }
            }
        }

        private static FileStreamOptions Options(FileMode mode)
        {
            FileStreamOptions options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = FileShare.None, Options = FileOptions.Asynchronous };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            return options;
        }

        private static string JournalPath(string path) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "." + Path.GetFileName(path) + ".usage-selection-rollback");
    }
}
