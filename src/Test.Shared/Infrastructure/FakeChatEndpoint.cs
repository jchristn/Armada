namespace Test.Shared.Infrastructure
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;

    /// <summary>
    /// A loopback HTTP endpoint that answers every POST with an OpenAI-style chat completion (and an Ollama-style chat
    /// reply on /api/chat) carrying <see cref="Reply"/>, or with <see cref="Status"/> when it is not 200. Records each
    /// request as "METHOD path".
    /// </summary>
    public sealed class FakeChatEndpoint : IDisposable
    {
        /// <summary>Base URL (http://127.0.0.1:port).</summary>
        public string BaseUrl { get; }

        /// <summary>Text the model replies with.</summary>
        public string Reply { get; set; } = "The landing refused to overwrite a dirty checkout. Commit or stash, then retry.";

        /// <summary>HTTP status to answer with.</summary>
        public int Status { get; set; } = 200;

        /// <summary>Requests seen.</summary>
        public List<string> Requests
        {
            get { lock (_Requests) return new List<string>(_Requests); }
        }

        /// <summary>Bodies of the requests seen.</summary>
        public List<string> Bodies
        {
            get { lock (_Requests) return new List<string>(_Bodies); }
        }

        private readonly HttpListener _Listener;
        private readonly List<string> _Requests = new List<string>();
        private readonly List<string> _Bodies = new List<string>();
        private readonly Task _Loop;

        /// <summary>Start the endpoint.</summary>
        public FakeChatEndpoint()
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
                string body;
                using (System.IO.StreamReader reader = new System.IO.StreamReader(context.Request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync().ConfigureAwait(false);
                lock (_Requests) { _Requests.Add(context.Request.HttpMethod + " " + path); _Bodies.Add(body); }
                string reply = JsonSerializer.Serialize(Reply);
                string json = path.EndsWith("/api/chat", StringComparison.Ordinal)
                    ? "{\"model\":\"fake\",\"message\":{\"role\":\"assistant\",\"content\":" + reply + "},\"done\":true}"
                    : "{\"id\":\"chatcmpl-fake\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"fake\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" + reply + "},\"finish_reason\":\"stop\"}],\"usage\":{\"prompt_tokens\":1,\"completion_tokens\":1,\"total_tokens\":2}}";
                byte[] bytes = Encoding.UTF8.GetBytes(Status == 200 ? json : "{\"error\":{\"message\":\"fake failure\"}}");
                context.Response.StatusCode = Status;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                context.Response.Close();
            }
        }
    }
}
