namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Services;
    using Armada.Test.Common;
    using SyslogLogging;

    public class GitServiceTests : TestSuite
    {
        public override string Name => "Git Service";

        private GitService CreateService()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            return new GitService(logging);
        }

        protected override async Task RunTestsAsync()
        {
            await RunTest("Constructor NullLogging Throws", () =>
            {
                AssertThrows<ArgumentNullException>(() => new GitService(null!));
            });

            await RunTest("CloneBareAsync NullRepoUrl Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CloneBareAsync(null!, "/tmp/path"));
            });

            await RunTest("CloneBareAsync EmptyRepoUrl Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CloneBareAsync("", "/tmp/path"));
            });

            await RunTest("CloneBareAsync NullLocalPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CloneBareAsync("https://github.com/test/repo", null!));
            });

            await RunTest("CreateWorktreeAsync NullRepoPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreateWorktreeAsync(null!, "/tmp/wt", "branch"));
            });

            await RunTest("CreateWorktreeAsync NullWorktreePath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreateWorktreeAsync("/tmp/repo", null!, "branch"));
            });

            await RunTest("CreateWorktreeAsync NullBranchName Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreateWorktreeAsync("/tmp/repo", "/tmp/wt", null!));
            });

            await RunTest("RemoveWorktreeAsync NullPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.RemoveWorktreeAsync(null!));
            });

            await RunTest("FetchAsync NullRepoPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.FetchAsync(null!));
            });

            await RunTest("PushBranchAsync NullPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.PushBranchAsync(null!));
            });

            await RunTest("CreatePullRequestAsync NullPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreatePullRequestAsync(null!, "title", "body"));
            });

            await RunTest("CreatePullRequestAsync NullTitle Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.CreatePullRequestAsync("/tmp/wt", null!, "body"));
            });

            await RunTest("RepairWorktreeAsync NullPath Throws", async () =>
            {
                GitService service = CreateService();
                await AssertThrowsAsync<ArgumentNullException>(() => service.RepairWorktreeAsync(null!));
            });

            await RunTest("IsRepositoryAsync NullPath ReturnsFalse", async () =>
            {
                GitService service = CreateService();
                bool result = await service.IsRepositoryAsync(null!);
                AssertFalse(result);
            });

            await RunTest("IsRepositoryAsync EmptyPath ReturnsFalse", async () =>
            {
                GitService service = CreateService();
                bool result = await service.IsRepositoryAsync("");
                AssertFalse(result);
            });

            await RunTest("IsRepositoryAsync NonExistentPath ReturnsFalse", async () =>
            {
                GitService service = CreateService();
                bool result = await service.IsRepositoryAsync("/tmp/nonexistent_" + Guid.NewGuid().ToString("N"));
                AssertFalse(result);
            });
        }
    }
}
