namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;

    /// <summary>
    /// A loopback model server for context discovery tests: answers the routes set in <see cref="Routes"/> ("METHOD path"
    /// to a JSON body), any other POST with an OpenAI-style chat completion (an Ollama-style reply on /api/chat), and
    /// anything else with 404. Records each request as "METHOD path".
    /// </summary>
    public sealed class FakeModelServer : IDisposable
    {
        /// <summary>Base URL (http://127.0.0.1:port).</summary>
        public string BaseUrl { get; }

        /// <summary>Bodies by "METHOD path", for example "GET /api/ps".</summary>
        public ConcurrentDictionary<string, string> Routes { get; } = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Requests seen.</summary>
        public List<string> Requests
        {
            get { lock (_Requests) return new List<string>(_Requests); }
        }

        private readonly HttpListener _Listener;
        private readonly List<string> _Requests = new List<string>();
        private readonly Task _Loop;

        /// <summary>Start the server.</summary>
        public FakeModelServer()
        {
            _Listener = TestPorts.StartHttpListener(p => new[] { "http://127.0.0.1:" + p + "/" }, out int port);
            BaseUrl = "http://127.0.0.1:" + port;
            _Loop = Task.Run(LoopAsync);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try { _Listener.Stop(); } catch (ObjectDisposedException) { }
            _Listener.Close();
            try { _Loop.Wait(2000); } catch (AggregateException) { }
        }

        private async Task LoopAsync()
        {
            while (_Listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _Listener.GetContextAsync().ConfigureAwait(false); }
                catch (Exception) { return; }
                string path = context.Request.Url!.AbsolutePath;
                string key = context.Request.HttpMethod + " " + path;
                using (System.IO.StreamReader reader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8)) await reader.ReadToEndAsync().ConfigureAwait(false);
                lock (_Requests) _Requests.Add(key);
                int status = 200;
                string json;
                if (Routes.TryGetValue(key, out string? routed)) json = routed;
                else if (context.Request.HttpMethod == "POST" && path.EndsWith("/api/chat", StringComparison.Ordinal))
                    json = "{\"model\":\"fake\",\"message\":{\"role\":\"assistant\",\"content\":\"pong\"},\"done\":true}";
                else if (context.Request.HttpMethod == "POST")
                    json = "{\"id\":\"chatcmpl-fake\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"fake\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"pong\"},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}";
                else
                {
                    status = 404;
                    json = "{\"error\":\"not found\"}";
                }

                byte[] bytes = Encoding.UTF8.GetBytes(json);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                context.Response.Close();
            }
        }
    }
}
