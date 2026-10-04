namespace Armada.Tui.Services
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.IO;
    using System.Text;
    using System.Threading.Tasks;
    using TUIKit.Hosting;

    /// <summary>
    /// The outside world: open URLs in the browser, edit long text in <c>$VISUAL</c>/<c>$EDITOR</c> (the terminal is
    /// handed over with TUIKit's <c>SuspendAsync</c>), and save or load files (exports, backups). Call
    /// <see cref="EditTextAsync"/> on the UI loop thread.
    /// </summary>
    public class ExternalService
    {
        #region Public-Members

        /// <summary>
        /// Opens a URL; replaceable for tests. Returns false when no opener worked.
        /// </summary>
        public Func<string, bool> UrlOpener { get; set; }

        /// <summary>
        /// Last URL passed to <see cref="OpenUrl"/> (diagnostics and tests).
        /// </summary>
        public string? LastUrl { get; private set; } = null;

        #endregion

        #region Private-Members

        private readonly TuiApplication? _App;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="app">Application for suspend, or null (tests).</param>
        public ExternalService(TuiApplication? app)
        {
            _App = app;
            UrlOpener = DefaultOpen;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Open an http(s) URL in the default browser.
        /// </summary>
        /// <param name="url">URL.</param>
        /// <returns>True when launched.</returns>
        public bool OpenUrl(string url)
        {
            if (String.IsNullOrWhiteSpace(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return false;
            LastUrl = url;
            return UrlOpener(url);
        }

        /// <summary>
        /// The editor command: <c>$VISUAL</c>, then <c>$EDITOR</c>, then <c>notepad</c> on Windows or <c>vi</c>.
        /// </summary>
        /// <returns>Editor command.</returns>
        public static string EditorCommand()
        {
            string? visual = Environment.GetEnvironmentVariable("VISUAL");
            if (!String.IsNullOrWhiteSpace(visual)) return visual!;
            string? editor = Environment.GetEnvironmentVariable("EDITOR");
            if (!String.IsNullOrWhiteSpace(editor)) return editor!;
            return OperatingSystem.IsWindows() ? "notepad" : "vi";
        }

        /// <summary>
        /// Edit text in the external editor and return the result (the original when the editor fails).
        /// </summary>
        /// <param name="initial">Initial text.</param>
        /// <param name="extension">Temporary file extension, for example .md.</param>
        /// <returns>Edited text.</returns>
        public async Task<string> EditTextAsync(string initial, string extension = ".md")
        {
            string path = Path.Combine(Path.GetTempPath(), "armada-tui-" + Guid.NewGuid().ToString("N") + (extension ?? ".txt"));
            await File.WriteAllTextAsync(path, initial ?? "", Encoding.UTF8).ConfigureAwait(false);
            try
            {
                Func<Task> run = async () =>
                {
                    string[] parts = EditorCommand().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                    ProcessStartInfo psi = new ProcessStartInfo(parts[0]);
                    if (parts.Length > 1) foreach (string arg in parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)) psi.ArgumentList.Add(arg);
                    psi.ArgumentList.Add(path);
                    psi.UseShellExecute = false;
                    using (Process? process = Process.Start(psi))
                    {
                        if (process != null) await process.WaitForExitAsync().ConfigureAwait(false);
                    }
                };

                if (_App != null) await _App.SuspendAsync(run).ConfigureAwait(false);
                else await run().ConfigureAwait(false);
                return await File.ReadAllTextAsync(path, Encoding.UTF8).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is Win32Exception || ex is IOException || ex is InvalidOperationException)
            {
                return initial ?? "";
            }
            finally
            {
                try { File.Delete(path); } catch (Exception) { }
            }
        }

        /// <summary>
        /// Save bytes to a file, creating directories.
        /// </summary>
        /// <param name="path">Path (~ expands to the home directory).</param>
        /// <param name="content">Content.</param>
        /// <returns>The full path written.</returns>
        /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
        public string SaveFile(string path, byte[] content)
        {
            string full = ExpandPath(path);
            string? dir = Path.GetDirectoryName(full);
            if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir!);
            File.WriteAllBytes(full, content ?? Array.Empty<byte>());
            return full;
        }

        /// <summary>
        /// Save text (UTF-8) to a file.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <param name="text">Text.</param>
        /// <returns>The full path written.</returns>
        public string SaveText(string path, string text)
        {
            return SaveFile(path, Encoding.UTF8.GetBytes(text ?? ""));
        }

        /// <summary>
        /// Load a file.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>Bytes.</returns>
        /// <exception cref="IOException">Thrown when the file cannot be read.</exception>
        public byte[] LoadFile(string path)
        {
            return File.ReadAllBytes(ExpandPath(path));
        }

        /// <summary>
        /// Expand a leading <c>~</c> and make the path absolute.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>Full path.</returns>
        public static string ExpandPath(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            string p = path.Trim();
            if (p == "~" || p.StartsWith("~/") || p.StartsWith("~\\"))
                p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p.Length > 2 ? p.Substring(2) : "");
            return Path.GetFullPath(p);
        }

        #endregion

        #region Private-Methods

        private static bool DefaultOpen(string url)
        {
            try
            {
                ProcessStartInfo psi;
                if (OperatingSystem.IsWindows()) psi = new ProcessStartInfo(url) { UseShellExecute = true };
                else if (OperatingSystem.IsMacOS()) psi = new ProcessStartInfo("open", url);
                else psi = new ProcessStartInfo("xdg-open", url);
                psi.UseShellExecute = OperatingSystem.IsWindows();
                psi.RedirectStandardError = !OperatingSystem.IsWindows();
                psi.RedirectStandardOutput = !OperatingSystem.IsWindows();
                using (Process? p = Process.Start(psi)) { }
                return true;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
            {
                return false;
            }
        }

        #endregion
    }
}
