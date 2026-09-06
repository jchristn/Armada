namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="EnumerationScope"/>: the three-tier (global admin / tenant admin / regular
    /// user) resolution of enumeration scope, including the privileged owner-filter narrowing.
    /// </summary>
    public sealed class EnumerationScopeSuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the EnumerationScope suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case("regular_user_locked_to_self", "Regular user is locked to their own records and ignores query filters", TestTags.Positive, () =>
            {
                AuthContext user = AuthContext.Authenticated("ten_a", "usr_a", false, false, "Test");
                EnumerationScope scope = EnumerationScope.Resolve(user, "ten_other", "usr_other");
                AssertTrue(!scope.All, "Regular user is never global.");
                AssertEqual("ten_a", scope.TenantId ?? string.Empty);
                AssertEqual("usr_a", scope.UserId ?? string.Empty);
            }));

            cases.Add(Case("tenant_admin_all_users_by_default", "Tenant admin sees the whole tenant by default", TestTags.Positive, () =>
            {
                AuthContext admin = AuthContext.Authenticated("ten_a", "usr_admin", false, true, "Test");
                EnumerationScope scope = EnumerationScope.Resolve(admin, null, null);
                AssertTrue(!scope.All, "Tenant admin is not global.");
                AssertEqual("ten_a", scope.TenantId ?? string.Empty);
                AssertTrue(scope.UserId == null, "Tenant admin with no filter spans all users in the tenant.");
            }));

            cases.Add(Case("tenant_admin_can_narrow_to_a_user", "Tenant admin can narrow to a specific user via the query filter", TestTags.Positive, () =>
            {
                AuthContext admin = AuthContext.Authenticated("ten_a", "usr_admin", false, true, "Test");
                EnumerationScope scope = EnumerationScope.Resolve(admin, "ten_ignored", "usr_target");
                AssertEqual("ten_a", scope.TenantId ?? string.Empty);
                AssertEqual("usr_target", scope.UserId ?? string.Empty);
            }));

            cases.Add(Case("global_admin_all_by_default", "Global admin spans everything by default", TestTags.Positive, () =>
            {
                AuthContext admin = AuthContext.Authenticated("ten_a", "usr_root", true, false, "Test");
                EnumerationScope scope = EnumerationScope.Resolve(admin, null, null);
                AssertTrue(scope.All, "Global admin with no filter enumerates globally.");
            }));

            cases.Add(Case("global_admin_can_narrow_tenant_and_user", "Global admin can narrow by tenant and user", TestTags.Positive, () =>
            {
                AuthContext admin = AuthContext.Authenticated("ten_a", "usr_root", true, false, "Test");
                EnumerationScope scope = EnumerationScope.Resolve(admin, "ten_x", "usr_y");
                AssertTrue(!scope.All, "With filters, global admin is not All.");
                AssertEqual("ten_x", scope.TenantId ?? string.Empty);
                AssertEqual("usr_y", scope.UserId ?? string.Empty);
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.EnumerationScope",
                displayName: "Enumeration Scope",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.EnumerationScope",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
