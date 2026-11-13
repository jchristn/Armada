namespace Armada.Benchmark.Client
{
    using System;

    /// <summary>A REST request returned a non-success status.</summary>
    public sealed class BenchRequestException : Exception
    {
        /// <summary>HTTP status code.</summary>
        public int StatusCode { get; }

        /// <summary>Response body.</summary>
        public string Body { get; }

        /// <summary>Create the exception.</summary>
        /// <param name="message">Method, path, status and a bounded response excerpt.</param>
        /// <param name="statusCode">HTTP status code.</param>
        /// <param name="body">Response body.</param>
        public BenchRequestException(string message, int statusCode, string body) : base(message)
        {
            StatusCode = statusCode;
            Body = body ?? "";
        }
    }
}
