namespace Armada.Test.Automated.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Armada.Test.Common;

    public class SignalTests : TestSuite
    {
        #region Public-Members

        public override string Name => "Signals";

        #endregion

        #region Private-Members

        private HttpClient _AuthClient;
        private HttpClient _UnauthClient;
        private List<string> _CreatedSignalIds = new List<string>();
        private List<string> _CreatedCaptainIds = new List<string>();

        #endregion

        #region Constructors-and-Factories

        public SignalTests(HttpClient authClient, HttpClient unauthClient)
        {
            _AuthClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
            _UnauthClient = unauthClient ?? throw new ArgumentNullException(nameof(unauthClient));
        }

        #endregion

        #region Public-Methods

        protected override async Task RunTestsAsync()
        {
            #region Create-Signal-Tests

            await RunTest("CreateSignal_ReturnsCreated201", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Type = "Nudge", Payload = "hello" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                _CreatedSignalIds.Add(doc.RootElement.GetProperty("Id").GetString()!);
            });

            await RunTest("CreateSignal_ReturnsCorrectProperties", async () =>
            {
                string captainId = await CreateCaptainAsync("signal-props-captain");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Type = "Nudge", Payload = "hello", ToCaptainId = captainId }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string signalId = root.GetProperty("Id").GetString()!;
                _CreatedSignalIds.Add(signalId);

                AssertStartsWith("sig_", signalId);
                AssertEqual("Nudge", root.GetProperty("Type").GetString()!);
                AssertEqual("hello", root.GetProperty("Payload").GetString()!);
                AssertEqual(captainId, root.GetProperty("ToCaptainId").GetString()!);
                AssertFalse(root.GetProperty("Read").GetBoolean());
                AssertTrue(root.TryGetProperty("CreatedUtc", out _));
            });

            await RunTest("CreateSignal_WithAllFields_ReturnsAllFields", async () =>
            {
                string recipientId = await CreateCaptainAsync("signal-recipient");
                string senderId = await CreateCaptainAsync("signal-sender");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        Type = "Mail",
                        Payload = "detailed payload",
                        ToCaptainId = recipientId,
                        FromCaptainId = senderId
                    }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string signalId = root.GetProperty("Id").GetString()!;
                _CreatedSignalIds.Add(signalId);

                AssertStartsWith("sig_", signalId);
                AssertEqual("Mail", root.GetProperty("Type").GetString()!);
                AssertEqual("detailed payload", root.GetProperty("Payload").GetString()!);
                AssertEqual(recipientId, root.GetProperty("ToCaptainId").GetString()!);
                AssertEqual(senderId, root.GetProperty("FromCaptainId").GetString()!);
                AssertFalse(root.GetProperty("Read").GetBoolean());
            });

            await RunTest("CreateSignal_WithMinimalFields_JustType_Returns201", async () =>
            {
                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Type = "Heartbeat" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals", content);
                AssertEqual(HttpStatusCode.Created, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                string signalId = root.GetProperty("Id").GetString()!;
                _CreatedSignalIds.Add(signalId);

                AssertStartsWith("sig_", signalId);
                AssertEqual("Heartbeat", root.GetProperty("Type").GetString()!);
            });

            await RunTest("CreateMultipleSignals_EachGetsUniqueId", async () =>
            {
                JsonElement sig1 = await CreateSignalAsync("Nudge", "first");
                JsonElement sig2 = await CreateSignalAsync("Mail", "second");
                JsonElement sig3 = await CreateSignalAsync("Heartbeat", "third");

                string id1 = sig1.GetProperty("Id").GetString()!;
                string id2 = sig2.GetProperty("Id").GetString()!;
                string id3 = sig3.GetProperty("Id").GetString()!;

                AssertStartsWith("sig_", id1);
                AssertStartsWith("sig_", id2);
                AssertStartsWith("sig_", id3);
                AssertNotEqual(id1, id2);
                AssertNotEqual(id2, id3);
                AssertNotEqual(id1, id3);
            });

            await RunTest("CreateSignal_AllSignalTypes_Succeed", async () =>
            {
                string[] types = new[] { "Assignment", "Progress", "Completion", "Error", "Heartbeat", "Nudge", "Mail" };

                foreach (string type in types)
                {
                    StringContent content = new StringContent(
                        JsonSerializer.Serialize(new { Type = type }),
                        Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals", content);
                    AssertEqual(HttpStatusCode.Created, response.StatusCode);

                    string body = await response.Content.ReadAsStringAsync();
                    JsonDocument doc = JsonDocument.Parse(body);
                    _CreatedSignalIds.Add(doc.RootElement.GetProperty("Id").GetString()!);
                    AssertEqual(type, doc.RootElement.GetProperty("Type").GetString()!);
                }
            });

            #endregion

            #region List-Signals-Tests

            await RunTest("ListSignals_Empty_ReturnsEmptyArray", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals");
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertEqual(JsonValueKind.Array, doc.RootElement.GetProperty("Objects").ValueKind);
                AssertTrue(doc.RootElement.GetProperty("Success").GetBoolean());
            });

            await RunTest("ListSignals_Empty_ReturnsCorrectEnumerationStructure", async () =>
            {
                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertTrue(root.GetProperty("Success").GetBoolean());
                AssertEqual(1, root.GetProperty("PageNumber").GetInt32());
                AssertTrue(root.GetProperty("PageSize").GetInt32() > 0);
            });

            await RunTest("ListSignals_AfterCreate_ReturnsSignals", async () =>
            {
                await CreateSignalAsync("Heartbeat", "ping");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 1);
            });

            await RunTest("ListSignals_AfterMultipleCreates_ReturnsAll", async () =>
            {
                await CreateSignalAsync("Nudge", "one");
                await CreateSignalAsync("Mail", "two");
                await CreateSignalAsync("Heartbeat", "three");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                AssertTrue(doc.RootElement.GetProperty("Objects").GetArrayLength() >= 3);
            });

            #endregion

            #region Pagination-Tests

            await RunTest("ListSignals_Pagination_25Signals_PageSize10_VerifyCounts", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateSignalAsync("Nudge", "signal-" + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals?pageSize=10&pageNumber=1");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(10, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(1, root.GetProperty("PageNumber").GetInt32());
                AssertEqual(10, root.GetProperty("PageSize").GetInt32());
                AssertTrue(root.GetProperty("Success").GetBoolean());
            });

            await RunTest("ListSignals_Pagination_Page2", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateSignalAsync("Nudge", "signal-" + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals?pageSize=10&pageNumber=2");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(10, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, root.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("ListSignals_Pagination_LastPage_PartialResults", async () =>
            {
                for (int i = 0; i < 25; i++)
                {
                    await CreateSignalAsync("Nudge", "signal-" + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals?pageSize=10&pageNumber=3");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertTrue(root.GetProperty("Objects").GetArrayLength() >= 5, "Page 3 should have at least 5 items");
                AssertEqual(3, root.GetProperty("PageNumber").GetInt32());
            });

            await RunTest("ListSignals_Pagination_BeyondLastPage_ReturnsEmptyObjects", async () =>
            {
                for (int i = 0; i < 5; i++)
                {
                    await CreateSignalAsync("Nudge", "signal-" + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals?pageSize=10&pageNumber=999");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(0, root.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("ListSignals_Pagination_FirstRecordOnFirstPage", async () =>
            {
                for (int i = 0; i < 15; i++)
                {
                    await CreateSignalAsync("Nudge", "signal-" + i);
                }

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals?pageSize=5&pageNumber=1");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertEqual(5, objects.GetArrayLength());
                foreach (JsonElement obj in objects.EnumerateArray())
                {
                    AssertStartsWith("sig_", obj.GetProperty("Id").GetString()!);
                }
            });

            await RunTest("ListSignals_Ordering_DefaultIsCreatedDescending", async () =>
            {
                await CreateSignalAsync("Nudge", "first");
                await Task.Delay(50);
                await CreateSignalAsync("Nudge", "second");
                await Task.Delay(50);
                await CreateSignalAsync("Nudge", "third");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 3);

                // Verify descending order by CreatedUtc
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = DateTime.Parse(objects[i].GetProperty("CreatedUtc").GetString()!);
                    DateTime next = DateTime.Parse(objects[i + 1].GetProperty("CreatedUtc").GetString()!);
                    Assert(current >= next, "Items should be in descending order by CreatedUtc");
                }
            });

            await RunTest("ListSignals_Ordering_CreatedAscending", async () =>
            {
                await CreateSignalAsync("Nudge", "first");
                await Task.Delay(50);
                await CreateSignalAsync("Nudge", "second");
                await Task.Delay(50);
                await CreateSignalAsync("Nudge", "third");

                HttpResponseMessage response = await _AuthClient.GetAsync("/api/v1/signals?order=CreatedAscending");
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 3);

                // Verify ascending order by CreatedUtc
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = DateTime.Parse(objects[i].GetProperty("CreatedUtc").GetString()!);
                    DateTime next = DateTime.Parse(objects[i + 1].GetProperty("CreatedUtc").GetString()!);
                    Assert(current <= next, "Items should be in ascending order by CreatedUtc");
                }
            });

            #endregion

            #region Enumerate-Tests

            await RunTest("EnumerateSignals_Default_ReturnsEnumerationResult", async () =>
            {
                StringContent content = new StringContent("{}", Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                AssertEqual(HttpStatusCode.OK, response.StatusCode);

                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertTrue(root.GetProperty("Success").GetBoolean());
                AssertTrue(root.TryGetProperty("Objects", out _));
                AssertTrue(root.TryGetProperty("PageNumber", out _));
                AssertTrue(root.TryGetProperty("PageSize", out _));
                AssertTrue(root.TryGetProperty("TotalPages", out _));
                AssertTrue(root.TryGetProperty("TotalRecords", out _));
            });

            await RunTest("EnumerateSignals_EmptyDatabase_ReturnsZeroRecords", async () =>
            {
                StringContent content = new StringContent("{}", Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertTrue(doc.RootElement.TryGetProperty("TotalRecords", out _));
                AssertTrue(doc.RootElement.TryGetProperty("Objects", out _));
            });

            await RunTest("EnumerateSignals_WithPageSizeAndPageNumber", async () =>
            {
                for (int i = 0; i < 15; i++)
                {
                    await CreateSignalAsync("Nudge", "enum-" + i);
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageSize = 5, PageNumber = 2 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(5, root.GetProperty("Objects").GetArrayLength());
                AssertEqual(2, root.GetProperty("PageNumber").GetInt32());
                AssertEqual(5, root.GetProperty("PageSize").GetInt32());
            });

            await RunTest("EnumerateSignals_WithSignalTypeFilter", async () =>
            {
                await CreateSignalAsync("Nudge", "nudge-1");
                await CreateSignalAsync("Nudge", "nudge-2");
                await CreateSignalAsync("Mail", "mail-1");
                await CreateSignalAsync("Heartbeat", "hb-1");
                await CreateSignalAsync("Heartbeat", "hb-2");
                await CreateSignalAsync("Heartbeat", "hb-3");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { SignalType = "Heartbeat" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 3, "Should have at least 3 Heartbeat signals");
                foreach (JsonElement obj in objects.EnumerateArray())
                {
                    AssertEqual("Heartbeat", obj.GetProperty("Type").GetString()!);
                }
            });

            await RunTest("EnumerateSignals_WithSignalTypeFilter_NudgeOnly", async () =>
            {
                await CreateSignalAsync("Nudge", "nudge-1");
                await CreateSignalAsync("Nudge", "nudge-2");
                await CreateSignalAsync("Mail", "mail-1");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { SignalType = "Nudge" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 2, "Should have at least 2 Nudge signals");
                foreach (JsonElement obj in objects.EnumerateArray())
                {
                    AssertEqual("Nudge", obj.GetProperty("Type").GetString()!);
                }
            });

            await RunTest("EnumerateSignals_WithToCaptainIdFilter", async () =>
            {
                string aliceId = await CreateCaptainAsync("signal-alice");
                string bobId = await CreateCaptainAsync("signal-bob");

                await CreateSignalAsync("Nudge", "to-alice", toCaptainId: aliceId);
                await CreateSignalAsync("Nudge", "to-alice-2", toCaptainId: aliceId);
                await CreateSignalAsync("Nudge", "to-bob", toCaptainId: bobId);
                await CreateSignalAsync("Nudge", "to-nobody");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { ToCaptainId = aliceId }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertEqual(2, objects.GetArrayLength());
                foreach (JsonElement obj in objects.EnumerateArray())
                {
                    AssertEqual(aliceId, obj.GetProperty("ToCaptainId").GetString()!);
                }
            });

            await RunTest("EnumerateSignals_WithToCaptainIdFilter_NoMatches", async () =>
            {
                string aliceId = await CreateCaptainAsync("signal-alice-nomatch");

                await CreateSignalAsync("Nudge", "to-alice", toCaptainId: aliceId);

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { ToCaptainId = "cpt_nonexistent" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(0, doc.RootElement.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("EnumerateSignals_Ordering_CreatedDescending", async () =>
            {
                await CreateSignalAsync("Nudge", "first");
                await Task.Delay(50);
                await CreateSignalAsync("Nudge", "second");
                await Task.Delay(50);
                await CreateSignalAsync("Nudge", "third");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Order = "CreatedDescending" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 3);
                // Verify descending order
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = DateTime.Parse(objects[i].GetProperty("CreatedUtc").GetString()!);
                    DateTime next = DateTime.Parse(objects[i + 1].GetProperty("CreatedUtc").GetString()!);
                    Assert(current >= next, "Enumerate items should be in descending order");
                }
            });

            await RunTest("EnumerateSignals_Ordering_CreatedAscending", async () =>
            {
                await CreateSignalAsync("Nudge", "first");
                await Task.Delay(50);
                await CreateSignalAsync("Nudge", "second");
                await Task.Delay(50);
                await CreateSignalAsync("Nudge", "third");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { Order = "CreatedAscending" }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertTrue(objects.GetArrayLength() >= 3);
                // Verify ascending order
                for (int i = 0; i < objects.GetArrayLength() - 1; i++)
                {
                    DateTime current = DateTime.Parse(objects[i].GetProperty("CreatedUtc").GetString()!);
                    DateTime next = DateTime.Parse(objects[i + 1].GetProperty("CreatedUtc").GetString()!);
                    Assert(current <= next, "Enumerate items should be in ascending order");
                }
            });

            await RunTest("EnumerateSignals_CombinedFilters_TypeAndToCaptainId", async () =>
            {
                string aliceId = await CreateCaptainAsync("signal-combined-alice");
                string bobId = await CreateCaptainAsync("signal-combined-bob");

                await CreateSignalAsync("Nudge", "nudge-alice", toCaptainId: aliceId);
                await CreateSignalAsync("Mail", "mail-alice", toCaptainId: aliceId);
                await CreateSignalAsync("Nudge", "nudge-bob", toCaptainId: bobId);
                await CreateSignalAsync("Heartbeat", "hb-nobody");

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { SignalType = "Nudge", ToCaptainId = aliceId }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement objects = doc.RootElement.GetProperty("Objects");

                AssertEqual(1, objects.GetArrayLength());
                AssertEqual("Nudge", objects[0].GetProperty("Type").GetString()!);
                AssertEqual(aliceId, objects[0].GetProperty("ToCaptainId").GetString()!);
            });

            await RunTest("EnumerateSignals_WithPagination_PageSize2", async () =>
            {
                for (int i = 0; i < 7; i++)
                {
                    await CreateSignalAsync("Nudge", "page-" + i);
                }

                StringContent content = new StringContent(
                    JsonSerializer.Serialize(new { PageSize = 2, PageNumber = 1 }),
                    Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);
                JsonElement root = doc.RootElement;

                AssertEqual(2, root.GetProperty("Objects").GetArrayLength());
            });

            await RunTest("EnumerateSignals_QuerystringOverrides_PageSize", async () =>
            {
                for (int i = 0; i < 10; i++)
                {
                    await CreateSignalAsync("Nudge", "qs-" + i);
                }

                StringContent content = new StringContent("{}", Encoding.UTF8, "application/json");

                HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals/enumerate?pageSize=3", content);
                string body = await response.Content.ReadAsStringAsync();
                JsonDocument doc = JsonDocument.Parse(body);

                AssertEqual(3, doc.RootElement.GetProperty("Objects").GetArrayLength());
                AssertEqual(3, doc.RootElement.GetProperty("PageSize").GetInt32());
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
            StringContent content = new StringContent(
                JsonSerializer.Serialize(new { Name = uniqueName }),
                Encoding.UTF8, "application/json");
            HttpResponseMessage resp = await _AuthClient.PostAsync("/api/v1/captains", content);
            string body = await resp.Content.ReadAsStringAsync();
            string captainId = JsonDocument.Parse(body).RootElement.GetProperty("Id").GetString()!;
            _CreatedCaptainIds.Add(captainId);
            return captainId;
        }

        private async Task<JsonElement> CreateSignalAsync(
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

            StringContent content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8, "application/json");

            HttpResponseMessage response = await _AuthClient.PostAsync("/api/v1/signals", content);
            string responseBody = await response.Content.ReadAsStringAsync();
            JsonElement root = JsonDocument.Parse(responseBody).RootElement;
            _CreatedSignalIds.Add(root.GetProperty("Id").GetString()!);
            return root;
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
