using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;

namespace Shortener.Infrastructure.SmsProviders;

/// <summary>
/// §M5.1 — SOAP client for www.afe.ir's "BoxService" (برند "واید" / Wide, از شرکت عصر فرا ارتباط).
/// No public documentation for this service could be found anywhere (afe.ir returns 403 to automated
/// fetches; no developer-docs page, no open-source integration referencing it beyond a much narrower
/// legacy GET endpoint). This implementation instead comes directly from the user's own
/// auto-generated WCF proxy (svcutil/"Add Service Reference" output) against their real, working
/// FaraErtebatSmsService reference, plus the corresponding client endpoint config they provided:
///
///     Endpoint: https://www.afe.ir/WebService/V7/BoxService.asmx (basicHttpBinding, SOAP 1.1)
///     Namespace: http://www.afe.ir/
///     Operations used here: SendMessage, GetMessagesStatus, GetRemainingCredit
///     (SendMessagePeerToPeer and GetMessageID exist on the service too but nothing in this codebase
///     needs them — bulk/pattern sending here always goes through SendMessage one recipient at a time.)
///
/// The operation names, parameter names, and types are therefore confirmed, not guessed. Two things
/// are NOT confirmed and are flagged inline where they matter:
///   1. The exact wire-level XML (namespace placement on the Mobile/CheckingMessageID array items) is
///      reconstructed from standard .NET DataContractSerializer conventions for the
///      [CollectionDataContract(ItemName="string")]-style attributes on the generated proxy, not from
///      a captured real request/response — get a raw SOAP trace from the user's other project
///      (Fiddler, WCF message logging) to tighten this up if the first live call fails.
///   2. The "Type" request field's valid values are unknown, so it's sent as an empty string (asked
///      the user; unanswered as of this writing). If AFE's server rejects that, every send will fail
///      loudly and immediately — not a silent misbehavior — but resolve this before relying on the
///      provider for real traffic, since in Iran a message-type field can also carry deliverability/
///      DND-list-bypass regulatory meaning (خدماتی vs تبلیغاتی) that's worth getting right regardless.
///   3. GetMessagesStatus/GetMessageStatus return plain strings, not documented status codes — mapped
///      conservatively by substring (deliver/fail keywords) with everything else staying Sent, the
///      same "don't guess at an unknown vocabulary" stance already used for the other three providers.
///
/// No live account exists in this sandbox — never exercised against the real service, only against a
/// mocked HttpMessageHandler in tests. Treat as contract-plausible-but-wire-unverified until validated
/// with real credentials.
/// </summary>
public sealed class AfeProvider(IHttpClientFactory httpClientFactory, ILogger<AfeProvider> logger) : ISmsProvider
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Afe = "http://www.afe.ir/";

    public string ProviderCode => "afe";

    public IReadOnlyList<ProviderField> GetRequiredFields() =>
    [
        new ProviderField("Username", "نام کاربری", IsRequired: true, IsSecret: false),
        new ProviderField("Password", "رمز عبور", IsRequired: true, IsSecret: true),
        new ProviderField("SenderNumber", "شماره فرستنده", IsRequired: true, IsSecret: false),
    ];

    public async Task<SmsSendResult> SendAsync(SmsAccountConfig config, SmsSendRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
        {
            return Failure("ERR_CREDENTIALS_MISSING", "نام کاربری یا رمز عبور پیکربندی نشده است.", isRetryable: false);
        }

        var envelope = new XElement(Soap + "Envelope",
            new XElement(Soap + "Body",
                new XElement(Afe + "SendMessage",
                    new XElement(Afe + "Username", config.Username),
                    new XElement(Afe + "Password", config.Password),
                    new XElement(Afe + "Number", config.SenderNumber),
                    new XElement(Afe + "Mobile", new XElement(Afe + "string", request.PhoneNumber)),
                    new XElement(Afe + "Message", request.Body),
                    new XElement(Afe + "Type", string.Empty))));

        try
        {
            var (root, fault) = await PostAsync(envelope, "SendMessage", request.MessageType, ct);
            if (fault is not null)
            {
                return Failure("AFE_FAULT", fault, isRetryable: false);
            }

            // SendMessageResult is an ArrayOfString, one id per Mobile entry — exactly one here.
            var messageId = root?
                .Descendants(Afe + "SendMessageResult").Elements()
                .Select(e => e.Value).FirstOrDefault();

            return new SmsSendResult(
                IsSuccess: true, ProviderMessageId: messageId, ProviderStatusCode: "ok", ErrorMessage: null,
                IsRetryable: false, Cost: null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Failure("ERR_PROVIDER_TIMEOUT", "پاسخ واید در زمان مجاز دریافت نشد.", isRetryable: true);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Afe (واید) SendMessage failed (network)");
            return Failure("ERR_PROVIDER_UNREACHABLE", "ارتباط با واید برقرار نشد.", isRetryable: true);
        }
    }

    public async Task<IReadOnlyList<SmsDeliveryStatus>> GetStatusAsync(
        SmsAccountConfig config, IReadOnlyList<string> providerMessageIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Username) || providerMessageIds.Count == 0)
        {
            return [];
        }

        var envelope = new XElement(Soap + "Envelope",
            new XElement(Soap + "Body",
                new XElement(Afe + "GetMessagesStatus",
                    new XElement(Afe + "Username", config.Username),
                    new XElement(Afe + "Password", config.Password),
                    new XElement(Afe + "SmsID", providerMessageIds.Select(id => new XElement(Afe + "string", id))))));

        try
        {
            var (root, fault) = await PostAsync(envelope, "GetMessagesStatus", SmsMessageType.DownloadLink, ct);
            if (fault is not null || root is null)
            {
                return [];
            }

            var statuses = root.Descendants(Afe + "GetMessagesStatusResult").Elements().Select(e => e.Value).ToList();

            // Assumes the result array is positionally aligned with the SmsID array we sent — the
            // proxy's shape strongly implies this (same "ArrayOfString in, ArrayOfString out" pattern
            // as SendMessage/Mobile) but it's not confirmed by a live call.
            return providerMessageIds.Zip(statuses, (id, status) => new SmsDeliveryStatus(id, MapStatus(status), status)).ToList();
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Afe (واید) GetMessagesStatus failed (network)");
            return [];
        }
    }

    public async Task<decimal?> GetCreditAsync(SmsAccountConfig config, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(config.Username))
        {
            return null;
        }

        var envelope = new XElement(Soap + "Envelope",
            new XElement(Soap + "Body",
                new XElement(Afe + "GetRemainingCredit",
                    new XElement(Afe + "Username", config.Username),
                    new XElement(Afe + "Password", config.Password))));

        try
        {
            var (root, fault) = await PostAsync(envelope, "GetRemainingCredit", SmsMessageType.DownloadLink, ct);
            if (fault is not null || root is null)
            {
                return null;
            }

            var value = root.Descendants(Afe + "GetRemainingCreditResult").FirstOrDefault()?.Value;
            return decimal.TryParse(value, out var credit) ? credit : null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Afe (واید) GetRemainingCredit failed (network)");
            return null;
        }
    }

    /// <summary>POSTs a SOAP 1.1 envelope to BoxService.asmx and returns the parsed response body
    /// root (soap:Body's first child), or a fault string if the server returned a SOAP Fault.</summary>
    private async Task<(XElement? Root, string? Fault)> PostAsync(
        XElement envelope, string operation, SmsMessageType messageType, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(SmsHttpClientNames.Afe);
        using var timeoutCts = new CancellationTokenSource(SmsProviderResilience.TimeoutFor(messageType));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        using var content = new StringContent(new XDocument(envelope).ToString(SaveOptions.DisableFormatting));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };

        // SOAPAction is a request header per the SOAP 1.1 spec, not a content header — ASMX servers
        // route on it, so it has to travel on the HttpRequestMessage itself, which needs client.SendAsync
        // rather than the simpler client.PostAsync(url, content) overload that only takes content.
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/WebService/V7/BoxService.asmx") { Content = content };
        httpRequest.Headers.Add("SOAPAction", $"\"http://www.afe.ir/{operation}\"");

        using var response = await client.SendAsync(httpRequest, linkedCts.Token);
        var body = await response.Content.ReadAsStringAsync(linkedCts.Token);

        XDocument doc;
        try
        {
            doc = XDocument.Parse(body);
        }
        catch (System.Xml.XmlException)
        {
            return (null, $"Non-XML response (HTTP {(int)response.StatusCode}): {body}");
        }

        var soapBody = doc.Root?.Element(Soap + "Body");
        var faultElement = soapBody?.Element(Soap + "Fault");
        if (faultElement is not null)
        {
            var faultString = faultElement.Element("faultstring")?.Value ?? faultElement.ToString();
            return (null, faultString);
        }

        return (soapBody?.Elements().FirstOrDefault(), null);
    }

    /// <summary>Unknown vocabulary (see class doc point 3) — only maps the substrings we can be
    /// reasonably confident about; everything else stays Sent rather than guessing Delivered/Failed.</summary>
    private static SmsStatus MapStatus(string status)
    {
        if (status.Contains("deliver", StringComparison.OrdinalIgnoreCase) || status.Contains("تحویل", StringComparison.Ordinal))
        {
            return SmsStatus.Delivered;
        }

        if (status.Contains("fail", StringComparison.OrdinalIgnoreCase) || status.Contains("error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("خطا", StringComparison.Ordinal))
        {
            return SmsStatus.Failed;
        }

        return SmsStatus.Sent;
    }

    private static SmsSendResult Failure(string code, string message, bool isRetryable) =>
        new(IsSuccess: false, ProviderMessageId: null, ProviderStatusCode: code, ErrorMessage: message, IsRetryable: isRetryable, Cost: null);
}
