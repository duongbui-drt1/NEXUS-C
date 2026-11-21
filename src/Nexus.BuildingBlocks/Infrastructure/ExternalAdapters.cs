using System.Collections.Concurrent;
using System.Security.Cryptography;
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

// === Email Provider Abstraction ===

public sealed record EmailMessage(string To, string Subject, string Body, bool IsHtml = false);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}

public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("Email dispatched via LoggingEmailSender to {Recipient} with subject '{Subject}'",
            message.To, message.Subject);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryTestEmailSender : IEmailSender
{
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
