namespace Armada.Tui.Services.Credentials
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// The Win32 <c>CREDENTIALW</c> structure used with Credential Manager.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NativeCredential
    {
        /// <summary>
        /// Flags.
        /// </summary>
        public uint Flags;

        /// <summary>
        /// Credential type (1 is generic).
        /// </summary>
        public uint Type;

        /// <summary>
        /// Target name.
        /// </summary>
        public IntPtr TargetName;

        /// <summary>
        /// Comment.
        /// </summary>
        public IntPtr Comment;

        /// <summary>
        /// Last written time.
        /// </summary>
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;

        /// <summary>
        /// Blob size in bytes.
        /// </summary>
        public uint CredentialBlobSize;

        /// <summary>
        /// Blob pointer.
        /// </summary>
        public IntPtr CredentialBlob;

        /// <summary>
        /// Persistence (2 is local machine).
        /// </summary>
        public uint Persist;

        /// <summary>
        /// Attribute count.
        /// </summary>
        public uint AttributeCount;

        /// <summary>
        /// Attributes pointer.
        /// </summary>
        public IntPtr Attributes;

        /// <summary>
        /// Target alias.
        /// </summary>
        public IntPtr TargetAlias;

        /// <summary>
        /// User name.
        /// </summary>
        public IntPtr UserName;
    }
}
