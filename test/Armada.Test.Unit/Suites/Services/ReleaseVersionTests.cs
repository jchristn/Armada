namespace Armada.Test.Unit.Suites.Services
{
    using System.IO;
    using System.Text.RegularExpressions;
    using Armada.Core;
    using Armada.Test.Common;

    public class ReleaseVersionTests : TestSuite
    {
        public override string Name => "Release Version";

        protected override async Task RunTestsAsync()
        {
            await RunTest("ProductVersion And Shared Build Props Match V050", () =>
            {
                string propsContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Directory.Build.props"));
                Match versionMatch = Regex.Match(propsContents, @"<Version>\s*([^<]+)\s*</Version>");

                AssertTrue(versionMatch.Success, "Directory.Build.props should contain a Version element");
                AssertEqual("0.5.0", Constants.ProductVersion);
                AssertEqual(Constants.ProductVersion, versionMatch.Groups[1].Value.Trim());
            });

            await RunTest("Helm Program Uses ProductVersion Constant", () =>
            {
                string programContents = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "Armada.Helm", "Program.cs"));

                AssertContains("\"v\" + Constants.ProductVersion", programContents, "Helm banner/help version should come from Constants.ProductVersion");
                AssertContains("config.SetApplicationVersion(Constants.ProductVersion);", programContents, "Helm CLI version should come from Constants.ProductVersion");
                AssertFalse(programContents.Contains("0.3.0"), "Helm entry point should not contain the stale 0.3.0 literal");
                AssertFalse(programContents.Contains("SetApplicationVersion(\"0.5.0\")"), "Helm CLI version should not be hard-coded");
            });
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                    Directory.Exists(Path.Combine(current.FullName, "test")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
        }
    }
}
