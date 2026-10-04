namespace Armada.Tui.Services.Credentials
{
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Windows Credential Manager store (generic credentials named <c>armada-tui:&lt;key&gt;</c>). Thread-safe.
    /// </summary>
    public class WindowsCredentialStore : ICredentialStore
    {
        #region Public-Members

        /// <inheritdoc />
        public string Name
        {
            get { return "wincred"; }
        }

        /// <summary>
        /// Target name prefix. Default <c>armada-tui:</c>.
        /// </summary>
        public string Prefix { get; set; } = "armada-tui:";

        /// <summary>
        /// True on Windows.
        /// </summary>
        public bool IsAvailable
        {
            get { return OperatingSystem.IsWindows(); }
        }

        #endregion

        #region Private-Members

        private const uint _GenericType = 1;
        private const uint _PersistLocalMachine = 2;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<string?> GetAsync(string key, CancellationToken token = default)
        {
            if (!IsAvailable) return Task.FromResult<string?>(null);
            if (!WindowsCredentialNative.CredRead(Prefix + key, _GenericType, 0, out IntPtr pointer)) return Task.FromResult<string?>(null);
            try
            {
                NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(pointer);
                if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return Task.FromResult<string?>(null);
                byte[] bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Task.FromResult<string?>(Encoding.Unicode.GetString(bytes));
            }
            finally
            {
                WindowsCredentialNative.CredFree(pointer);
            }
        }

        /// <inheritdoc />
        public Task<bool> SetAsync(string key, string secret, CancellationToken token = default)
        {
            if (!IsAvailable) return Task.FromResult(false);
            byte[] bytes = Encoding.Unicode.GetBytes(secret ?? "");
            IntPtr target = Marshal.StringToCoTaskMemUni(Prefix + key);
            IntPtr user = Marshal.StringToCoTaskMemUni(key);
            IntPtr blob = Marshal.AllocCoTaskMem(Math.Max(1, bytes.Length));
            try
            {
                Marshal.Copy(bytes, 0, blob, bytes.Length);
                NativeCredential credential = new NativeCredential();
                credential.Type = _GenericType;
                credential.TargetName = target;
                credential.UserName = user;
                credential.CredentialBlob = blob;
                credential.CredentialBlobSize = (uint)bytes.Length;
                credential.Persist = _PersistLocalMachine;
                return Task.FromResult(WindowsCredentialNative.CredWrite(ref credential, 0));
            }
            finally
            {
                Marshal.FreeCoTaskMem(target);
                Marshal.FreeCoTaskMem(user);
                Marshal.FreeCoTaskMem(blob);
            }
        }

        /// <inheritdoc />
        public Task DeleteAsync(string key, CancellationToken token = default)
        {
            if (IsAvailable) WindowsCredentialNative.CredDelete(Prefix + key, _GenericType, 0);
            return Task.CompletedTask;
        }

        #endregion
    }
}
