namespace Armada.Runtimes.Usage
{
    using System;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;
    using System.Text;
    /// <summary>Read one existing keytar credential. Never writes, enumerates or refreshes credentials.</summary>
    internal static class UsageWindowsCredentialReader
    {
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool Read(string target, uint type, uint flags, out IntPtr credential);
        [DllImport("advapi32.dll", EntryPoint = "CredFree")]
        private static extern void Free(IntPtr credential);
        internal static string? ReadGemini()
        {
            if (!OperatingSystem.IsWindows() || !Read("gemini-cli-oauth/main-account", 1, 0, out IntPtr pointer)) return null;
            try
            {
                UsageWindowsCredential credential = Marshal.PtrToStructure<UsageWindowsCredential>(pointer);
                if (credential.CredentialBlobSize == 0 || credential.CredentialBlobSize > 65536) return null;
                byte[] bytes = new byte[credential.CredentialBlobSize];
                try { Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length); return Encoding.UTF8.GetString(bytes); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            finally { Free(pointer); }
        }
    }
}
