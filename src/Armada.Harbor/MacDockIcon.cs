namespace Armada.Harbor
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Sets the macOS Dock icon for the running process. Harbor ships as a plain executable rather than an
    /// .app bundle, and Avalonia does not apply the window icon to the Dock, so without this macOS shows the
    /// generic "exec" icon. Uses the Objective-C runtime directly: NSApplication.sharedApplication
    /// .applicationIconImage = [[NSImage alloc] initWithData:png]. Must be called on the main (UI) thread.
    /// Thread safety: not thread safe; call once from the UI thread.
    /// </summary>
    public static class MacDockIcon
    {
        #region Private-Members

        private const string _ObjCLibrary = "/usr/lib/libobjc.A.dylib";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Apply a PNG image as the Dock icon. Does nothing on platforms other than macOS. Never throws; a
        /// failure leaves the default icon in place.
        /// </summary>
        /// <param name="png">PNG image stream; read to the end but not disposed.</param>
        /// <returns>True when the icon was applied.</returns>
        public static bool TryApply(Stream png)
        {
            if (!OperatingSystem.IsMacOS() || png == null) return false;

            try
            {
                byte[] bytes;
                using (MemoryStream buffer = new MemoryStream())
                {
                    png.CopyTo(buffer);
                    bytes = buffer.ToArray();
                }

                if (bytes.Length < 1) return false;

                IntPtr nsData = IntPtr.Zero;
                GCHandle pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                try
                {
                    nsData = SendPointerLength(
                        objc_getClass("NSData"),
                        sel_registerName("dataWithBytes:length:"),
                        pinned.AddrOfPinnedObject(),
                        (nuint)bytes.Length);
                }
                finally
                {
                    pinned.Free();
                }

                if (nsData == IntPtr.Zero) return false;

                IntPtr allocated = Send(objc_getClass("NSImage"), sel_registerName("alloc"));
                IntPtr image = SendPointer(allocated, sel_registerName("initWithData:"), nsData);
                if (image == IntPtr.Zero) return false;

                IntPtr application = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                if (application == IntPtr.Zero) return false;

                SendPointer(application, sel_registerName("setApplicationIconImage:"), image);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        #endregion

        #region Private-Methods

        [DllImport(_ObjCLibrary)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(_ObjCLibrary)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(_ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(_ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendPointer(IntPtr receiver, IntPtr selector, IntPtr argument);

        [DllImport(_ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendPointerLength(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

        #endregion
    }
}
