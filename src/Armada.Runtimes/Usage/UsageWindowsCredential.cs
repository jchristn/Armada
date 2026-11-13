namespace Armada.Runtimes.Usage
{
    using System;
    using System.Runtime.InteropServices;
    /// <summary>Native CREDENTIALW layout used only by the read-only Windows credential reader.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct UsageWindowsCredential
    {
        internal uint Flags;
        internal uint Type;
        internal IntPtr TargetName;
        internal IntPtr Comment;
        internal System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        internal uint CredentialBlobSize;
        internal IntPtr CredentialBlob;
        internal uint Persist;
        internal uint AttributeCount;
        internal IntPtr Attributes;
        internal IntPtr TargetAlias;
        internal IntPtr UserName;
    }
}
