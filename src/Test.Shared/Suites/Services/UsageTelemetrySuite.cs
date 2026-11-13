namespace Test.Shared.Suites.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Threading.Tasks;
    using Armada.Core.Services;
    using Test.Shared.Infrastructure;
    using Touchstone.Core;
    using static Test.Shared.Infrastructure.Asserts;

    /// <summary>Usage operational metrics remain bounded and cannot affect the measured work.</summary>
    public sealed class UsageTelemetrySuite : IArmadaTestSuite
    {
        /// <inheritdoc />
        public TestSuiteDescriptor Build()
        {
            const string suite = "Services.UsageTelemetry";
            return new TestSuiteDescriptor(suite, "Usage telemetry", new List<TestCaseDescriptor>
            {
                new TestCaseDescriptor(suite, "labels_and_failures", "Usage scopes report failures, active depth and bounded labels without account identity", token =>
                {
                    List<string> observed = new List<string>();
                    using MeterListener listener = new MeterListener();
                    listener.InstrumentPublished = (instrument, current) => { if (instrument.Name.StartsWith("armada.usage.", StringComparison.Ordinal)) current.EnableMeasurementEvents(instrument); };
                    listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                    {
                        observed.Add(instrument.Name);
                        foreach (KeyValuePair<string, object?> tag in tags)
                            AssertTrue(tag.Key == "operation" || tag.Key == "runtime" || tag.Key == "outcome" || tag.Key == "kind", "no account, tenant, host, captain or free-form error labels");
                    });
                    listener.Start();
                    using (UsageTelemetryOperation operation = UsageTelemetry.Start("capture", "ClaudeCode")) operation.Complete("Ok");
                    using (UsageTelemetryOperation operation = UsageTelemetry.Start("query")) { }
                    UsageTelemetry.Schedule("Joined"); UsageTelemetry.Deleted("raw", 500);
                    UsageTelemetry.ObservationAge(DateTime.UtcNow.AddMinutes(-5));
                    listener.RecordObservableInstruments();
                    AssertTrue(observed.Contains("armada.usage.operations")); AssertTrue(observed.Contains("armada.usage.active"));
                    AssertTrue(observed.Contains("armada.usage.retention.deleted"));
                    listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) => throw new InvalidOperationException("fixture telemetry sink"));
                    using (UsageTelemetryOperation operation = UsageTelemetry.Start("capture", "Codex")) operation.Complete();
                    UsageTelemetry.Schedule("Skipped"); UsageTelemetry.Deleted("raw", 1);
                    AssertNull(UsageTelemetry.Receive("invalid trace context"));
                    return Task.CompletedTask;
                })
            });
        }
    }
}
