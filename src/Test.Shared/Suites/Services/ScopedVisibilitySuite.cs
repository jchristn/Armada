namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for <see cref="ScopedVisibility"/>: the view/edit/create rules for tenant-wide vs
    /// user-specific configuration entities.
    /// </summary>
    public sealed class ScopedVisibilitySuite : IArmadaTestSuite
    {
        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the ScopedVisibility suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            AuthContext user = AuthContext.Authenticated("ten_a", "usr_a", false, false, "Test");
            AuthContext other = AuthContext.Authenticated("ten_a", "usr_b", false, false, "Test");
            AuthContext tenantAdmin = AuthContext.Authenticated("ten_a", "usr_admin", false, true, "Test");
            AuthContext globalAdmin = AuthContext.Authenticated("ten_a", "usr_root", true, false, "Test");

            cases.Add(Case("user_views_tenantwide_and_own", "Regular user views tenant-wide and their own, not others'", TestTags.Positive, () =>
            {
                AssertTrue(ScopedVisibility.CanView(user, ScopeEnum.TenantWide, "ten_a", null), "should view tenant-wide");
                AssertTrue(ScopedVisibility.CanView(user, ScopeEnum.UserSpecific, "ten_a", "usr_a"), "should view own");
                AssertTrue(!ScopedVisibility.CanView(user, ScopeEnum.UserSpecific, "ten_a", "usr_b"), "should not view others'");
                AssertTrue(!ScopedVisibility.CanView(user, ScopeEnum.TenantWide, "ten_other", null), "should not view other tenant");
            }));

            cases.Add(Case("user_edits_only_own_userspecific", "Regular user edits only their own user-specific object", TestTags.Positive, () =>
            {
                AssertTrue(ScopedVisibility.CanEdit(user, ScopeEnum.UserSpecific, "ten_a", "usr_a"), "own user-specific editable");
                AssertTrue(!ScopedVisibility.CanEdit(user, ScopeEnum.TenantWide, "ten_a", null), "tenant-wide not editable by user");
                AssertTrue(!ScopedVisibility.CanEdit(other, ScopeEnum.UserSpecific, "ten_a", "usr_a"), "others' not editable");
            }));

            cases.Add(Case("tenant_admin_edits_all_in_tenant", "Tenant admin edits tenant-wide and user-specific in the tenant", TestTags.Positive, () =>
            {
                AssertTrue(ScopedVisibility.CanEdit(tenantAdmin, ScopeEnum.TenantWide, "ten_a", null), "tenant-wide editable by tenant admin");
                AssertTrue(ScopedVisibility.CanEdit(tenantAdmin, ScopeEnum.UserSpecific, "ten_a", "usr_a"), "user object editable by tenant admin");
                AssertTrue(!ScopedVisibility.CanEdit(tenantAdmin, ScopeEnum.TenantWide, "ten_other", null), "not across tenants");
            }));

            cases.Add(Case("global_admin_edits_anything", "Global admin can view/edit anything", TestTags.Positive, () =>
            {
                AssertTrue(ScopedVisibility.CanEdit(globalAdmin, ScopeEnum.UserSpecific, "ten_other", "usr_z"), "global admin edits across tenants");
                AssertTrue(ScopedVisibility.CanView(globalAdmin, ScopeEnum.UserSpecific, "ten_other", "usr_z"), "global admin views across tenants");
            }));

            cases.Add(Case("create_scope_by_role", "Create scope: regular user forced to UserSpecific; admin may choose", TestTags.Positive, () =>
            {
                AssertEqual(ScopeEnum.UserSpecific, ScopedVisibility.ResolveCreateScope(user, ScopeEnum.TenantWide));
                AssertEqual(ScopeEnum.UserSpecific, ScopedVisibility.ResolveCreateScope(user, null));
                AssertEqual(ScopeEnum.TenantWide, ScopedVisibility.ResolveCreateScope(tenantAdmin, null));
                AssertEqual(ScopeEnum.UserSpecific, ScopedVisibility.ResolveCreateScope(tenantAdmin, ScopeEnum.UserSpecific));
            }));

            return new TestSuiteDescriptor(
                suiteId: "Services.ScopedVisibility",
                displayName: "Scoped Visibility",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: "Services.ScopedVisibility",
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) => { body(); return Task.CompletedTask; },
                tags: new List<string> { tag });
        }

        #endregion
    }
}
