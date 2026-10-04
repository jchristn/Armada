namespace Armada.Tui.Services.Credentials
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// P/Invoke declarations for Windows Credential Manager (advapi32).
    /// </summary>
    internal static class WindowsCredentialNative
    {
        /// <summary>
        /// Read a credential.
        /// </summary>
        /// <param name="target">Target name.</param>
        /// <param name="type">Credential type.</param>
        /// <param name="flags">Reserved.</param>
        /// <param name="credential">Receives a pointer to the credential; free with <see cref="CredFree"/>.</param>
        /// <returns>True on success.</returns>
        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        /// <summary>
        /// Write a credential.
        /// </summary>
        /// <param name="credential">Credential.</param>
        /// <param name="flags">Reserved.</param>
        /// <returns>True on success.</returns>
        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredWrite(ref NativeCredential credential, uint flags);

        /// <summary>
        /// Delete a credential.
        /// </summary>
        /// <param name="target">Target name.</param>
        /// <param name="type">Credential type.</param>
        /// <param name="flags">Reserved.</param>
        /// <returns>True on success.</returns>
        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredDelete(string target, uint type, uint flags);

        /// <summary>
        /// Free a buffer returned by <see cref="CredRead"/>.
        /// </summary>
        /// <param name="buffer">Buffer.</param>
        [DllImport("advapi32.dll", SetLastError = false)]
        public static extern void CredFree(IntPtr buffer);
    }
}
