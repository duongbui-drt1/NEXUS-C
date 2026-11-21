using System.Net;
using System.Net.Sockets;
using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Integration.Domain;

public enum ConnectionStatus
{
    Pending = 1,
    Connected = 2,
    Degraded = 3,
    Disconnected = 4
}

public enum SyncStatus
{
    Running = 1,
    Succeeded = 2,
    Failed = 3
}

public sealed class ProviderDefinition : AuditableEntity
{
    private ProviderDefinition() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty; // ERP, Banking, Carrier, EInvoice, AI
    public bool IsActive { get; private set; } = true;
    public int DefaultMaxRetries { get; private set; } = 3;
    public int DefaultTimeoutSeconds { get; private set; } = 30;

    public static Result<ProviderDefinition> Create(string code, string name, string category, int maxRetries = 3, int timeoutSeconds = 30)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("INTEGRATION_INVALID_PROVIDER", "Provider code and name are required.");
        }

        return new ProviderDefinition
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
            IsActive = true,
            DefaultMaxRetries = Math.Clamp(maxRetries, 0, 10),
            DefaultTimeoutSeconds = Math.Clamp(timeoutSeconds, 5, 120)
        };
    }
}

public sealed class IntegrationConnection : TenantScopedEntity
{
    private IntegrationConnection() { }

    public Guid ProviderId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string CredentialReference { get; private set; } = string.Empty; // e.g., vault://nexus/tenant-a/provider-x
    public ConnectionStatus Status { get; private set; } = ConnectionStatus.Pending;
    public int MaxRetries { get; private set; } = 3;
    public int TimeoutSeconds { get; private set; } = 30;
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public static Result<IntegrationConnection> Create(Guid tenantId, Guid providerId, string name, string credentialReference, int maxRetries = 3, int timeoutSeconds = 30)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("INTEGRATION_INVALID_NAME", "Connection name is required.");
        }

        if (string.IsNullOrWhiteSpace(credentialReference) ||
            (!credentialReference.StartsWith("vault://", StringComparison.OrdinalIgnoreCase) &&
             !credentialReference.StartsWith("env://", StringComparison.OrdinalIgnoreCase) &&
             !credentialReference.StartsWith("kms://", StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Validation(
                "INTEGRATION_INVALID_CREDENTIAL_REF",
                "CredentialReference must be an external secret reference URI (vault://, env://, or kms://). Raw credentials are forbidden.");
        }

        var conn = new IntegrationConnection
        {
            Id = Guid.CreateVersion7(),
            ProviderId = providerId,
            Name = name.Trim(),
            CredentialReference = credentialReference.Trim(),
            Status = ConnectionStatus.Connected,
            MaxRetries = Math.Clamp(maxRetries, 0, 10),
            TimeoutSeconds = Math.Clamp(timeoutSeconds, 5, 120)
        };
        conn.AssignTenant(tenantId);
        return conn;
    }

    public void UpdateStatus(ConnectionStatus status, DateTimeOffset? syncedAt = null)
    {
        Status = status;
        if (syncedAt.HasValue) LastSyncedAt = syncedAt.Value;
    }
}

public sealed class SyncLog : TenantScopedEntity
{
    private SyncLog() { }

    public Guid ConnectionId { get; private set; }
    public string Direction { get; private set; } = "Outbound";
    public SyncStatus Status { get; private set; }
    public int RecordsProcessed { get; private set; }
    public string? Summary { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static SyncLog Record(Guid tenantId, Guid connectionId, string direction, SyncStatus status, int recordsProcessed, string? summary, DateTimeOffset utcNow)
    {
        var log = new SyncLog
        {
            Id = Guid.CreateVersion7(),
            ConnectionId = connectionId,
            Direction = direction,
            Status = status,
            RecordsProcessed = recordsProcessed,
            Summary = summary,
            StartedAt = utcNow,
            CompletedAt = status == SyncStatus.Running ? null : utcNow
        };
        log.AssignTenant(tenantId);
        return log;
    }
}

public sealed class IntegrationError : TenantScopedEntity
{
    private IntegrationError() { }

    public Guid ConnectionId { get; private set; }
    public string ErrorCode { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public int AttemptNumber { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static IntegrationError Record(Guid tenantId, Guid connectionId, string errorCode, string message, int attempt, DateTimeOffset utcNow)
    {
        var err = new IntegrationError
        {
            Id = Guid.CreateVersion7(),
            ConnectionId = connectionId,
            ErrorCode = errorCode,
            Message = message.Length > 1500 ? message[..1500] : message,
            AttemptNumber = attempt,
            OccurredAt = utcNow
        };
        err.AssignTenant(tenantId);
        return err;
    }
}

public sealed class WebhookRegistration : TenantScopedEntity
{
    private WebhookRegistration() { }

    public string EventType { get; private set; } = string.Empty;
    public string TargetUrl { get; private set; } = string.Empty;
    public string SigningSecretRef { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    public static Result<WebhookRegistration> Create(Guid tenantId, string eventType, string targetUrl, string signingSecretRef)
    {
        if (string.IsNullOrWhiteSpace(eventType) || !Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return Error.Validation("WEBHOOK_INVALID_URL", "Webhook TargetUrl must be a valid HTTPS URL.");
        }

        if (IsUnsafeSsrfTarget(uri))
        {
            return Error.Validation(
                "WEBHOOK_UNSAFE_TARGET",
                "Webhook TargetUrl cannot target localhost, loopback, link-local, metadata, or private internal network addresses.");
        }

        if (string.IsNullOrWhiteSpace(signingSecretRef) ||
            (!signingSecretRef.StartsWith("vault://", StringComparison.OrdinalIgnoreCase) &&
             !signingSecretRef.StartsWith("env://", StringComparison.OrdinalIgnoreCase)))
        {
            return Error.Validation("WEBHOOK_INVALID_SECRET_REF", "SigningSecretRef must be a vault:// or env:// reference.");
        }

        var wh = new WebhookRegistration
        {
            Id = Guid.CreateVersion7(),
            EventType = eventType.Trim(),
            TargetUrl = targetUrl.Trim(),
            SigningSecretRef = signingSecretRef.Trim(),
            IsActive = true
        };
        wh.AssignTenant(tenantId);
        return wh;
    }

    private static bool IsUnsafeSsrfTarget(Uri uri)
    {
        var host = uri.DnsSafeHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host) ||
            host is "localhost" or "ip6-localhost" or "metadata.google.internal" ||
            host.EndsWith(".localhost", StringComparison.Ordinal) ||
            host.EndsWith(".local", StringComparison.Ordinal) ||
            host.EndsWith(".internal", StringComparison.Ordinal))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            {
                return true;
            }

            var bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
            {
                // 0.0.0.0/8, 10.0.0.0/8, 100.64.0.0/10 (CGNAT), 127.0.0.0/8, 169.254.0.0/16 (link-local/cloud metadata),
                // 172.16.0.0/12, 192.168.0.0/16
                return bytes[0] == 0
                    || bytes[0] == 10
                    || bytes[0] == 127
                    || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                    || (bytes[0] == 169 && bytes[1] == 254)
                    || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                    || (bytes[0] == 192 && bytes[1] == 168);
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
            {
                // Link-local fe80::/10 or Unique Local fc00::/7
                var isLinkLocal = bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80;
                var isUniqueLocal = (bytes[0] & 0xFE) == 0xFC;
                return isLinkLocal || isUniqueLocal;
            }
        }

        return false;
    }
}

public sealed class AiModelConfiguration : AuditableEntity
{
    private AiModelConfiguration() { }

    public string ModelCode { get; private set; } = string.Empty;
    public string ProviderName { get; private set; } = string.Empty;
    public int TimeoutSeconds { get; private set; } = 30;
    public int MaxTokensLimit { get; private set; } = 2048;
    public bool IsEnabled { get; private set; } = true;

    public static AiModelConfiguration Create(string modelCode, string providerName, int timeoutSeconds = 30, int maxTokensLimit = 2048)
        => new()
        {
            Id = Guid.CreateVersion7(),
            ModelCode = modelCode.Trim(),
            ProviderName = providerName.Trim(),
            TimeoutSeconds = Math.Clamp(timeoutSeconds, 5, 120),
            MaxTokensLimit = Math.Clamp(maxTokensLimit, 64, 32768),
            IsEnabled = true
        };
}

public sealed class AiUsageLog : TenantScopedEntity
{
    private AiUsageLog() { }

    public Guid? UserId { get; private set; }
    public string ModelCode { get; private set; } = string.Empty;
    public string ProviderName { get; private set; } = string.Empty;
    public int PromptTokens { get; private set; }
    public int CompletionTokens { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string? SanitizedRequestMetaJson { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static AiUsageLog Record(Guid tenantId, Guid? userId, string modelCode, string providerName, int promptTokens, int completionTokens, string status, string? sanitizedMeta, DateTimeOffset utcNow)
    {
        var log = new AiUsageLog
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            ModelCode = modelCode,
            ProviderName = providerName,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            Status = status,
            SanitizedRequestMetaJson = sanitizedMeta,
            OccurredAt = utcNow
        };
        log.AssignTenant(tenantId);
        return log;
    }
}
