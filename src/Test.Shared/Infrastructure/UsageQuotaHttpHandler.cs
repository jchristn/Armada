namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    /// <summary>Quota-only deterministic transport. Never stores request credentials or model prompts.</summary>
    internal class UsageQuotaHttpHandler : HttpMessageHandler
    {
        internal Queue<string> Responses { get; } = new Queue<string>();
        internal List<string> Paths { get; } = new List<string>();
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Paths.Add(request.Method + " " + request.RequestUri!.AbsolutePath);
            HttpResponseMessage response = new HttpResponseMessage(Status) { Content = new StringContent(Responses.Count > 0 ? Responses.Dequeue() : "{}") };
            if ((int)Status == 429) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(10));
            return Task.FromResult(response);
        }
    }
}
