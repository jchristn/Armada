namespace Armada.Server.Mcp.Tools
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Core;
    using Armada.Core.Database;
    using Armada.Core.Settings;

    /// <summary>
    /// Registers MCP tools for backup and restore operations.
    /// </summary>
    public static class McpBackupTools
    {
        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        /// <summary>
        /// Registers backup and restore MCP tools with the server.
        /// </summary>
        /// <param name="register">Delegate to register each tool.</param>
        /// <param name="database">Database driver for backup operations.</param>
        /// <param name="settings">Armada settings for backup path configuration.</param>
        public static void Register(RegisterToolDelegate register, DatabaseDriver database, ArmadaSettings settings)
        {
            register(
                "armada_backup",
                "Create a ZIP backup of the Armada database and settings for disaster recovery. " +
                "Uses SQLite online backup API for a consistent snapshot.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        outputPath = new { type = "string", description = "Output path for the ZIP file. Default: ~/.armada/backups/armada-backup-{timestamp}.zip" }
                    }
                },
                async (args) =>
                {
                    BackupArgs backupArgs = args != null
                        ? JsonSerializer.Deserialize<BackupArgs>(args.Value, _JsonOptions) ?? new BackupArgs()
                        : new BackupArgs();

                    object result = await McpToolHelpers.PerformBackupAsync(database, settings, backupArgs.OutputPath).ConfigureAwait(false);
                    return result;
                });

            register(
                "armada_restore",
                "Restore the Armada database and settings from a ZIP backup file. " +
                "Creates a safety backup of the current state before overwriting. Server restart recommended after restore.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        filePath = new { type = "string", description = "Path to the ZIP backup file to restore from" }
                    },
                    required = new[] { "filePath" }
                },
                async (args) =>
                {
                    RestoreArgs restoreArgs = args != null
                        ? JsonSerializer.Deserialize<RestoreArgs>(args.Value, _JsonOptions) ?? new RestoreArgs()
                        : new RestoreArgs();

                    if (String.IsNullOrEmpty(restoreArgs.FilePath))
                        throw new ArgumentException("filePath is required");

                    object result = await McpToolHelpers.PerformRestoreAsync(database, settings, restoreArgs.FilePath).ConfigureAwait(false);
                    return result;
                });
        }
    }
}
