namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Server;
    using Microsoft.Extensions.Logging;
    using SyslogLogging;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>
    /// Descriptors for Admiral log export through <see cref="ArmadaTelemetryHost"/>. An Information entry
    /// that carries secret-shaped message text and exception text must leave the host as one redacted
    /// message and no exception object. A failing sink must not escape the forwarder, and the host must
    /// count that failure. Debug stays local, and dispose removes the handler.
    /// </summary>
    public sealed class ArmadaTelemetryHostSuite : IArmadaTestSuite
    {
        #region Private-Members

        private const string SuiteId = "Services.ArmadaTelemetryHost";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the descriptor for the Admiral telemetry log-export suite.
        /// </summary>
        /// <returns>The suite descriptor.</returns>
        public TestSuiteDescriptor Build()
        {
            List<TestCaseDescriptor> cases = new List<TestCaseDescriptor>();

            cases.Add(Case(
                "redacts_exported_message_and_exception",
                "An exported Information entry redacts message and exception secrets and omits the exception object",
                TestTags.Positive,
                RedactsExportedMessageAndException));

            cases.Add(Case(
                "counts_forwarding_failures",
                "A failing export sink does not escape and increases the forwarding failure count",
                TestTags.Negative,
                CountsForwardingFailures));

            cases.Add(Case(
                "skips_debug_and_unsubscribes_on_dispose",
                "Debug is not exported and Dispose removes the export handler",
                TestTags.Positive,
                SkipsDebugAndUnsubscribesOnDispose));

            return new TestSuiteDescriptor(
                suiteId: SuiteId,
                displayName: "Armada Telemetry Host",
                cases: cases);
        }

        #endregion

        #region Private-Methods

        private static void RedactsExportedMessageAndException()
        {
            string messageSecret = "super" + "secretvalue";
            string exceptionSecret = "abcdef" + "123456";
            string message = "provider failed password=" + messageSecret;
            string exceptionText = "token=" + exceptionSecret + " rejected";
            LogEntry entry = new LogEntry(Severity.Info, message, new InvalidOperationException(exceptionText));

            LoggingModule logging = CreateLogging();
            using (ArmadaTelemetryHost host = new ArmadaTelemetryHost(logging))
            {
                CaptureLogger sink = new CaptureLogger();
                SetExportSink(host, sink);
                InvokeForward(host, entry);

                AssertEqual(1, sink.Calls.Count, "one Information entry is exported");
                CapturedCall call = sink.Calls[0];
                string exported = call.Text;
                if (call.Exception != null)
                {
                    exported = exported + Environment.NewLine + call.Exception.ToString();
                }

                bool rawSecret = exported.Contains(messageSecret, StringComparison.Ordinal)
                    || exported.Contains(exceptionSecret, StringComparison.Ordinal);
                AssertFalse(rawSecret || call.Exception != null, "raw secrets and the exception object stay out of the export");
                AssertTrue(call.Text.Contains("InvalidOperationException", StringComparison.Ordinal), "exception text is part of the exported message");
                AssertTrue(call.Text.Contains("rejected", StringComparison.Ordinal), "non-secret exception text is kept");
                AssertTrue(call.Text.Contains("[REDACTED]", StringComparison.Ordinal), "secret-shaped text is redacted");
                AssertNull(call.Exception, "the export does not include an exception object");
            }
        }

        private static void CountsForwardingFailures()
        {
            LoggingModule logging = CreateLogging();
            int moduleLogs = 0;
            Action<LogEntry> countModuleLogs = entry => moduleLogs++;
            logging.MessageLogged += countModuleLogs;
            try
            {
                using (ArmadaTelemetryHost host = new ArmadaTelemetryHost(logging))
                {
                    CaptureLogger sink = new CaptureLogger();
                    sink.ThrowOnLog = true;
                    SetExportSink(host, sink);
                    LogEntry entry = new LogEntry(Severity.Info, "export attempt");

                    Exception? escaped = null;
                    try
                    {
                        InvokeForward(host, entry);
                    }
                    catch (TargetInvocationException ex)
                    {
                        escaped = ex.InnerException;
                    }

                    AssertNull(escaped, "a sink failure does not escape the forwarder");
                    AssertEqual(0, moduleLogs, "a forwarding failure is not logged through the same module");
                    long failures = ReadForwardingFailures(host);
                    AssertEqual(1L, failures, "a sink failure increases the forwarding failure count");
                }
            }
            finally
            {
                logging.MessageLogged -= countModuleLogs;
            }
        }

        private static void SkipsDebugAndUnsubscribesOnDispose()
        {
            LoggingModule logging = CreateLogging();
            string debugMarker = "debug-marker-" + "alpha";
            string infoMarker = "info-marker-" + "beta";
            string afterMarker = "after-dispose-" + "gamma";
            CaptureLogger sink = new CaptureLogger();

            using (ArmadaTelemetryHost host = new ArmadaTelemetryHost(logging))
            {
                SetExportSink(host, sink);
                Action<LogEntry> forwarder = CreateForwarder(host);
                SetForwarder(host, forwarder);
                logging.MessageLogged += forwarder;

                logging.LogEntry(new LogEntry(Severity.Debug, debugMarker));
                AssertEqual(0, sink.Calls.Count, "Debug is not exported");

                logging.LogEntry(new LogEntry(Severity.Info, infoMarker));
                AssertEqual(1, sink.Calls.Count, "Information is exported");
                AssertTrue(sink.Calls[0].Text.Contains(infoMarker, StringComparison.Ordinal), "the information message is exported");
            }

            int afterDispose = sink.Calls.Count;
            logging.LogEntry(new LogEntry(Severity.Info, afterMarker));
            AssertEqual(afterDispose, sink.Calls.Count, "Dispose removes the export handler");
        }

        private static LoggingModule CreateLogging()
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            logging.Settings.MinimumSeverity = Severity.Debug;
            return logging;
        }

        private static MethodInfo RequireForwardMethod()
        {
            MethodInfo method = typeof(ArmadaTelemetryHost).GetMethod(
                "ForwardLogEntry",
                BindingFlags.Instance | BindingFlags.NonPublic);
            AssertNotNull(method, "ForwardLogEntry is the log export path");
            return method;
        }

        private static void SetExportSink(ArmadaTelemetryHost host, CaptureLogger sink)
        {
            FieldInfo field = typeof(ArmadaTelemetryHost).GetField(
                "_LokiLogger",
                BindingFlags.Instance | BindingFlags.NonPublic);
            AssertNotNull(field, "the export sink is the telemetry logger");
            field.SetValue(host, sink);
        }

        private static void InvokeForward(ArmadaTelemetryHost host, LogEntry entry)
        {
            MethodInfo method = RequireForwardMethod();
            method.Invoke(host, new object[] { entry });
        }

        private static Action<LogEntry> CreateForwarder(ArmadaTelemetryHost host)
        {
            MethodInfo method = RequireForwardMethod();
            Delegate handler = Delegate.CreateDelegate(typeof(Action<LogEntry>), host, method);
            return (Action<LogEntry>)handler;
        }

        private static void SetForwarder(ArmadaTelemetryHost host, Action<LogEntry> forwarder)
        {
            FieldInfo field = typeof(ArmadaTelemetryHost).GetField(
                "_MessageForwarder",
                BindingFlags.Instance | BindingFlags.NonPublic);
            AssertNotNull(field, "Dispose removes the stored export handler");
            field.SetValue(host, forwarder);
        }

        private static long ReadForwardingFailures(ArmadaTelemetryHost host)
        {
            PropertyInfo property = typeof(ArmadaTelemetryHost).GetProperty(
                "LogForwardingFailures",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            AssertNotNull(property, "forwarding failures are counted on the telemetry host");
            object value = property.GetValue(host);
            AssertNotNull(value, "the forwarding failure count has a value");
            return (long)value;
        }

        private static TestCaseDescriptor Case(string caseId, string displayName, string tag, Action body)
        {
            return new TestCaseDescriptor(
                suiteId: SuiteId,
                caseId: caseId,
                displayName: displayName,
                executeAsync: (CancellationToken ct) =>
                {
                    body();
                    return Task.CompletedTask;
                },
                tags: new List<string> { tag });
        }

        private sealed class CapturedCall
        {
            public CapturedCall(string text, Exception? exception)
            {
                Text = text ?? String.Empty;
                Exception = exception;
            }

            public string Text { get; }

            public Exception? Exception { get; }
        }

        private sealed class CaptureLogger : ILogger
        {
            private readonly List<CapturedCall> _Calls = new List<CapturedCall>();

            public bool ThrowOnLog { get; set; }

            public List<CapturedCall> Calls => _Calls;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return new EmptyScope();
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (ThrowOnLog)
                {
                    throw new InvalidOperationException("sink failed");
                }

                string text = String.Empty;
                if (formatter != null)
                {
                    text = formatter(state, exception) ?? String.Empty;
                }

                _Calls.Add(new CapturedCall(text, exception));
            }
        }

        private sealed class EmptyScope : IDisposable
        {
            public void Dispose()
            {
            }
        }

        #endregion
    }
}
