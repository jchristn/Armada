namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Armada.Core.Harbor;
    using Armada.Core.Services;

    /// <summary>
    /// Real git repositories for Harbor-hosted dock tests, all in one temporary folder: a bare origin with a main branch,
    /// the user's checkout of it (under a code root folder, as on a developer's machine), and separate folders for the
    /// Harbor's docks and clones and for the Admiral's own data.
    /// </summary>
    public sealed class HarborDockGitFixture : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Folder holding everything.
        /// </summary>
        public string Root { get; }

        /// <summary>
        /// The bare origin repository (its path is the vessel's repository URL).
        /// </summary>
        public string Origin { get; }

        /// <summary>
        /// The folder the user keeps code in (a Harbor root folder).
        /// </summary>
        public string CodeRoot { get; }

        /// <summary>
        /// The user's checkout of the origin, on branch main.
        /// </summary>
        public string Checkout { get; }

        /// <summary>
        /// The Harbor's docks folder.
        /// </summary>
        public string HarborDocks { get; }

        /// <summary>
        /// The Harbor's clones folder.
        /// </summary>
        public string HarborRepos { get; }

        /// <summary>
        /// The Admiral's data folder (docks, repos, and logs go under it); never shared with the Harbor.
        /// </summary>
        public string AdmiralData { get; }

        /// <summary>
        /// The Harbor's dock settings: its docks and clones folders, with no mappings or root folders until a test adds
        /// them.
        /// </summary>
        public HarborDockSettings Settings { get; }

        #endregion

        #region Private-Members

        private static readonly LocalHostCommandExecutor _Commands = new LocalHostCommandExecutor();

        #endregion

        #region Constructors-and-Factories

        private HarborDockGitFixture(string root)
        {
            Root = root;
            Origin = Path.Combine(root, "remote", "app.git");
            CodeRoot = Path.Combine(root, "code");
            Checkout = Path.Combine(CodeRoot, "app");
            HarborDocks = Path.Combine(root, "harbor", "docks");
            HarborRepos = Path.Combine(root, "harbor", "repos");
            AdmiralData = Path.Combine(root, "admiral");
            Settings = new HarborDockSettings { DocksDirectory = HarborDocks, ReposDirectory = HarborRepos };
        }

        /// <summary>
        /// Create the origin (one commit on main) and the user's checkout.
        /// </summary>
        /// <returns>The fixture.</returns>
        public static async Task<HarborDockGitFixture> CreateAsync()
        {
            HarborDockGitFixture fixture = new HarborDockGitFixture(TestTemp.NewDirectory("harbor_docks"));
            Directory.CreateDirectory(Path.GetDirectoryName(fixture.Origin)!);
            Directory.CreateDirectory(fixture.CodeRoot);
            Directory.CreateDirectory(fixture.AdmiralData);

            string seed = Path.Combine(fixture.Root, "seed");
            Directory.CreateDirectory(seed);
            await GitAsync(seed, "init", "--initial-branch=main").ConfigureAwait(false);
            await ConfigureIdentityAsync(seed).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(seed, "README.md"), "# app\n").ConfigureAwait(false);
            await GitAsync(seed, "add", "README.md").ConfigureAwait(false);
            await GitAsync(seed, "commit", "-m", "Initial commit").ConfigureAwait(false);
            await GitAsync(fixture.Root, "clone", "--bare", seed, fixture.Origin).ConfigureAwait(false);

            await GitAsync(fixture.CodeRoot, "clone", fixture.Origin, fixture.Checkout).ConfigureAwait(false);
            await ConfigureIdentityAsync(fixture.Checkout).ConfigureAwait(false);
            return fixture;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run git and require success.
        /// </summary>
        /// <param name="workingDirectory">Directory to run in.</param>
        /// <param name="arguments">Arguments.</param>
        /// <returns>Standard output, trimmed.</returns>
        public static async Task<string> GitAsync(string workingDirectory, params string[] arguments)
        {
            HostCommandResult result = await TryGitAsync(workingDirectory, arguments).ConfigureAwait(false);
            if (!result.Success)
                throw new InvalidOperationException("git " + String.Join(" ", arguments) + " failed in " + workingDirectory + " (exit " + result.ExitCode + "): " + result.StandardError.Trim());
            return result.StandardOutput.Trim();
        }

        /// <summary>
        /// Run git and return its result whatever the exit code.
        /// </summary>
        /// <param name="workingDirectory">Directory to run in.</param>
        /// <param name="arguments">Arguments.</param>
        /// <returns>The result.</returns>
        public static Task<HostCommandResult> TryGitAsync(string workingDirectory, params string[] arguments)
        {
            return _Commands.RunAsync(new HostCommandRequest
            {
                Executable = "git",
                WorkingDirectory = workingDirectory,
                Arguments = new List<string>(arguments),
                TimeoutMs = 60000
            });
        }

        /// <summary>
        /// Commit one new file in a working tree.
        /// </summary>
        /// <param name="workingTree">Working tree.</param>
        /// <param name="fileName">File name.</param>
        /// <param name="content">Content.</param>
        /// <param name="message">Commit message.</param>
        /// <returns>The new commit.</returns>
        public static async Task<string> CommitFileAsync(string workingTree, string fileName, string content, string message)
        {
            await File.WriteAllTextAsync(Path.Combine(workingTree, fileName), content).ConfigureAwait(false);
            await GitAsync(workingTree, "add", "--", fileName).ConfigureAwait(false);
            await GitAsync(workingTree, "-c", "user.name=Armada Test", "-c", "user.email=armada@example.com", "commit", "-m", message).ConfigureAwait(false);
            return await GitAsync(workingTree, "rev-parse", "HEAD").ConfigureAwait(false);
        }

        /// <summary>
        /// The commit a ref points at, or null when it does not exist.
        /// </summary>
        /// <param name="repository">Repository.</param>
        /// <param name="gitRef">Ref.</param>
        /// <returns>The commit, or null.</returns>
        public static async Task<string?> ResolveAsync(string repository, string gitRef)
        {
            HostCommandResult result = await TryGitAsync(repository, "rev-parse", "--verify", "--quiet", gitRef + "^{commit}").ConfigureAwait(false);
            return result.Success ? result.StandardOutput.Trim() : null;
        }

        /// <summary>
        /// Whether a ref's history contains a file.
        /// </summary>
        /// <param name="repository">Repository.</param>
        /// <param name="gitRef">Ref.</param>
        /// <param name="path">File path in the tree.</param>
        /// <returns>True when the file exists at that ref.</returns>
        public static async Task<bool> TreeHasFileAsync(string repository, string gitRef, string path)
        {
            HostCommandResult result = await TryGitAsync(repository, "cat-file", "-e", gitRef + ":" + path).ConfigureAwait(false);
            return result.Success;
        }

        /// <summary>
        /// The worktree paths registered in a repository (git worktree list --porcelain -z), full paths.
        /// </summary>
        /// <param name="repository">Repository.</param>
        /// <returns>The paths.</returns>
        public static async Task<List<string>> WorktreesAsync(string repository)
        {
            string output = (await TryGitAsync(repository, "worktree", "list", "--porcelain", "-z").ConfigureAwait(false)).StandardOutput;
            List<string> paths = new List<string>();
            foreach (string field in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                const string prefix = "worktree ";
                if (field.StartsWith(prefix, StringComparison.Ordinal))
                    paths.Add(Armada.Core.PathCanonicalizer.Canonicalize(field.Substring(prefix.Length)));
            }

            return paths;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            TestTemp.TryDelete(Root);
        }

        #endregion

        #region Private-Methods

        private static async Task ConfigureIdentityAsync(string repository)
        {
            await GitAsync(repository, "config", "user.name", "Armada Test").ConfigureAwait(false);
            await GitAsync(repository, "config", "user.email", "armada@example.com").ConfigureAwait(false);
        }

        #endregion
    }
}
