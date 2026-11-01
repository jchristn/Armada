namespace Armada.Runtimes.Usage
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    /// <summary>Read existing credentials only on the execution host, without refresh or write-back.</summary>
    internal static class UsageCredentialReader
    {
        internal static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        internal static string ClaudeDirectory => Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        internal static string GeminiDirectory => Path.Combine(Environment.GetEnvironmentVariable("GEMINI_CLI_HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini");
        internal static Task<UsageOAuthCredentials?> ReadAsync(string path, CancellationToken token, int maximumBytes = 65536) => ReadFileAsync<UsageOAuthCredentials>(path, token, maximumBytes);
        internal static async Task<T?> ReadFileAsync<T>(string path, CancellationToken token, int maximumBytes = 65536) where T : class
        {
            if (!File.Exists(path)) return null;
            FileInfo file = new FileInfo(path);
            if (file.Length > maximumBytes) return null;
            try { return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), Options); }
            catch (JsonException) { return null; }
        }
        internal static async Task<UsageOAuthCredentials?> ClaudeAsync(CancellationToken token)
        {
            UsageOAuthCredentials? file = await ReadAsync(Path.Combine(ClaudeDirectory, ".credentials.json"), token).ConfigureAwait(false);
            if (file?.ClaudeAiOauth != null) return file.ClaudeAiOauth;
            if (!OperatingSystem.IsMacOS() || !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"))) return null;
            // Read only the CLI's existing keychain item. No keychain write, unlock or credential refresh.
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo("/usr/bin/security") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                foreach (string argument in new[] { "find-generic-password", "-s", "Claude Code-credentials", "-w" }) process.StartInfo.ArgumentList.Add(argument);
                process.Start();
                try
                {
                    char[] buffer = new char[65537];
                    int count = 0;
                    while (count < buffer.Length)
                    {
                        int read = await process.StandardOutput.ReadAsync(buffer.AsMemory(count), token).ConfigureAwait(false);
                        if (read == 0) break;
                        count += read;
                    }
                    if (count > 65536) return null;
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                    if (process.ExitCode != 0) return null;
                    try { return JsonSerializer.Deserialize<UsageOAuthCredentials>(new string(buffer, 0, count), Options)?.ClaudeAiOauth; }
                    catch (JsonException) { return null; }
                }
                finally { if (!process.HasExited) process.Kill(true); }
            }
        }
    }
}
