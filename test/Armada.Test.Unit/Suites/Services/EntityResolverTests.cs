namespace Armada.Test.Unit.Suites.Services
{
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Armada.Core.Services;
    using Armada.Test.Common;

    public class EntityResolverTests : TestSuite
    {
        public override string Name => "Entity Resolver";

        protected override async Task RunTestsAsync()
        {
            // Vessel Resolution

            await RunTest("ResolveVessel ById ReturnsMatch", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("myapp", "https://github.com/user/myapp") { Id = "vsl_abc123" },
                    new Vessel("other", "https://github.com/user/other") { Id = "vsl_def456" }
                };

                Vessel? result = EntityResolver.ResolveVessel(vessels, "vsl_abc123");
                AssertNotNull(result);
                AssertEqual("myapp", result!.Name);
            });

            await RunTest("ResolveVessel ByName ReturnsMatch", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("myapp", "https://github.com/user/myapp"),
                    new Vessel("other", "https://github.com/user/other")
                };

                Vessel? result = EntityResolver.ResolveVessel(vessels, "myapp");
                AssertNotNull(result);
                AssertEqual("myapp", result!.Name);
            });

            await RunTest("ResolveVessel ByNameCaseInsensitive ReturnsMatch", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("MyApp", "https://github.com/user/myapp")
                };

                Vessel? result = EntityResolver.ResolveVessel(vessels, "myapp");
                AssertNotNull(result);
                AssertEqual("MyApp", result!.Name);
            });

            await RunTest("ResolveVessel BySubstring ReturnsSingleMatch", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("my-awesome-app", "https://github.com/user/myapp"),
                    new Vessel("other-service", "https://github.com/user/other")
                };

                Vessel? result = EntityResolver.ResolveVessel(vessels, "awesome");
                AssertNotNull(result);
                AssertEqual("my-awesome-app", result!.Name);
            });

            await RunTest("ResolveVessel AmbiguousSubstring ReturnsNull", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("app-one", "https://github.com/user/app-one"),
                    new Vessel("app-two", "https://github.com/user/app-two")
                };

                Vessel? result = EntityResolver.ResolveVessel(vessels, "app");
                AssertNull(result);
            });

            await RunTest("ResolveVessel EmptyList ReturnsNull", () =>
            {
                Vessel? result = EntityResolver.ResolveVessel(new List<Vessel>(), "anything");
                AssertNull(result);
            });

            await RunTest("ResolveVessel NullList ReturnsNull", () =>
            {
                Vessel? result = EntityResolver.ResolveVessel(null!, "anything");
                AssertNull(result);
            });

            await RunTest("ResolveVessel NoMatch ReturnsNull", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("myapp", "https://github.com/user/myapp")
                };

                Vessel? result = EntityResolver.ResolveVessel(vessels, "nonexistent");
                AssertNull(result);
            });

            // Captain Resolution

            await RunTest("ResolveCaptain ById ReturnsMatch", () =>
            {
                List<Captain> captains = new List<Captain>
                {
                    new Captain("claude-1") { Id = "cpt_abc123" },
                    new Captain("claude-2") { Id = "cpt_def456" }
                };

                Captain? result = EntityResolver.ResolveCaptain(captains, "cpt_abc123");
                AssertNotNull(result);
                AssertEqual("claude-1", result!.Name);
            });

            await RunTest("ResolveCaptain ByName ReturnsMatch", () =>
            {
                List<Captain> captains = new List<Captain>
                {
                    new Captain("claude-1"),
                    new Captain("codex-1")
                };

                Captain? result = EntityResolver.ResolveCaptain(captains, "claude-1");
                AssertNotNull(result);
                AssertEqual("claude-1", result!.Name);
            });

            await RunTest("ResolveCaptain BySubstring ReturnsSingleMatch", () =>
            {
                List<Captain> captains = new List<Captain>
                {
                    new Captain("claude-alpha"),
                    new Captain("codex-beta")
                };

                Captain? result = EntityResolver.ResolveCaptain(captains, "alpha");
                AssertNotNull(result);
                AssertEqual("claude-alpha", result!.Name);
            });

            await RunTest("ResolveCaptain NullList ReturnsNull", () =>
            {
                Captain? result = EntityResolver.ResolveCaptain(null!, "anything");
                AssertNull(result);
            });

            // Mission Resolution

            await RunTest("ResolveMission ById ReturnsMatch", () =>
            {
                List<Mission> missions = new List<Mission>
                {
                    new Mission("Fix login bug") { Id = "msn_abc123" },
                    new Mission("Add tests") { Id = "msn_def456" }
                };

                Mission? result = EntityResolver.ResolveMission(missions, "msn_abc123");
                AssertNotNull(result);
                AssertEqual("Fix login bug", result!.Title);
            });

            await RunTest("ResolveMission ByTitleSubstring ReturnsSingleMatch", () =>
            {
                List<Mission> missions = new List<Mission>
                {
                    new Mission("Fix login bug"),
                    new Mission("Add user registration")
                };

                Mission? result = EntityResolver.ResolveMission(missions, "login");
                AssertNotNull(result);
                AssertEqual("Fix login bug", result!.Title);
            });

            await RunTest("ResolveMission AmbiguousTitle ReturnsNull", () =>
            {
                List<Mission> missions = new List<Mission>
                {
                    new Mission("Fix bug in login"),
                    new Mission("Fix bug in signup")
                };

                Mission? result = EntityResolver.ResolveMission(missions, "Fix bug");
                AssertNull(result);
            });

            // Voyage Resolution

            await RunTest("ResolveVoyage ById ReturnsMatch", () =>
            {
                List<Voyage> voyages = new List<Voyage>
                {
                    new Voyage("API Hardening") { Id = "vyg_abc123" },
                    new Voyage("UI Refresh") { Id = "vyg_def456" }
                };

                Voyage? result = EntityResolver.ResolveVoyage(voyages, "vyg_abc123");
                AssertNotNull(result);
                AssertEqual("API Hardening", result!.Title);
            });

            await RunTest("ResolveVoyage ByTitleSubstring ReturnsSingleMatch", () =>
            {
                List<Voyage> voyages = new List<Voyage>
                {
                    new Voyage("API Hardening"),
                    new Voyage("UI Refresh")
                };

                Voyage? result = EntityResolver.ResolveVoyage(voyages, "Hardening");
                AssertNotNull(result);
                AssertEqual("API Hardening", result!.Title);
            });

            // Fleet Resolution

            await RunTest("ResolveFleet ByName ReturnsMatch", () =>
            {
                List<Fleet> fleets = new List<Fleet>
                {
                    new Fleet("production"),
                    new Fleet("staging")
                };

                Fleet? result = EntityResolver.ResolveFleet(fleets, "production");
                AssertNotNull(result);
                AssertEqual("production", result!.Name);
            });

            await RunTest("ResolveFleet BySubstring ReturnsSingleMatch", () =>
            {
                List<Fleet> fleets = new List<Fleet>
                {
                    new Fleet("my-production-fleet"),
                    new Fleet("staging-fleet")
                };

                Fleet? result = EntityResolver.ResolveFleet(fleets, "production");
                AssertNotNull(result);
                AssertEqual("my-production-fleet", result!.Name);
            });

            // Remote URL Resolution

            await RunTest("ResolveVesselByRemoteUrl ExactMatch ReturnsVessel", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("myapp", "https://github.com/user/myapp.git"),
                    new Vessel("other", "https://github.com/user/other.git")
                };

                Vessel? result = EntityResolver.ResolveVesselByRemoteUrl(vessels, "https://github.com/user/myapp.git");
                AssertNotNull(result);
                AssertEqual("myapp", result!.Name);
            });

            await RunTest("ResolveVesselByRemoteUrl WithoutGitSuffix Matches", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("myapp", "https://github.com/user/myapp.git")
                };

                Vessel? result = EntityResolver.ResolveVesselByRemoteUrl(vessels, "https://github.com/user/myapp");
                AssertNotNull(result);
                AssertEqual("myapp", result!.Name);
            });

            await RunTest("ResolveVesselByRemoteUrl SshToHttps Matches", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("myapp", "https://github.com/user/myapp.git")
                };

                Vessel? result = EntityResolver.ResolveVesselByRemoteUrl(vessels, "git@github.com:user/myapp.git");
                AssertNotNull(result);
                AssertEqual("myapp", result!.Name);
            });

            await RunTest("ResolveVesselByRemoteUrl HttpsToSsh Matches", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("myapp", "git@github.com:user/myapp.git")
                };

                Vessel? result = EntityResolver.ResolveVesselByRemoteUrl(vessels, "https://github.com/user/myapp");
                AssertNotNull(result);
                AssertEqual("myapp", result!.Name);
            });

            await RunTest("ResolveVesselByRemoteUrl NoMatch ReturnsNull", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("myapp", "https://github.com/user/myapp.git")
                };

                Vessel? result = EntityResolver.ResolveVesselByRemoteUrl(vessels, "https://github.com/user/other.git");
                AssertNull(result);
            });

            await RunTest("ResolveVesselByRemoteUrl EmptyUrl ReturnsNull", () =>
            {
                List<Vessel> vessels = new List<Vessel>
                {
                    new Vessel("myapp", "https://github.com/user/myapp.git")
                };

                Vessel? result = EntityResolver.ResolveVesselByRemoteUrl(vessels, "");
                AssertNull(result);
            });

            await RunTest("ResolveVesselByRemoteUrl NullList ReturnsNull", () =>
            {
                Vessel? result = EntityResolver.ResolveVesselByRemoteUrl(null!, "https://github.com/user/myapp");
                AssertNull(result);
            });
        }
    }
}
