namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Authorization;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors pinning the permission levels of the vessel health routes in <see cref="AuthorizationConfig"/>:
    /// enumerate (POST) and summary are Authenticated, evaluate is TenantAdmin, the per-vessel GET is Authenticated, and
    /// override PUT and DELETE are TenantAdmin.
    /// </summary>
    public sealed class VesselHealthAuthorizationSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();
            cases.Add(new TestCaseDescriptor(
                suiteId: "Services.VesselHealthAuthorization",
                caseId: "permission_levels",
                displayName: "Vessel health routes map to the planned permission levels",
                executeAsync: (CancellationToken ct) =>
                {
                    AssertEqual(PermissionLevel.Authenticated, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/vessel-health/enumerate"));
                    AssertEqual(PermissionLevel.Authenticated, AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/vessel-health/summary"));
                    AssertEqual(PermissionLevel.TenantAdmin, AuthorizationConfig.GetPermissionLevel("POST", "/api/v1/vessel-health/evaluate"));
                    AssertEqual(PermissionLevel.Authenticated, AuthorizationConfig.GetPermissionLevel("GET", "/api/v1/vessels/vsl_x/health"));
                    AssertEqual(PermissionLevel.TenantAdmin, AuthorizationConfig.GetPermissionLevel("PUT", "/api/v1/vessels/vsl_x/health/overrides/Dependencies"));
                    AssertEqual(PermissionLevel.TenantAdmin, AuthorizationConfig.GetPermissionLevel("DELETE", "/api/v1/vessels/vsl_x/health/overrides/Overall"));
                    return Task.CompletedTask;
                },
                tags: new List<string> { TestTags.Positive }));

            return new TestSuiteDescriptor(
                suiteId: "Services.VesselHealthAuthorization",
                displayName: "Vessel Health Authorization",
                cases: cases);
        }

        #endregion
    }
}
