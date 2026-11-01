namespace Armada.Runtimes.Usage
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Org.BouncyCastle.Crypto.Generators;

    /// <summary>Read-only Gemini CLI 0.66 native/legacy/encrypted stores. Never runs CLI migration or refresh code.</summary>
    public static class GeminiCredentialStore
    {
        internal static async Task<UsageOAuthCredentials?> ReadAsync(CancellationToken token)
        {
            string? password = null;
            bool forceFile = Environment.GetEnvironmentVariable("GEMINI_FORCE_FILE_STORAGE") == "true" || Environment.GetEnvironmentVariable("GEMINI_FORCE_ENCRYPTED_FILE_STORAGE") == "true";
            if (!forceFile && String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_CLI_HOME")))
            {
                if (OperatingSystem.IsMacOS()) password = await UsageCredentialCommand.ReadAsync("/usr/bin/security", new[] { "find-generic-password", "-s", "gemini-cli-oauth", "-a", "main-account", "-w" }, token).ConfigureAwait(false);
                else if (OperatingSystem.IsWindows()) password = UsageWindowsCredentialReader.ReadGemini();
                else if (OperatingSystem.IsLinux()) password = await UsageCredentialCommand.ReadAsync("secret-tool", new[] { "lookup", "service", "gemini-cli-oauth", "account", "main-account" }, token).ConfigureAwait(false);
            }
            string encryptedPath = Path.Combine(UsageCredentialReader.GeminiDirectory, "gemini-credentials.json");
            if (password == null && File.Exists(encryptedPath) && new FileInfo(encryptedPath).Length <= 1024 * 1024)
                password = ReadEncryptedAccount(await File.ReadAllTextAsync(encryptedPath, token).ConfigureAwait(false), Environment.MachineName, Environment.UserName);
            if (password != null)
            {
                try
                {
                    UsageOAuthCredentials? stored = JsonSerializer.Deserialize<UsageOAuthCredentials>(password, UsageCredentialReader.Options);
                    if (stored?.Token?.AccessToken != null) return new UsageOAuthCredentials { GoogleAccessToken = stored.Token.AccessToken };
                }
                catch (JsonException) { return null; }
            }
            return await UsageCredentialReader.ReadAsync(Path.Combine(UsageCredentialReader.GeminiDirectory, "oauth_creds.json"), token).ConfigureAwait(false);
        }

        /// <summary>Decode the existing vendor file format; only the main-account entry is returned. No file is written.</summary>
        public static string? ReadEncryptedAccount(string encrypted, string hostName, string userName)
        {
            if (encrypted == null || encrypted.Length > 1024 * 1024) return null;
            byte[]? key = null;
            byte[]? plaintext = null;
            try
            {
                string[] parts = encrypted.Trim().Split(':');
                if (parts.Length != 3 || parts[0].Length != 24 || parts[1].Length != 32) return null;
                byte[] nonce = Convert.FromHexString(parts[0]);
                byte[] tag = Convert.FromHexString(parts[1]);
                byte[] ciphertext = Convert.FromHexString(parts[2]);
                key = SCrypt.Generate(Encoding.UTF8.GetBytes("gemini-cli-oauth"), Encoding.UTF8.GetBytes(hostName + "-" + userName + "-gemini-cli"), 16384, 8, 1, 32);
                plaintext = new byte[ciphertext.Length];
                using (AesGcm aes = new AesGcm(key, 16)) aes.Decrypt(nonce, ciphertext, tag, plaintext);
                Dictionary<string, Dictionary<string, string>>? store = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(plaintext);
                return store != null && store.TryGetValue("gemini-cli-oauth", out Dictionary<string, string>? service) && service.TryGetValue("main-account", out string? account) ? account : null;
            }
            catch (Exception error) when (error is FormatException || error is CryptographicException || error is JsonException || error is ArgumentException) { return null; }
            finally { if (key != null) CryptographicOperations.ZeroMemory(key); if (plaintext != null) CryptographicOperations.ZeroMemory(plaintext); }
        }
    }
}
