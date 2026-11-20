namespace Test.Shared.Infrastructure
{
    /// <summary>
    /// Paths for a worktree branch-sync test: an origin bare repository, the vessel's bare clone of it with the branch at
    /// <see cref="StartingCommit"/>, a source working copy, and where the worktree is created.
    /// </summary>
    internal sealed class WorktreeBranchFixture
    {
        /// <summary>Root directory that holds every repository of the fixture.</summary>
        public string RootDir { get; }

        /// <summary>Working copy used to author commits.</summary>
        public string SourceDir { get; }

        /// <summary>The vessel's bare repository.</summary>
        public string BareDir { get; }

        /// <summary>Path where the test creates the worktree.</summary>
        public string WorktreeDir { get; }

        /// <summary>Branch under test.</summary>
        public string BranchName { get; }

        /// <summary>Commit the branch starts at, locally and on origin.</summary>
        public string StartingCommit { get; }

        /// <summary>Instantiate.</summary>
        /// <param name="rootDir">Root directory.</param>
        /// <param name="sourceDir">Source working copy.</param>
        /// <param name="bareDir">Vessel bare repository.</param>
        /// <param name="worktreeDir">Worktree path.</param>
        /// <param name="branchName">Branch name.</param>
        /// <param name="startingCommit">Starting commit.</param>
        public WorktreeBranchFixture(string rootDir, string sourceDir, string bareDir, string worktreeDir, string branchName, string startingCommit)
        {
            RootDir = rootDir;
            SourceDir = sourceDir;
            BareDir = bareDir;
            WorktreeDir = worktreeDir;
            BranchName = branchName;
            StartingCommit = startingCommit;
        }
    }
}
