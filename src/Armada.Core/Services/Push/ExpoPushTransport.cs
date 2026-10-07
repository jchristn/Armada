namespace Armada.Core.Services.Push
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Armada.Core.Enums;
    using Armada.Core.Models;

    /// <summary>
    /// <see cref="IPushTransport"/> over the Expo Push Service HTTP API: POST /push/send (at most 100 messages per
    /// request) and POST /push/getReceipts (at most 1000 ids per request), with the optional access token as a bearer
    /// token. HTTP 429 and 5xx responses, timeouts, and connection failures are reported as transient.
    /// </summary>
    public class ExpoPushTransport : IPushTransport, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Default base URL of the Expo Push Service API.
        /// </summary>
        public const string DefaultBaseUrl = "https://exp.host/--/api/v2/push";

        /// <summary>
        /// Maximum messages per send request.
        /// </summary>
        public const int MaxMessagesPerRequest = 100;

        /// <summary>
        /// Maximum ticket ids per receipts request.
        /// </summary>
        public const int MaxReceiptIdsPerRequest = 1000;

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly bool _OwnsHttp;
        private readonly string _BaseUrl;
        private bool _Disposed = false;

        private static readonly JsonSerializerOptions _JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with a private HttpClient (30 second timeout, gzip and deflate decompression).
        /// </summary>
        public ExpoPushTransport()
            : this(CreateDefaultClient(), DefaultBaseUrl, true)
        {
        }

        /// <summary>
        /// Instantiate with a caller-supplied HttpClient (not disposed by this instance) and base URL.
        /// </summary>
        /// <param name="http">HTTP client.</param>
        /// <param name="baseUrl">Base URL of the push API (for example <see cref="DefaultBaseUrl"/>).</param>
        public ExpoPushTransport(HttpClient http, string baseUrl)
            : this(http, baseUrl, false)
        {
        }

        private ExpoPushTransport(HttpClient http, string baseUrl, bool ownsHttp)
        {
            _Http = http ?? throw new ArgumentNullException(nameof(http));
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
            _OwnsHttp = ownsHttp;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<List<PushTicket>> SendAsync(List<PushMessage> messages, string? accessToken, CancellationToken token = default)
        {
            if (messages == null) throw new ArgumentNullException(nameof(messages));
            if (messages.Count == 0) return new List<PushTicket>();
            if (messages.Count > MaxMessagesPerRequest) throw new ArgumentException("At most " + MaxMessagesPerRequest + " messages per request.", nameof(messages));

            string body = JsonSerializer.Serialize(messages, _JsonOptions);
            string responseText = await PostAsync(_BaseUrl + "/send", body, accessToken, token).ConfigureAwait(false);
            ExpoPushSendResponse? response = Deserialize<ExpoPushSendResponse>(responseText);
            if (response == null || response.Data == null)
                throw new PushTransportException("Expo push send returned no tickets" + DescribeErrors(response?.Errors), false);
            if (response.Data.Count != messages.Count)
                throw new PushTransportException("Expo push send returned " + response.Data.Count + " tickets for " + messages.Count + " messages", false);

            List<PushTicket> tickets = new List<PushTicket>();
            foreach (ExpoPushTicketDto dto in response.Data)
            {
                PushTicket ticket = new PushTicket();
                ticket.Ok = IsOk(dto);
                ticket.TicketId = dto.Id;
                ticket.Error = ticket.Ok ? (PushErrorCodeEnum?)null : MapError(dto.Details?.Error);
                ticket.Message = dto.Message;
                tickets.Add(ticket);
            }

            return tickets;
        }

        /// <inheritdoc />
        public async Task<List<PushReceipt>> GetReceiptsAsync(List<string> ticketIds, string? accessToken, CancellationToken token = default)
        {
            if (ticketIds == null) throw new ArgumentNullException(nameof(ticketIds));
            if (ticketIds.Count == 0) return new List<PushReceipt>();
            if (ticketIds.Count > MaxReceiptIdsPerRequest) throw new ArgumentException("At most " + MaxReceiptIdsPerRequest + " ids per request.", nameof(ticketIds));

            ExpoPushReceiptsRequest request = new ExpoPushReceiptsRequest();
            request.Ids = new List<string>(ticketIds);
            string responseText = await PostAsync(_BaseUrl + "/getReceipts", JsonSerializer.Serialize(request, _JsonOptions), accessToken, token).ConfigureAwait(false);
            ExpoPushReceiptsResponse? response = Deserialize<ExpoPushReceiptsResponse>(responseText);
            if (response == null || response.Data == null)
                throw new PushTransportException("Expo push receipts returned no data" + DescribeErrors(response?.Errors), false);

            List<PushReceipt> receipts = new List<PushReceipt>();
            foreach (KeyValuePair<string, ExpoPushTicketDto> kvp in response.Data)
            {
                PushReceipt receipt = new PushReceipt();
                receipt.TicketId = kvp.Key;
                receipt.Ok = IsOk(kvp.Value);
                receipt.Error = receipt.Ok ? (PushErrorCodeEnum?)null : MapError(kvp.Value?.Details?.Error);
                receipt.Message = kvp.Value?.Message;
                receipts.Add(receipt);
            }

            return receipts;
        }

        /// <summary>
        /// Map an Expo error code to <see cref="PushErrorCodeEnum"/>; unknown or missing codes map to Unknown.
        /// </summary>
        /// <param name="code">Expo error code.</param>
        /// <returns>The error code.</returns>
        public static PushErrorCodeEnum MapError(string? code)
        {
            if (String.IsNullOrWhiteSpace(code)) return PushErrorCodeEnum.Unknown;
            if (Enum.TryParse<PushErrorCodeEnum>(code!.Trim(), false, out PushErrorCodeEnum parsed)) return parsed;
            return PushErrorCodeEnum.Unknown;
        }

        /// <summary>
        /// Dispose the private HttpClient (a caller-supplied client is left open).
        /// </summary>
        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            if (_OwnsHttp) _Http.Dispose();
        }

        #endregion

        #region Private-Methods

        private static HttpClient CreateDefaultClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            HttpClient client = new HttpClient(handler, true);
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        private async Task<string> PostAsync(string url, string body, string? accessToken, CancellationToken token)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                if (!String.IsNullOrWhiteSpace(accessToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken!.Trim());

                HttpResponseMessage response;
                try
                {
                    response = await _Http.SendAsync(request, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (!token.IsCancellationRequested)
                {
                    throw new PushTransportException("Expo push request timed out", true, null, ex);
                }
                catch (HttpRequestException ex)
                {
                    throw new PushTransportException("Expo push request failed: " + ex.Message, true, null, ex);
                }

                using (response)
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    int status = (int)response.StatusCode;
                    if (status == 429 || status >= 500)
                        throw new PushTransportException("Expo push service returned HTTP " + status, true, status);
                    if (status < 200 || status > 299)
                    {
                        ExpoPushSendResponse? errorBody = Deserialize<ExpoPushSendResponse>(text);
                        throw new PushTransportException("Expo push service returned HTTP " + status + DescribeErrors(errorBody?.Errors), false, status);
                    }

                    return text;
                }
            }
        }

        private static T? Deserialize<T>(string text) where T : class
        {
            if (String.IsNullOrWhiteSpace(text)) return null;
            try
            {
                return JsonSerializer.Deserialize<T>(text, _JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool IsOk(ExpoPushTicketDto? dto)
        {
            return dto != null && String.Equals(dto.Status, "ok", StringComparison.OrdinalIgnoreCase);
        }

        private static string DescribeErrors(List<ExpoPushRequestError>? errors)
        {
            if (errors == null || errors.Count == 0) return String.Empty;
            List<string> codes = new List<string>();
            foreach (ExpoPushRequestError error in errors)
            {
                if (!String.IsNullOrWhiteSpace(error.Code)) codes.Add(error.Code!);
            }

            return codes.Count == 0 ? String.Empty : " (" + String.Join(", ", codes) + ")";
        }

        #endregion
    }
}
