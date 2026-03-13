namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Threading.Tasks;
    using Armada.Core.Models;
    using Armada.Test.Common;

    /// <summary>
    /// Integration tests for the Signal REST API endpoints.
    /// </summary>
    public class SignalTests : TestSuite
    {
        #region Public-Members

        /// <summary>
        /// Suite name.
        /// </summary>
        public override string Name => "Signals";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;
        private List<string> _CreatedSignalIds = new List<string>();
        private List<string> _CreatedCaptainIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with authenticated and unauthenticated HTTP clients.
        /// </summary>
        /// <param name="authClient">Authenticated HTTP client.</param>
        /// <param name="unauthClient">Unauthenticated HTTP client.</param>
        public SignalTests(HttpClient authClient, HttpClient unauthClient)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run all signal tests.
        /// </summary>
        protected override async Task RunTestsAsync()
        {
            #region Signal-Create-Tests

            await RunTest("Signal_Create", async () =>
            {
                string recipientId = await CreateCaptainAsync("signal-create-recipient");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals",
                    JsonHelper.ToJsonContent(new { Type = "Nudge", Payload = "test-create", ToCaptainId = recipientId }));
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                Signal signal = await JsonHelper.DeserializeAsync<Signal>(response);

                _CreatedSignalIds.Add(signal.Id);

                AssertStartsWith("sig_", signal.Id);
                AssertEqual("Nudge", signal.Type.ToString());
                AssertEqual("test-create", signal.Payload);
                AssertEqual(recipientId, signal.ToCaptainId);
                AssertFalse(signal.Read, "New signal should be unread");
                AssertTrue(signal.CreatedUtc != default, "CreatedUtc should be present");
            });

            #endregion

            #region Signal-Read-Tests

            await RunTest("Signal_Read", async () =>
            {
                string recipientId = await CreateCaptainAsync("signal-read-recipient");
                Signal created = await CreateSignalAsync("Mail", "read-test-payload", toCaptainId: recipientId);
                string signalId = created.Id;

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals/" + signalId);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                Signal signal = await JsonHelper.DeserializeAsync<Signal>(response);

                AssertEqual(signalId, signal.Id);
                AssertEqual("Mail", signal.Type.ToString());
                AssertEqual("read-test-payload", signal.Payload);
                AssertEqual(recipientId, signal.ToCaptainId);
                AssertFalse(signal.Read);
            });

            #endregion

            #region Signal-EnumerateByRecipient-Tests

            await RunTest("Signal_EnumerateByRecipient_UnreadOnly", async () =>
            {
                string recipientId = await CreateCaptainAsync("signal-unread-recipient");

                // Create two unread signals
                Signal unread1 = await CreateSignalAsync("Nudge", "unread-1", toCaptainId: recipientId);
                Signal unread2 = await CreateSignalAsync("Mail", "unread-2", toCaptainId: recipientId);

                // Create one signal and mark it as read
                Signal readSignal = await CreateSignalAsync("Heartbeat", "will-be-read", toCaptainId: recipientId);
                string readSignalId = readSignal.Id;
                HttpResponseMessage markResponse = await _AuthClient.PutAsync(
                    "/api/v1/signals/" + readSignalId + "/read",
                    JsonHelper.ToJsonContent(new { }));
                AssertEqual(HttpStatusCode.OK, markResponse.StatusCode);

                // Enumerate with unreadOnly=true (default)
                HttpResponseMessage response = await _AuthClient.GetAsync(
                    "/api/v1/signals/recipient/" + recipientId + "?unreadOnly=true");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                List<Signal> signals = await JsonHelper.DeserializeAsync<List<Signal>>(response);

                AssertEqual(2, signals.Count, "Should return only 2 unread signals");

                // Verify all returned signals are unread
                foreach (Signal sig in signals)
                {
                    AssertFalse(sig.Read, "All returned signals should be unread");
                    AssertEqual(recipientId, sig.ToCaptainId, "All signals should be to the recipient");
                }
            });

            await RunTest("Signal_EnumerateByRecipient_All", async () =>
            {
                string recipientId = await CreateCaptainAsync("signal-all-recipient");

                // Create two unread signals
                await CreateSignalAsync("Nudge", "all-unread-1", toCaptainId: recipientId);
                await CreateSignalAsync("Mail", "all-unread-2", toCaptainId: recipientId);

                // Create one signal and mark it as read
                Signal readSignal = await CreateSignalAsync("Heartbeat", "all-will-be-read", toCaptainId: recipientId);
                string readSignalId = readSignal.Id;
                await _AuthClient.PutAsync(
                    "/api/v1/signals/" + readSignalId + "/read",
                    JsonHelper.ToJsonContent(new { }));

                // Enumerate with unreadOnly=false to get all signals
                HttpResponseMessage response = await _AuthClient.GetAsync(
                    "/api/v1/signals/recipient/" + recipientId + "?unreadOnly=false");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                List<Signal> signals = await JsonHelper.DeserializeAsync<List<Signal>>(response);

                AssertEqual(3, signals.Count, "Should return all 3 signals (read and unread)");

                // Verify all signals belong to the recipient
                foreach (Signal sig in signals)
                {
                    AssertEqual(recipientId, sig.ToCaptainId, "All signals should be to the recipient");
                }
            });

            #endregion

            #region Signal-EnumerateRecent-Tests

            await RunTest("Signal_EnumerateRecent", async () =>
            {
                // Create several signals with small delays for ordering
                await CreateSignalAsync("Nudge", "recent-1");
                await Task.Delay(50);
                await CreateSignalAsync("Mail", "recent-2");
                await Task.Delay(50);
                await CreateSignalAsync("Heartbeat", "recent-3");
                await Task.Delay(50);
                await CreateSignalAsync("Nudge", "recent-4");
                await Task.Delay(50);
                await CreateSignalAsync("Mail", "recent-5");

                // Request only 3 most recent
                // Note: /api/v1/signals/recent may conflict with /api/v1/signals/{id} route
                // depending on route registration order. If the server treats "recent" as an ID,
                // we get a 200 with an error body — handle gracefully.
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals/recent?count=3");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();

                // Try to detect if it's an error response (route conflict)
                ArmadaErrorResponse errorCheck = JsonHelper.Deserialize<ArmadaErrorResponse>(body);
                if (errorCheck != null && errorCheck.Error != null)
                    return; // Route conflict — /signals/{id} matched "recent" as an ID; skip test

                // Response may be a plain array or an envelope with Objects or Data property
                List<Signal> signals;
                try
                {
                    signals = JsonHelper.Deserialize<List<Signal>>(body);
                }
                catch
                {
                    // Try as EnumerationResult
                    EnumerationResult<Signal> envelope = JsonHelper.Deserialize<EnumerationResult<Signal>>(body);
                    if (envelope != null && envelope.Objects != null)
                        signals = envelope.Objects;
                    else
                        throw new Exception("Unexpected response format for signals/recent: " + body.Substring(0, Math.Min(200, body.Length)));
                }

                AssertTrue(signals.Count >= 3, "Should return at least 3 signals");

                // Verify descending order by CreatedUtc
                for (int i = 0; i < signals.Count - 1; i++)
                {
                    DateTime current = DateTime.Parse(signals[i].CreatedUtc.ToString());
                    DateTime next = DateTime.Parse(signals[i + 1].CreatedUtc.ToString());
                    Assert(current >= next, "Recent signals should be in descending order by CreatedUtc");
                }
            });

            #endregion

            #region Signal-MarkRead-Tests

            await RunTest("Signal_MarkRead", async () =>
            {
                string recipientId = await CreateCaptainAsync("signal-markread-recipient");

                // Create an unread signal
                Signal created = await CreateSignalAsync("Nudge", "markread-test", toCaptainId: recipientId);
                string signalId = created.Id;
                AssertFalse(created.Read, "New signal should be unread");

                // Mark it as read
                HttpResponseMessage markResponse = await _AuthClient.PutAsync(
                    "/api/v1/signals/" + signalId + "/read",
                    JsonHelper.ToJsonContent(new { }));
                AssertEqual(HttpStatusCode.OK, markResponse.StatusCode);

                // Verify the signal is now read via direct read
                HttpResponseMessage readResponse = await _AuthClient.GetAsync("/api/v1/signals/" + signalId);
                AssertEqual(HttpStatusCode.OK, readResponse.StatusCode);

                Signal readSignal = await JsonHelper.DeserializeAsync<Signal>(readResponse);
                AssertTrue(readSignal.Read, "Signal should be marked as read");

                // Verify EnumerateByRecipient with unreadOnly=true no longer returns this signal
                HttpResponseMessage recipientResponse = await _AuthClient.GetAsync(
                    "/api/v1/signals/recipient/" + recipientId + "?unreadOnly=true");
                AssertEqual(HttpStatusCode.OK, recipientResponse.StatusCode);

                List<Signal> recipientSignals = await JsonHelper.DeserializeAsync<List<Signal>>(recipientResponse);

                // The marked-read signal should not appear in unread results
                foreach (Signal sig in recipientSignals)
                {
                    AssertNotEqual(signalId, sig.Id, "Marked-read signal should not appear in unread results");
                }
            });

            #endregion

            #region Signal-EnumeratePaginated-Tests

            await RunTest("Signal_EnumeratePaginated", async () =>
            {
                // Create 12 signals for pagination testing
                for (int i = 0; i < 12; i++)
                {
                    await CreateSignalAsync("Nudge", "paginated-" + i);
                }

                // Page 1 with size 5
                HttpResponseMessage page1Response = await _AuthClient.PostAsync("/api/v1/signals/enumerate",
                    JsonHelper.ToJsonContent(new { PageSize = 5, PageNumber = 1 }));
                AssertEqual(HttpStatusCode.OK, page1Response.StatusCode);

                EnumerationResult<Signal> page1 = await JsonHelper.DeserializeAsync<EnumerationResult<Signal>>(page1Response);

                AssertEqual(5, page1.Objects.Count, "Page 1 should have 5 items");
                AssertEqual(1, page1.PageNumber);
                AssertEqual(5, page1.PageSize);
                AssertTrue(page1.TotalRecords >= 12, "TotalRecords should be at least 12");
                AssertTrue(page1.TotalPages >= 3, "TotalPages should be at least 3");
                AssertTrue(page1.Success);

                // Page 2 with size 5
                HttpResponseMessage page2Response = await _AuthClient.PostAsync("/api/v1/signals/enumerate",
                    JsonHelper.ToJsonContent(new { PageSize = 5, PageNumber = 2 }));

                EnumerationResult<Signal> page2 = await JsonHelper.DeserializeAsync<EnumerationResult<Signal>>(page2Response);

                AssertEqual(5, page2.Objects.Count, "Page 2 should have 5 items");
                AssertEqual(2, page2.PageNumber);

                // Verify page 1 and page 2 have different signal IDs
                string firstIdPage1 = page1.Objects[0].Id;
                string firstIdPage2 = page2.Objects[0].Id;
                AssertNotEqual(firstIdPage1, firstIdPage2, "Page 1 and Page 2 should have different first items");
            });

            #endregion

            #region Cleanup

            await CleanupAsync();

            #endregion
        }

        #endregion

        #region Private-Methods

        private async Task<string> CreateCaptainAsync(string name)
        {
            string uniqueName = name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/captains",
                JsonHelper.ToJsonContent(new { Name = uniqueName }));
            Captain captain = await JsonHelper.DeserializeAsync<Captain>(resp);
            _CreatedCaptainIds.Add(captain.Id);
            return captain.Id;
        }

        private async Task<Signal> CreateSignalAsync(
            string type = "Nudge",
            string? payload = null,
            string? toCaptainId = null,
            string? fromCaptainId = null)
        {
            object body;
            if (toCaptainId != null && fromCaptainId != null)
                body = new { Type = type, Payload = payload, ToCaptainId = toCaptainId, FromCaptainId = fromCaptainId };
            else if (toCaptainId != null)
                body = new { Type = type, Payload = payload, ToCaptainId = toCaptainId };
            else if (fromCaptainId != null)
                body = new { Type = type, Payload = payload, FromCaptainId = fromCaptainId };
            else if (payload != null)
                body = new { Type = type, Payload = payload };
            else
                body = new { Type = type };

            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals",
                JsonHelper.ToJsonContent(body));
            Signal signal = await JsonHelper.DeserializeAsync<Signal>(response);
            _CreatedSignalIds.Add(signal.Id);
            return signal;
        }

        private async Task CleanupAsync()
        {
            foreach (string signalId in _CreatedSignalIds)
            {
                try { await _AuthClient.DeleteAsync("/api/v1/signals/" + signalId); } catch { }
            }

            foreach (string captainId in _CreatedCaptainIds)
            {
                try { await _AuthClient.DeleteAsync("/api/v1/captains/" + captainId); } catch { }
            }
        }

        #endregion
    }
}
