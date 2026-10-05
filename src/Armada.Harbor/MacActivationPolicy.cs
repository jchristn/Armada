namespace Armada.Harbor
{
    using System;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Switches the macOS activation policy so Harbor behaves as a menu-bar app: a Dock icon and app menu only
    /// while its window is open (NSApplicationActivationPolicyRegular), and only the menu-bar (tray) icon while
    /// it runs in the background (NSApplicationActivationPolicyAccessory). The bundle's Info.plist sets
    /// LSUIElement so a launch from the login item starts without a Dock icon. Does nothing on other platforms.
    /// Thread safety: not thread safe; call from the UI thread.
    /// </summary>
    public static class MacActivationPolicy
    {
        #region Private-Members

        private const string _ObjCLibrary = "/usr/lib/libobjc.A.dylib";
        private const long _PolicyRegular = 0;
        private const long _PolicyAccessory = 1;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Show Harbor in the Dock and the app switcher (call before showing the window). Never throws.
        /// </summary>
        /// <returns>True when the policy was applied.</returns>
        public static bool ShowInDock()
        {
            return Apply(_PolicyRegular);
        }

        /// <summary>
        /// Remove Harbor from the Dock and the app switcher, leaving the menu-bar icon (call after hiding the
        /// window). Never throws.
        /// </summary>
        /// <returns>True when the policy was applied.</returns>
        public static bool HideFromDock()
        {
            return Apply(_PolicyAccessory);
        }

        /// <summary>
        /// True when this process runs from inside a macOS .app bundle (Contents/MacOS), rather than from
        /// dotnet run or a bare executable.
        /// </summary>
        /// <returns>True inside a bundle.</returns>
        public static bool IsRunningFromBundle()
        {
            if (!OperatingSystem.IsMacOS()) return false;
            string? path = Environment.ProcessPath;
            if (String.IsNullOrEmpty(path)) return false;
            return path.Contains(".app/Contents/MacOS/", StringComparison.Ordinal);
        }

        #endregion

        #region Private-Methods

        private static bool Apply(long policy)
        {
            if (!OperatingSystem.IsMacOS()) return false;

            try
            {
                IntPtr application = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                if (application == IntPtr.Zero) return false;
                return SendLong(application, sel_registerName("setActivationPolicy:"), policy);
            }
            catch (Exception)
            {
                return false;
            }
        }

        [DllImport(_ObjCLibrary)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(_ObjCLibrary)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(_ObjCLibrary, EntryPoint = "objc_msgSend")]
        private static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(_ObjCLibrary, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool SendLong(IntPtr receiver, IntPtr selector, long argument);

        #endregion
    }
}
