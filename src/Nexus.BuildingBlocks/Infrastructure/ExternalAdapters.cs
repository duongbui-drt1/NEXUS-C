using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nexus.BuildingBlocks.Infrastructure;

// === Object Storage Abstraction (S3-Compatible + Local Dev/Test) ===

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    public string Provider { get; set; } = "Local"; // "S3" or "Local"
    public string BucketName { get; set; } = "nexus-files";
    public string LocalBasePath { get; set; } = "storage-data";
    public string? ServiceUrl { get; set; }
    public string Region { get; set; } = "us-east-1";
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public bool ForcePathStyle { get; set; } = true;
    public long MaxUploadSizeBytes { get; set; } = 25 * 1024 * 1024; // 25 MB
    public string SigningSecret { get; set; } = "CHANGE_ME_LOCAL_DEV_SIGNING_KEY_32CHARS";
}

public interface IObjectStorage
{
    Task PutObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream?> GetObjectAsync(string objectKey, CancellationToken ct = default);
    Task DeleteObjectAsync(string objectKey, CancellationToken ct = default);
}

public sealed class LocalFileObjectStorage(IOptions<ObjectStorageOptions> options) : IObjectStorage
{
    private readonly string _root = Path.GetFullPath(options.Value.LocalBasePath);

    public async Task PutObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default)
    {
        var fullPath = ResolveSafePath(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using var file = File.Create(fullPath);
        await content.CopyToAsync(file, ct);
    }

    public Task<Stream?> GetObjectAsync(string objectKey, CancellationToken ct = default)
    {
        var fullPath = ResolveSafePath(objectKey);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<Stream?>(null);
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
    {
        var fullPath = ResolveSafePath(objectKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string ResolveSafePath(string objectKey)
    {
        var sanitized = objectKey.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_root, sanitized));
        if (!fullPath.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Path traversal detected in object key.");
        }

        return fullPath;
    }
}

public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public S3ObjectStorage(IOptions<ObjectStorageOptions> options)
    {
        var cfg = options.Value;
        _bucket = cfg.BucketName;
        var s3Config = new AmazonS3Config
        {
            ForcePathStyle = cfg.ForcePathStyle,
            AuthenticationRegion = cfg.Region
        };

        if (!string.IsNullOrWhiteSpace(cfg.ServiceUrl))
        {
            s3Config.ServiceURL = cfg.ServiceUrl;
        }

        _client = !string.IsNullOrWhiteSpace(cfg.AccessKey) && !string.IsNullOrWhiteSpace(cfg.SecretKey)
            ? new AmazonS3Client(cfg.AccessKey, cfg.SecretKey, s3Config)
            : new AmazonS3Client(s3Config);
    }

    public async Task PutObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default)
    {
        var req = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = objectKey,
            InputStream = content,
            ContentType = contentType
        };
        await _client.PutObjectAsync(req, ct);
    }

    public async Task<Stream?> GetObjectAsync(string objectKey, CancellationToken ct = default)
    {
        try
        {
            var resp = await _client.GetObjectAsync(_bucket, objectKey, ct);
            return resp.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
    {
        await _client.DeleteObjectAsync(_bucket, objectKey, ct);
    }

    public void Dispose() => _client.Dispose();
}

// === Email Provider Abstraction (Provider-Neutral + Resend Adapter) ===

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = "Resend";
    public string DefaultFromAddress { get; set; } = "no-reply@nexus.local";
    public string DefaultFromName { get; set; } = "NEXUS Platform";
    public ResendEmailOptions Resend { get; set; } = new();
}

public sealed class ResendEmailOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.resend.com";
    public string WebhookSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
}

public sealed record EmailMessage(
    string To,
    string Subject,
    string Body,
    bool IsHtml = false,
    string? IdempotencyKey = null,
    string? FromAddress = null,
    string? FromName = null,
    string? CorrelationId = null);

public sealed record EmailDispatchResult(
    bool Succeeded,
    string Provider,
    string? ProviderMessageId,
    bool IsTransientFailure,
    int? HttpStatusCode,
    string? ErrorMessage);

public interface IEmailSender
{
    string ProviderName => "Logging";
    bool IsConfigured => true;
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
    async Task<EmailDispatchResult> SendWithResultAsync(EmailMessage message, CancellationToken ct = default)
    {
        await SendAsync(message, ct);
        return new EmailDispatchResult(true, ProviderName, $"local-{Guid.NewGuid():N}", false, 200, null);
    }
}

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public string ProviderName => "Logging";
    public bool IsConfigured => true;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("Email dispatched via LoggingEmailSender to {MaskedRecipient} with subject '{Subject}'",
            MaskEmail(message.To), message.Subject);
        return Task.CompletedTask;
    }

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "***";
        var at = email.IndexOf('@');
        if (at <= 1) return $"***{(at >= 0 ? email[at..] : string.Empty)}";
        return $"{email[0]}***{email[at..]}";
    }
}

public sealed class ResendEmailSender(
    IHttpClientFactory httpClientFactory,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    public string ProviderName => "Resend";

    private string ResolveApiKey()
        => !string.IsNullOrWhiteSpace(options.Value.Resend.ApiKey)
            ? options.Value.Resend.ApiKey.Trim()
            : (Environment.GetEnvironmentVariable("RESEND_API_KEY") ?? string.Empty).Trim();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ResolveApiKey());

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var result = await SendWithResultAsync(message, ct);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(result.ErrorMessage ?? "Resend email delivery failed.");
        }
    }

    public async Task<EmailDispatchResult> SendWithResultAsync(EmailMessage message, CancellationToken ct = default)
    {
        var apiKey = ResolveApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new EmailDispatchResult(false, ProviderName, null, false, null, "Resend API key is not configured.");
        }

        var cfg = options.Value;
        var maxRetries = Math.Clamp(cfg.Resend.MaxRetries, 1, 5);
        var fromAddr = string.IsNullOrWhiteSpace(message.FromAddress) ? cfg.DefaultFromAddress : message.FromAddress.Trim();
        var fromName = string.IsNullOrWhiteSpace(message.FromName) ? cfg.DefaultFromName : message.FromName.Trim();
        var fromFormatted = string.IsNullOrWhiteSpace(fromName) ? fromAddr : $"{fromName} <{fromAddr}>";

        var payload = new Dictionary<string, object?>
        {
            ["from"] = fromFormatted,
            ["to"] = new[] { message.To.Trim() },
            ["subject"] = message.Subject
        };
        if (message.IsHtml) payload["html"] = message.Body;
        else payload["text"] = message.Body;

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var url = $"{cfg.Resend.BaseUrl.TrimEnd('/')}/emails";
        var client = httpClientFactory.CreateClient("ResendApi");

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                if (!string.IsNullOrWhiteSpace(message.IdempotencyKey))
                {
                    req.Headers.TryAddWithoutValidation("Idempotency-Key", message.IdempotencyKey);
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(cfg.Resend.TimeoutSeconds, 2, 60)));

                using var resp = await client.SendAsync(req, timeoutCts.Token);
                var body = await resp.Content.ReadAsStringAsync(timeoutCts.Token);
                var statusCode = (int)resp.StatusCode;

                if (resp.IsSuccessStatusCode)
                {
                    string? messageId = null;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(body);
                        if (doc.RootElement.TryGetProperty("id", out var idEl))
                        {
                            messageId = idEl.GetString();
                        }
                    }
                    catch { /* non-fatal parse */ }

                    logger.LogInformation("Resend email accepted (ProviderMessageId={MessageId}, Recipient={MaskedRecipient})",
                        messageId, LoggingEmailSender.MaskEmail(message.To));
                    return new EmailDispatchResult(true, ProviderName, messageId, false, statusCode, null);
                }

                var isTransient = statusCode == 429 || statusCode >= 500;
                var sanitizedErr = $"Resend HTTP {statusCode}: {(body.Length > 300 ? body[..300] : body)}";

                if (!isTransient || attempt == maxRetries)
                {
                    return new EmailDispatchResult(false, ProviderName, null, isTransient, statusCode, sanitizedErr);
                }

                var delayMs = (int)(Math.Pow(2, attempt) * 150) + Random.Shared.Next(25, 125);
                await Task.Delay(delayMs, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt == maxRetries)
                {
                    return new EmailDispatchResult(false, ProviderName, null, true, 504, "Resend API request timed out.");
                }
                await Task.Delay(attempt * 200, ct);
            }
            catch (HttpRequestException ex)
            {
                if (attempt == maxRetries)
                {
                    return new EmailDispatchResult(false, ProviderName, null, true, null, $"Resend transport error: {ex.Message}");
                }
                await Task.Delay(attempt * 200, ct);
            }
        }

        return new EmailDispatchResult(false, ProviderName, null, true, null, "Resend delivery exhausted retries.");
    }
}

public sealed class InMemoryTestEmailSender : IEmailSender
{
    public string ProviderName => "InMemoryTest";
    public bool IsConfigured => true;
    private readonly ConcurrentQueue<EmailMessage> _sent = new();
    public IReadOnlyCollection<EmailMessage> SentMessages => _sent.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        while (_sent.TryDequeue(out _)) { }
    }
}

// === AI Gateway Provider Interface ===

public sealed record AiCompletionRequest(
    string ModelCode,
    string Prompt,
    int MaxTokens = 512,
    double Temperature = 0.2);

public sealed record AiCompletionResponse(
    string ModelCode,
    string OutputText,
    int PromptTokens,
    int CompletionTokens,
    string ProviderName);

public interface IAiProvider
{
    string ProviderName { get; }
    bool IsConfigured { get; }
    Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default);
}

/// <summary>
/// Default honest AI provider when no external provider credentials are supplied.
/// Never simulates fake AI responses in production.
/// </summary>
public sealed class UnconfiguredAiProvider : IAiProvider
{
    public string ProviderName => "Unconfigured";
    public bool IsConfigured => false;

    public Task<AiCompletionResponse> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default)
        => throw new InvalidOperationException(
            "No AI provider is configured. Supply external AI provider credentials and adapter configuration.");
}

// === Cryptographic Token Helpers ===

public static class CryptoHelper
{
    public static string GenerateSecureToken(int byteLength = 64)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string Sha256Hex(string input)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeHmacSha256(string data, string secret)
    {
        using var hmac = new HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var bytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool FixedTimeEquals(string left, string right)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(left);
        var b = System.Text.Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
