namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A loopback HTTP stub standing in for a remote Admiral: answers the health endpoint and the mission and fleet
    /// lists with empty pages, and records every request (path and credential headers) so tests can assert where the
    /// CLI sent its calls and with which credential.
    /// </summary>
    public sealed class RecordingAdmiralStub : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL, for example http://127.0.0.1:23456.
        /// </summary>
        public string BaseUrl { get; }

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener;
        private readonly CancellationTokenSource _Cancellation = new CancellationTokenSource();
        private readonly Task _Loop;
        private readonly List<RecordedAdmiralRequest> _Requests = new List<RecordedAdmiralRequest>();
        private readonly object _Lock = new object();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start the stub on a free loopback port.
        /// </summary>
        public RecordingAdmiralStub()
        {
            int port = TestPorts.Reserve(1)[0];
            BaseUrl = "http://127.0.0.1:" + port;
            _Listener = new HttpListener();
            _Listener.Prefixes.Add(BaseUrl + "/");
            _Listener.Start();
            _Loop = Task.Run(() => ListenAsync(_Cancellation.Token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// A copy of the requests seen so far.
        /// </summary>
        /// <returns>Requests.</returns>
        public List<RecordedAdmiralRequest> Requests()
        {
            lock (_Lock) return new List<RecordedAdmiralRequest>(_Requests);
        }

        /// <summary>
        /// Stop the stub.
        /// </summary>
        public void Dispose()
        {
            _Cancellation.Cancel();
            try { _Listener.Stop(); } catch { }
            try { _Loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
            _Listener.Close();
            _Cancellation.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task ListenAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                RecordedAdmiralRequest recorded = new RecordedAdmiralRequest();
                recorded.Method = context.Request.HttpMethod;
                recorded.Path = context.Request.Url?.AbsolutePath ?? "";
                recorded.Authorization = context.Request.Headers["Authorization"];
                recorded.SessionToken = context.Request.Headers["X-Token"];
                recorded.ApiKey = context.Request.Headers["X-Api-Key"];
                lock (_Lock) _Requests.Add(recorded);

                string body;
                int status = 200;
                if (recorded.Path == "/api/v1/status/health") body = "{\"Status\":\"healthy\"}";
                else if (recorded.Path == "/api/v1/missions" || recorded.Path == "/api/v1/fleets")
                    body = "{\"Success\":true,\"PageNumber\":1,\"PageSize\":100,\"TotalPages\":0,\"TotalRecords\":0,\"Objects\":[]}";
                else
                {
                    status = 404;
                    body = "{\"Error\":\"NotFound\"}";
                }

                byte[] bytes = Encoding.UTF8.GetBytes(body);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                try
                {
                    await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }

                context.Response.Close();
            }
        }

        #endregion
    }
}
