using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Infrastructure;

namespace Nexus.Modules.Integration.Application;

public sealed record WebhookDispatchRequest(
    Guid EventId,
    string EventType,
    string TargetUrl,
    string PayloadJson,
    string SigningKey,
    long TimestampUnix,
    int MaxAttempts = 3);

public sealed record WebhookDispatchOutcome(
    bool Succeeded,
    int? HttpStatusCode,
    int AttemptsMade,
    string SignatureDigestPrefix,
    string? FailureReason,
    string? ResponseSnippet,
    double DurationMs,
    bool BlockedBySsrf = false);

public interface IWebhookHttpDispatcher
{
    Task<WebhookDispatchOutcome> DispatchAsync(WebhookDispatchRequest request, CancellationToken ct = default);
}

public sealed class SafeWebhookHttpDispatcher(
    IHttpClientFactory httpClientFactory,
    ILogger<SafeWebhookHttpDispatcher> logger) : IWebhookHttpDispatcher
{
    public const string HttpClientName = "NexusOutboundWebhook";
    private const int MaxResponseReadBytes = 64 * 1024;

    public async Task<WebhookDispatchOutcome> DispatchAsync(WebhookDispatchRequest request, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var signature = WebhookSignatureHelper.ComputeSignature(request.PayloadJson, request.TimestampUnix, request.SigningKey);
        var sigPrefix = signature.Length >= 16 ? signature[..16] : signature;

        if (!Uri.TryCreate(request.TargetUrl, UriKind.Absolute, out var targetUri) || targetUri.Scheme != Uri.UriSchemeHttps)
        {
            return new WebhookDispatchOutcome(false, null, 1, sigPrefix, "WEBHOOK_INVALID_URL: Only HTTPS targets are permitted.", null, sw.Elapsed.TotalMilliseconds, BlockedBySsrf: true);
        }

        // Strict SSRF + DNS-rebinding verification BEFORE opening any network socket
        var ssrfCheck = await SsrfGuard.ValidateUriAndResolveDnsAsync(targetUri, allowedHosts: null, ct: ct);
        if (!ssrfCheck.IsSafe)
        {
            logger.LogWarning("Blocked outbound webhook delivery to {Host} due to SSRF guard: {Reason}", targetUri.Host, ssrfCheck.FailureReason);
            return new WebhookDispatchOutcome(false, null, 1, sigPrefix, $"WEBHOOK_SSRF_BLOCKED: {ssrfCheck.FailureReason}", null, sw.Elapsed.TotalMilliseconds, BlockedBySsrf: true);
        }

        var maxAttempts = Math.Clamp(request.MaxAttempts, 1, 5);
        var client = httpClientFactory.CreateClient(HttpClientName);

        int? lastStatusCode = null;
        string? lastError = null;
        string? lastSnippet = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var httpReq = new HttpRequestMessage(HttpMethod.Post, targetUri)
                {
                    Content = new StringContent(request.PayloadJson, Encoding.UTF8, "application/json")
                };

                httpReq.Headers.Add("X-Nexus-Event-Id", request.EventId.ToString("D"));
                httpReq.Headers.Add("X-Nexus-Event-Type", request.EventType);
                httpReq.Headers.Add("X-Nexus-Timestamp", request.TimestampUnix.ToString(CultureInfo.InvariantCulture));
                httpReq.Headers.Add("X-Nexus-Signature", $"t={request.TimestampUnix},v1={signature}");
                httpReq.Headers.UserAgent.ParseAdd("NEXUS-C-WebhookDispatcher/1.0");

                using var response = await client.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
                lastStatusCode = (int)response.StatusCode;
                lastSnippet = await ReadBoundedSnippetAsync(response, ct);

                // Strictly reject HTTP redirects (3xx) to prevent SSRF redirect-to-internal attacks!
                if (lastStatusCode is >= 300 and < 400)
                {
                    return new WebhookDispatchOutcome(
                        false,
                        lastStatusCode,
                        attempt,
                        sigPrefix,
                        $"WEBHOOK_REDIRECT_FORBIDDEN: Target responded with redirect HTTP {lastStatusCode}.",
                        lastSnippet,
                        sw.Elapsed.TotalMilliseconds,
                        BlockedBySsrf: true);
                }

                if (response.IsSuccessStatusCode)
                {
                    return new WebhookDispatchOutcome(true, lastStatusCode, attempt, sigPrefix, null, lastSnippet, sw.Elapsed.TotalMilliseconds);
                }

                var isTransient = response.StatusCode == HttpStatusCode.TooManyRequests || lastStatusCode >= 500;
                lastError = $"HTTP {lastStatusCode} from webhook endpoint.";

                if (!isTransient || attempt == maxAttempts)
                {
                    return new WebhookDispatchOutcome(false, lastStatusCode, attempt, sigPrefix, lastError, lastSnippet, sw.Elapsed.TotalMilliseconds);
                }
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = "WEBHOOK_TIMEOUT: Outbound delivery timed out.";
                if (attempt == maxAttempts)
                {
                    return new WebhookDispatchOutcome(false, lastStatusCode, attempt, sigPrefix, lastError, lastSnippet, sw.Elapsed.TotalMilliseconds);
                }
            }
            catch (HttpRequestException ex)
            {
                lastError = $"WEBHOOK_TRANSPORT_ERROR: {ex.Message}";
                if (attempt == maxAttempts)
                {
                    return new WebhookDispatchOutcome(false, lastStatusCode, attempt, sigPrefix, lastError, lastSnippet, sw.Elapsed.TotalMilliseconds);
                }
            }

            // Exponential backoff with jitter (150ms, 350ms, ...)
            var delayMs = (int)(150 * Math.Pow(2, attempt - 1)) + Random.Shared.Next(10, 75);
            await Task.Delay(delayMs, ct);
        }

        return new WebhookDispatchOutcome(false, lastStatusCode, maxAttempts, sigPrefix, lastError ?? "Unknown delivery error.", lastSnippet, sw.Elapsed.TotalMilliseconds);
    }

    private static async Task<string?> ReadBoundedSnippetAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[Math.Min(MaxResponseReadBytes, 1024)];
            var read = await stream.ReadAsync(buffer, ct);
            if (read <= 0) return null;

            var text = Encoding.UTF8.GetString(buffer, 0, read).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length > 512 ? text[..512] : text;
        }
        catch
        {
            return null;
        }
    }
}
