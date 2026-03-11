namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Services;
    using Armada.Test.Common;

    public class GitInferenceTests : TestSuite
    {
        public override string Name => "Git Inference";

        protected override async Task RunTestsAsync()
        {
            // InferVesselName

            await RunTest("InferVesselName HttpsUrl ExtractsRepoName", () =>
            {
                string name = GitInference.InferVesselName("https://github.com/user/myapp.git");
                AssertEqual("myapp", name);
            });

            await RunTest("InferVesselName HttpsUrlNoGit ExtractsRepoName", () =>
            {
                string name = GitInference.InferVesselName("https://github.com/user/myapp");
                AssertEqual("myapp", name);
            });

            await RunTest("InferVesselName SshUrl ExtractsRepoName", () =>
            {
                string name = GitInference.InferVesselName("git@github.com:user/myapp.git");
                AssertEqual("myapp", name);
            });

            await RunTest("InferVesselName SshUrlNoGit ExtractsRepoName", () =>
            {
                string name = GitInference.InferVesselName("git@github.com:user/myapp");
                AssertEqual("myapp", name);
            });

            await RunTest("InferVesselName TrailingSlash ExtractsRepoName", () =>
            {
                string name = GitInference.InferVesselName("https://github.com/user/myapp/");
                AssertEqual("myapp", name);
            });

            await RunTest("InferVesselName NestedPath ExtractsLastSegment", () =>
            {
                string name = GitInference.InferVesselName("https://gitlab.com/org/group/myapp.git");
                AssertEqual("myapp", name);
            });

            await RunTest("InferVesselName EmptyString ReturnsUnnamed", () =>
            {
                string name = GitInference.InferVesselName("");
                AssertEqual("unnamed", name);
            });

            // IsGitRepository

            await RunTest("IsGitRepository CurrentDirectory DoesNotThrow", () =>
            {
                bool result = GitInference.IsGitRepository(Directory.GetCurrentDirectory());
                AssertTrue(result || !result);
            });

            await RunTest("IsGitRepository TempDirectory ReturnsFalse", () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "armada_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    bool result = GitInference.IsGitRepository(tempDir);
                    AssertFalse(result);
                }
                finally
                {
                    Directory.Delete(tempDir);
                }
            });

            // GetDefaultBranch

            await RunTest("GetDefaultBranch NonGitDir ReturnsMain", () =>
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "armada_test_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    string branch = GitInference.GetDefaultBranch(tempDir);
                    AssertEqual("main", branch);
                }
                finally
                {
                    Directory.Delete(tempDir);
                }
            });
        }
    }
}
