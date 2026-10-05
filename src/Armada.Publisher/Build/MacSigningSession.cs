namespace Armada.Publisher.Build
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Armada.Publisher.Manifest;

    /// <summary>
    /// Resolves how macOS artifacts are signed for one channel run and performs the signing, packaging
    /// signature, and notarization steps.
    ///
    /// Signing is driven entirely by environment variables named in the manifest:
    /// <list type="bullet">
    /// <item>APPLE_CERT_BASE64 / APPLE_CERT_PASSWORD: a .p12 imported into a temporary keychain (CI).</item>
    /// <item>APPLE_SIGNING_IDENTITY / APPLE_INSTALLER_IDENTITY: identities already in the login keychain (local).</item>
    /// <item>APPLE_NOTARY_KEY / APPLE_NOTARY_KEY_ID / APPLE_NOTARY_ISSUER: App Store Connect API key for notarytool.</item>
    /// </list>
    /// With none of them set, bundles and binaries are ad-hoc signed (codesign -s -), which Apple Silicon
    /// requires to execute anything, and packages are left unsigned. A clear log line says so.
    /// </summary>
    public class MacSigningSession : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Codesign identity in use, or "-" for ad-hoc signing.
        /// </summary>
        public string Identity
        {
            get { return _Identity; }
        }

        /// <summary>
        /// Installer (productbuild) identity, or empty when packages are left unsigned.
        /// </summary>
        public string InstallerIdentity
        {
            get { return _InstallerIdentity; }
        }

        /// <summary>
        /// True when no Developer ID identity is available and artifacts are ad-hoc signed.
        /// </summary>
        public bool IsAdHoc
        {
            get { return _Identity == "-"; }
        }

        /// <summary>
        /// True when notarization credentials are present and a real identity is in use.
        /// </summary>
        public bool CanNotarize
        {
            get { return !IsAdHoc && !string.IsNullOrEmpty(_NotaryKey) && !string.IsNullOrEmpty(_NotaryKeyId) && !string.IsNullOrEmpty(_NotaryIssuer); }
        }

        #endregion

        #region Private-Members

        private readonly string _LogPrefix;
        private readonly string _WorkDirectory;
        private string _Identity = "-";
        private string _InstallerIdentity = string.Empty;
        private string _KeychainPath = string.Empty;
        private string _NotaryKey = string.Empty;
        private string _NotaryKeyId = string.Empty;
        private string _NotaryIssuer = string.Empty;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Resolve the signing configuration from the environment, importing a certificate into a
        /// temporary keychain when one is supplied.
        /// </summary>
        /// <param name="settings">macOS signing settings from the manifest.</param>
        /// <param name="workDirectory">Scratch directory for the temporary keychain and key files.</param>
        /// <param name="logPrefix">Prefix for log lines, for example "[dmg]".</param>
        public MacSigningSession(MacosSigningSettings settings, string workDirectory, string logPrefix)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _WorkDirectory = workDirectory ?? throw new ArgumentNullException(nameof(workDirectory));
            _LogPrefix = logPrefix ?? string.Empty;
            Directory.CreateDirectory(_WorkDirectory);

            string certBase64 = Env(settings.CertBase64Secret);
            string certPassword = Env(settings.CertPasswordSecret);
            string identityOverride = Env(settings.SigningIdentitySecret);
            string installerOverride = Env(settings.InstallerIdentitySecret);

            _NotaryKey = Env(settings.NotaryKeySecret);
            _NotaryKeyId = Env(settings.NotaryKeyIdSecret);
            _NotaryIssuer = Env(settings.NotaryIssuerSecret);

            if (!string.IsNullOrEmpty(certBase64))
            {
                ImportCertificate(certBase64, certPassword);
                List<MacSigningIdentity> identities = ListIdentities();
                _Identity = FirstNonEmpty(identityOverride, MacSigningIdentity.Select(identities, settings.DeveloperIdApplication, "Developer ID Application"));
                _InstallerIdentity = FirstNonEmpty(installerOverride, MacSigningIdentity.Select(identities, settings.DeveloperIdInstaller, "Developer ID Installer"));
                if (string.IsNullOrEmpty(_Identity)) _Identity = "-";
            }
            else if (!string.IsNullOrEmpty(identityOverride))
            {
                _Identity = identityOverride;
                _InstallerIdentity = installerOverride;
            }

            if (IsAdHoc)
            {
                Console.WriteLine(_LogPrefix + " no Apple signing identity (" + settings.CertBase64Secret + " / " + settings.SigningIdentitySecret
                    + " not set): ad-hoc signing only; artifacts are NOT notarized and Gatekeeper will warn on first open.");
            }
            else
            {
                Console.WriteLine(_LogPrefix + " signing with identity '" + _Identity + "'.");
                if (!CanNotarize)
                {
                    Console.WriteLine(_LogPrefix + " notarization credentials (" + settings.NotaryKeySecret + ", " + settings.NotaryKeyIdSecret + ", "
                        + settings.NotaryIssuerSecret + ") not set: signed but NOT notarized.");
                }
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Sign a single Mach-O binary (or a whole .app bundle when the path is a directory).
        /// </summary>
        /// <param name="path">Binary or bundle path.</param>
        /// <param name="entitlementsPath">Entitlements plist used with the hardened runtime, or null.</param>
        public void Sign(string path, string? entitlementsPath)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));

            List<string> arguments = new List<string> { "--force" };
            if (IsAdHoc)
            {
                arguments.AddRange(new string[] { "--sign", "-" });
            }
            else
            {
                arguments.AddRange(new string[] { "--sign", _Identity, "--options", "runtime", "--timestamp" });
                if (!string.IsNullOrEmpty(entitlementsPath)) arguments.AddRange(new string[] { "--entitlements", entitlementsPath });
                if (!string.IsNullOrEmpty(_KeychainPath)) arguments.AddRange(new string[] { "--keychain", _KeychainPath });
            }

            arguments.Add(path);
            ProcessRunner.Run("codesign", arguments);
        }

        /// <summary>
        /// Sign a disk image. Disk images are only signed with a real identity; ad-hoc signing a .dmg adds nothing.
        /// </summary>
        /// <param name="dmgPath">Disk image path.</param>
        public void SignDiskImage(string dmgPath)
        {
            if (IsAdHoc)
            {
                Console.WriteLine(_LogPrefix + " disk image left unsigned (no Developer ID identity).");
                return;
            }

            List<string> arguments = new List<string> { "--force", "--sign", _Identity, "--timestamp" };
            if (!string.IsNullOrEmpty(_KeychainPath)) arguments.AddRange(new string[] { "--keychain", _KeychainPath });
            arguments.Add(dmgPath);
            ProcessRunner.Run("codesign", arguments);
        }

        /// <summary>
        /// Append the productbuild signing arguments, or log that the package is unsigned.
        /// </summary>
        /// <param name="productbuildArguments">Argument list to extend.</param>
        public void AddInstallerSigning(List<string> productbuildArguments)
        {
            if (productbuildArguments == null) throw new ArgumentNullException(nameof(productbuildArguments));

            if (string.IsNullOrEmpty(_InstallerIdentity))
            {
                Console.WriteLine(_LogPrefix + " no Developer ID Installer identity: package is unsigned and Gatekeeper will warn on open.");
                return;
            }

            productbuildArguments.AddRange(new string[] { "--sign", _InstallerIdentity, "--timestamp" });
            if (!string.IsNullOrEmpty(_KeychainPath)) productbuildArguments.AddRange(new string[] { "--keychain", _KeychainPath });
        }

        /// <summary>
        /// Submit a .dmg or .pkg to Apple's notary service, wait for the verdict, and staple the ticket.
        /// Does nothing (with a log line) when notarization is not possible.
        /// </summary>
        /// <param name="path">Artifact to notarize.</param>
        public void NotarizeAndStaple(string path)
        {
            if (!CanNotarize)
            {
                Console.WriteLine(_LogPrefix + " skipping notarization for " + Path.GetFileName(path) + ".");
                return;
            }

            string keyPath = Path.Combine(_WorkDirectory, "notary-" + Guid.NewGuid().ToString("N") + ".p8");
            File.WriteAllText(keyPath, _NotaryKey);
            try
            {
                ProcessRunner.Run("xcrun", new List<string>
                {
                    "notarytool", "submit", path,
                    "--key", keyPath,
                    "--key-id", _NotaryKeyId,
                    "--issuer", _NotaryIssuer,
                    "--wait"
                });
                ProcessRunner.Run("xcrun", new List<string> { "stapler", "staple", path });
            }
            finally
            {
                if (File.Exists(keyPath)) File.Delete(keyPath);
            }
        }

        /// <summary>
        /// Delete the temporary keychain, if one was created.
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;

            if (!string.IsNullOrEmpty(_KeychainPath))
            {
                try
                {
                    ProcessRunner.Run("security", new List<string> { "delete-keychain", _KeychainPath });
                }
                catch (Exception ex)
                {
                    Console.WriteLine(_LogPrefix + " could not delete temporary keychain: " + ex.Message);
                }
            }

            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private static string Env(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            return Environment.GetEnvironmentVariable(name) ?? string.Empty;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrEmpty(value)) return value;
            }
            return string.Empty;
        }

        private void ImportCertificate(string certBase64, string certPassword)
        {
            string keychainPassword = Guid.NewGuid().ToString("N");
            string p12Path = Path.Combine(_WorkDirectory, "signing-" + Guid.NewGuid().ToString("N") + ".p12");
            _KeychainPath = Path.Combine(_WorkDirectory, "armada-signing-" + Guid.NewGuid().ToString("N") + ".keychain-db");

            File.WriteAllBytes(p12Path, Convert.FromBase64String(certBase64));
            try
            {
                ProcessRunner.Run("security", new List<string> { "create-keychain", "-p", keychainPassword, _KeychainPath });
                ProcessRunner.Run("security", new List<string> { "set-keychain-settings", "-lut", "21600", _KeychainPath });
                ProcessRunner.Run("security", new List<string> { "unlock-keychain", "-p", keychainPassword, _KeychainPath });
                ProcessRunner.Run("security", new List<string>
                {
                    "import", p12Path,
                    "-k", _KeychainPath,
                    "-P", certPassword,
                    "-T", "/usr/bin/codesign",
                    "-T", "/usr/bin/productbuild",
                    "-T", "/usr/bin/productsign"
                });
                ProcessRunner.Run("security", new List<string>
                {
                    "set-key-partition-list", "-S", "apple-tool:,apple:,codesign:", "-s", "-k", keychainPassword, _KeychainPath
                });
            }
            finally
            {
                if (File.Exists(p12Path)) File.Delete(p12Path);
            }
        }

        private List<MacSigningIdentity> ListIdentities()
        {
            if (string.IsNullOrEmpty(_KeychainPath)) return new List<MacSigningIdentity>();
            string output = ProcessRunner.Capture("security", new List<string> { "find-identity", "-v", _KeychainPath });
            return MacSigningIdentity.ParseFindIdentityOutput(output);
        }

        #endregion
    }
}
