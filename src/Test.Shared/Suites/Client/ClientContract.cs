namespace Test.Shared.Suites.Client
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Client;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;

    /// <summary>
    /// Shared builders for the Client.Contract.* suites: a case that runs against the suite's own live server
    /// (<see cref="E2EServerFixture"/>) with an admin <see cref="ArmadaClient"/>, and assertions for error replies.
    /// </summary>
    internal static class ClientContract
    {
        #region Public-Methods

        /// <summary>
        /// A live case. The suite instance keys the fixture, so every case of a suite shares one server.
        /// </summary>
        /// <param name="suiteId">Suite id.</param>
        /// <param name="owner">Suite instance.</param>
        /// <param name="caseId">Case id.</param>
        /// <param name="name">Display name.</param>
        /// <param name="body">Body.</param>
        /// <param name="prepare">Optional per-fixture preparation (for example installing the stub captain).</param>
        /// <returns>Descriptor.</returns>
        public static TestCaseDescriptor Case(string suiteId, object owner, string caseId, string name, Func<ArmadaClient, E2EServerFixture, Task> body, Action<E2EServerFixture>? prepare = null)
        {
            return new TestCaseDescriptor(
                suiteId: suiteId,
                caseId: caseId,
                displayName: name,
                executeAsync: async (CancellationToken ct) =>
                {
                    E2EServerFixture fx = await E2EServerFixture.AcquireAsync(owner).ConfigureAwait(false);
                    prepare?.Invoke(fx);
                    using (ArmadaClient client = LiveServerSetup.Admin(fx))
                    {
                        await body(client, fx).ConfigureAwait(false);
                    }
                },
                tags: new List<string> { TestTags.EndToEnd });
        }

        /// <summary>
        /// Expect the call to fail with an <see cref="ArmadaApiException"/> whose status is in the given range.
        /// </summary>
        /// <param name="call">Call.</param>
        /// <param name="label">Label.</param>
        /// <param name="minStatus">Lowest accepted status.</param>
        /// <param name="maxStatus">Highest accepted status.</param>
        /// <returns>The exception.</returns>
        public static async Task<ArmadaApiException> ExpectErrorAsync(Func<Task> call, string label, int minStatus = 400, int maxStatus = 599)
        {
            try
            {
                await call().ConfigureAwait(false);
            }
            catch (ArmadaApiException ex)
            {
                if (ex.StatusCode < minStatus || ex.StatusCode > maxStatus)
                    throw new AssertionException(label + ": expected HTTP " + minStatus + "-" + maxStatus + " but got " + ex.StatusCode + ": " + ex.Message);
                return ex;
            }

            throw new AssertionException(label + ": expected an error reply");
        }

        /// <summary>
        /// Short unique suffix.
        /// </summary>
        /// <returns>Six hex characters.</returns>
        public static string Suffix()
        {
            return Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        #endregion
    }
}
