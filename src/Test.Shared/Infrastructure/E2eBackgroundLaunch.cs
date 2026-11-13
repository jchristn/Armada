namespace Test.Shared.Infrastructure
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Models;

    /// <summary>
    /// The Admiral returns from dispatch once a mission is admitted (Assigned) and provisions the dock and launches the
    /// agent in the background. Tests that assert on running processes wait here until no admitted mission is still
    /// being launched.
    /// </summary>
    public static class E2eBackgroundLaunch
    {
        /// <summary>Wait until no mission visible to <paramref name="client"/> is Assigned, or the timeout passes.</summary>
        /// <param name="client">Authenticated client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="timeoutSeconds">Most seconds to wait.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="client"/> is null.</exception>
        public static async Task SettleAsync(HttpClient client, CancellationToken token = default, int timeoutSeconds = 20)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                using HttpResponseMessage response = await client.GetAsync("/api/v1/missions?status=Assigned&pageSize=1", token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode && (await JsonHelper.DeserializeAsync<EnumerationResult<Mission>>(response).ConfigureAwait(false)).TotalRecords == 0) return;
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
    }
}
