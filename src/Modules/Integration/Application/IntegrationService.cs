using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.BuildingBlocks.Observability;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Integration.Domain;
using Nexus.Modules.Integration.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Integration.Application;

public sealed record CreateProviderRequest(
    string Code,
    string Name,
    string Category,
    int MaxRetries = 3,
    int TimeoutSeconds = 30,
    string? Version = null,
    string? AllowedHosts = null,
    string? ConfigSchemaJson = null,
    bool RequiresSecretRef = true);

public sealed record ProviderDto(
    Guid Id,
    string Code,
    string Name,
    string Category,
    bool IsActive,
    int DefaultMaxRetries,
    int DefaultTimeoutSeconds,
    string Version = "1.0",
    string? AllowedHosts = null,
    bool RequiresSecretRef = true);

public sealed record CreateConnectionRequest(
    Guid ProviderId,
    string Name,
    string CredentialReference,
    int MaxRetries = 3,
    int TimeoutSeconds = 30,
    string? ApplicationCode = null,
    Dictionary<string, object>? NonSecretConfig = null);

public sealed record RotateConnectionCredentialRefRequest(string CredentialReference);

public sealed record ConnectionTestResultDto(
    Guid ConnectionId,
    string HealthStatus,
    bool Passed,
    string Summary,
    DateTimeOffset CheckedAt);

public sealed record ConnectionDto(
    Guid Id,
    Guid TenantId,
    Guid ProviderId,
    string Name,
    string CredentialReference,
    string Status,
    int MaxRetries,
    int TimeoutSeconds,
    DateTimeOffset? LastSyncedAt,
    string? ApplicationCode = null,
    string HealthStatus = "Unknown",
    DateTimeOffset? LastHealthCheckAt = null,
    DateTimeOffset? DisabledAt = null,
    string? NonSecretConfigJson = null);

public sealed record RecordSyncRequest(string Direction, SyncStatus Status, int RecordsProcessed, string? Summary, string? ErrorCode = null, string? ErrorMessage = null);
public sealed record SyncLogDto(Guid Id, Guid ConnectionId, string Direction, string Status, int RecordsProcessed, string? Summary, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);
public sealed record IntegrationErrorDto(Guid Id, Guid TenantId, Guid ConnectionId, string ErrorCode, string Message, int AttemptNumber, DateTimeOffset OccurredAt);

public sealed record CreateWebhookRequest(string EventType, string TargetUrl, string SigningSecretRef, string? Description = null, string? ApplicationCode = null);
public sealed record UpdateWebhookRequest(string EventType, string TargetUrl, string? Description = null);
public sealed record DispatchWebhookRequest(Guid? EventId, string PayloadJson, int MaxAttempts = 3);
public sealed record WebhookDto(
    Guid Id,
    Guid TenantId,
    string EventType,
    string TargetUrl,
    bool IsActive,
    string? Description = null,
    int SecretVersion = 1,
    string? SecretPrefix = null,
    DateTimeOffset? SecretRotatedAt = null,
    string? ApplicationCode = null);

public sealed record WebhookSecretRotationDto(WebhookDto Webhook, string SigningSecret);

public sealed record RecordWebhookDeliveryRequest(
    Guid EventId,
    string PayloadJson,
    WebhookDeliveryStatus Status = WebhookDeliveryStatus.Succeeded,
    int? HttpStatusCode = 200,
    string? FailureReason = null);

public sealed record WebhookDeliveryDto(
    Guid Id,
    Guid TenantId,
    Guid WebhookId,
    Guid EventId,
    string EventType,
    string Status,
    int AttemptCount,
    int? HttpStatusCode,
    long TimestampUnix,
    string? SignatureDigestPrefix,
    string? FailureReason,
    DateTimeOffset AttemptedAt,
    DateTimeOffset? CompletedAt,
    string? ResponseSnippet = null,
    double? DurationMs = null);

public sealed record RegisterAiModelRequest(string ModelCode, string ProviderName, int TimeoutSeconds = 30, int MaxTokensLimit = 2048);
public sealed record AiModelDto(Guid Id, string ModelCode, string ProviderName, int TimeoutSeconds, int MaxTokensLimit, bool IsEnabled);
public sealed record InvokeAiRequest(string ModelCode, string Prompt, int MaxTokens = 256, Dictionary<string, object>? Metadata = null);

public interface IIntegrationService
{
    Task SeedCanonicalProvidersAsync(CancellationToken ct = default);
    Task<Result<ProviderDto>> RegisterProviderAsync(CreateProviderRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ProviderDto>>> ListProvidersAsync(CancellationToken ct = default);
    Task<Result<ConnectionDto>> CreateConnectionAsync(CreateConnectionRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ConnectionDto>>> ListConnectionsAsync(CancellationToken ct = default);
    Task<Result<ConnectionTestResultDto>> TestConnectionAsync(Guid connectionId, CancellationToken ct = default);
    Task<Result<ConnectionDto>> SetConnectionActiveAsync(Guid connectionId, bool isActive, CancellationToken ct = default);
    Task<Result<ConnectionDto>> RotateConnectionCredentialRefAsync(Guid connectionId, RotateConnectionCredentialRefRequest request, CancellationToken ct = default);
    Task<Result> DeleteConnectionAsync(Guid connectionId, CancellationToken ct = default);
    Task<Result<SyncLogDto>> RecordSyncAsync(Guid connectionId, RecordSyncRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SyncLogDto>>> ListSyncLogsAsync(Guid connectionId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<IntegrationErrorDto>>> ListErrorsAsync(CancellationToken ct = default);

    Task<Result<WebhookDto>> RegisterWebhookAsync(CreateWebhookRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<WebhookDto>>> ListWebhooksAsync(CancellationToken ct = default);
    Task<Result<WebhookDto>> UpdateWebhookAsync(Guid webhookId, UpdateWebhookRequest request, CancellationToken ct = default);
    Task<Result<WebhookDto>> SetWebhookActiveAsync(Guid webhookId, bool isActive, CancellationToken ct = default);
    Task<Result> DeleteWebhookAsync(Guid webhookId, CancellationToken ct = default);
    Task<Result<WebhookSecretRotationDto>> RotateWebhookSecretAsync(Guid webhookId, CancellationToken ct = default);
    Task<Result<WebhookDeliveryDto>> DispatchWebhookAsync(Guid webhookId, DispatchWebhookRequest request, CancellationToken ct = default);
    Task<Result<WebhookDeliveryDto>> RecordWebhookDeliveryAsync(Guid webhookId, RecordWebhookDeliveryRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<WebhookDeliveryDto>>> ListWebhookDeliveriesAsync(Guid webhookId, CancellationToken ct = default);
    Task<Result<WebhookDeliveryDto>> RetryWebhookDeliveryAsync(Guid deliveryId, CancellationToken ct = default);

    Task<Result<AiModelDto>> RegisterAiModelAsync(RegisterAiModelRequest request, CancellationToken ct = default);
    Task<Result<AiCompletionResponse>> InvokeAiGatewayAsync(InvokeAiRequest request, CancellationToken ct = default);
}

public sealed class IntegrationService(
    IntegrationDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock,
    IEntitlementChecker entitlementChecker,
    IAiProvider aiProvider,
    IWebhookHttpDispatcher webhookDispatcher,
    IAuditWriter auditWriter) : IIntegrationService
{
    public async Task SeedCanonicalProvidersAsync(CancellationToken ct = default)
    {
        var existingCodes = await db.Providers.AsNoTracking()
            .Select(p => p.Code)
            .ToListAsync(ct);
        var set = existingCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        (string Code, string Name, string Category, string AllowedHosts, bool RequiresSecret)[] canonical =
        [
            ("RESEND", "Resend Transactional Email", "Email", "api.resend.com", true),
            ("CLERK", "Clerk Authentication & Identity", "Identity", "api.clerk.com", true),
            ("S3_STORAGE", "S3-Compatible Object Storage", "Storage", "", true),
            ("RABBITMQ", "RabbitMQ Message Broker", "Messaging", "", true),
            ("FX_FRANKFURTER", "Frankfurter ECB Reference FX", "ReferenceData", "api.frankfurter.app", false)
        ];

        var added = false;
        foreach (var item in canonical)
        {
            if (set.Contains(item.Code)) continue;
            var res = ProviderDefinition.Create(item.Code, item.Name, item.Category, 3, 15, "1.0", item.AllowedHosts, null, item.RequiresSecret);
            if (res.IsSuccess)
            {
                db.Providers.Add(res.Value);
                added = true;
            }
        }

        if (added)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<Result<ProviderDto>> RegisterProviderAsync(CreateProviderRequest request, CancellationToken ct = default)
    {
        var created = ProviderDefinition.Create(
            request.Code,
            request.Name,
            request.Category,
            request.MaxRetries,
            request.TimeoutSeconds,
            request.Version,
            request.AllowedHosts,
            request.ConfigSchemaJson,
            request.RequiresSecretRef);
        if (created.IsFailure) return Result.Failure<ProviderDto>(created.Error);

        var provider = created.Value;
        if (await db.Providers.AsNoTracking().AnyAsync(p => p.Code == provider.Code, ct))
        {
            return Error.Conflict("PROVIDER_DUPLICATE", $"Provider '{provider.Code}' already exists.");
        }

        db.Providers.Add(provider);
        await db.SaveChangesAsync(ct);

        return MapProvider(provider);
    }

    public async Task<Result<IReadOnlyList<ProviderDto>>> ListProvidersAsync(CancellationToken ct = default)
    {
        var list = await db.Providers.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => MapProvider(p))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<ConnectionDto>> CreateConnectionAsync(CreateConnectionRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        if (!await db.Providers.AsNoTracking().AnyAsync(p => p.Id == request.ProviderId && p.IsActive, ct))
        {
            return Error.NotFound("PROVIDER_NOT_FOUND", "Integration provider not found.");
        }

        // Guard against plaintext secrets sneaking into NonSecretConfig
        if (request.NonSecretConfig is not null)
        {
            foreach (var key in request.NonSecretConfig.Keys)
            {
                if (SensitiveDataRedactor.IsSensitiveKey(key))
                {
                    return Error.Validation(
                        "CONNECTION_SECRET_IN_CONFIG_FORBIDDEN",
                        $"Sensitive field '{key}' cannot be stored in NonSecretConfig. Use CredentialReference (vault://, env://, or kms://).");
                }
            }
        }

        var configJson = request.NonSecretConfig is null ? null : SensitiveDataRedactor.SanitizeToJson(request.NonSecretConfig);
        var created = IntegrationConnection.Create(
            tenantId,
            request.ProviderId,
            request.Name,
            request.CredentialReference,
            request.MaxRetries,
            request.TimeoutSeconds,
            request.ApplicationCode,
            configJson);
        if (created.IsFailure) return Result.Failure<ConnectionDto>(created.Error);

        var conn = created.Value;
        db.Connections.Add(conn);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "integration.connection.create", "IntegrationConnection", conn.Id.ToString("D"), tenantId, "Success",
            AfterState: new { conn.Name, conn.ProviderId, conn.ApplicationCode, Status = conn.Status.ToString() }), ct);

        return MapConnection(conn);
    }

    public async Task<Result<IReadOnlyList<ConnectionDto>>> ListConnectionsAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var list = await db.Connections.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => MapConnection(c))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<ConnectionTestResultDto>> TestConnectionAsync(Guid connectionId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var conn = await db.Connections.FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (conn is null) return Error.NotFound("CONNECTION_NOT_FOUND", "Integration connection not found.");

        var provider = await db.Providers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == conn.ProviderId, ct);
        var now = clock.UtcNow;

        if (conn.Status == ConnectionStatus.Disconnected)
        {
            return new ConnectionTestResultDto(conn.Id, "Disabled", false, "Connection is disabled. Reconnect before testing.", now);
        }

        var passed = IntegrationConnection.IsValidSecretReference(conn.CredentialReference);
        var summary = passed
            ? $"Credential reference validated ({conn.CredentialReference.Split("://")[0]}://) for provider '{provider?.Code ?? "UNKNOWN"}'."
            : "Invalid credential reference format.";

        // If NonSecretConfig contains an endpointUrl, verify SSRF + provider AllowedHosts!
        if (passed && !string.IsNullOrWhiteSpace(conn.NonSecretConfigJson))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(conn.NonSecretConfigJson);
                if (doc.RootElement.TryGetProperty("endpointUrl", out var urlProp) && urlProp.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var rawUrl = urlProp.GetString();
                    if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
                    {
                        passed = false;
                        summary = "Configured endpointUrl is not a valid absolute URI.";
                    }
                    else
                    {
                        var allowed = string.IsNullOrWhiteSpace(provider?.AllowedHosts)
                            ? null
                            : provider.AllowedHosts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                        if (SsrfGuard.IsUnsafeTarget(uri))
                        {
                            passed = false;
                            summary = "Configured endpointUrl blocked by SSRF protection (internal/private target).";
                        }
                        else if (allowed is { Length: > 0 } && !allowed.Any(h => string.Equals(h, uri.DnsSafeHost, StringComparison.OrdinalIgnoreCase)))
                        {
                            passed = false;
                            summary = $"Host '{uri.DnsSafeHost}' is not in provider AllowedHosts ({provider!.AllowedHosts}).";
                        }
                    }
                }
            }
            catch
            {
                passed = false;
                summary = "NonSecretConfigJson could not be parsed.";
            }
        }

        var health = passed ? "Healthy" : "Degraded";
        conn.RecordHealthCheck(health, now);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "integration.connection.test", "IntegrationConnection", conn.Id.ToString("D"), tenantId, passed ? "Success" : "Failed",
            Reason: summary,
            AfterState: new { conn.HealthStatus, conn.LastHealthCheckAt }), ct);

        return new ConnectionTestResultDto(conn.Id, health, passed, summary, now);
    }

    public async Task<Result<ConnectionDto>> SetConnectionActiveAsync(Guid connectionId, bool isActive, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var conn = await db.Connections.FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (conn is null) return Error.NotFound("CONNECTION_NOT_FOUND", "Integration connection not found.");

        if (isActive) conn.Reconnect();
        else conn.Disable(clock.UtcNow);

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            isActive ? "integration.connection.reconnect" : "integration.connection.disable",
            "IntegrationConnection", conn.Id.ToString("D"), tenantId, "Success",
            AfterState: new { Status = conn.Status.ToString(), conn.HealthStatus, conn.DisabledAt }), ct);

        return MapConnection(conn);
    }

    public async Task<Result<ConnectionDto>> RotateConnectionCredentialRefAsync(Guid connectionId, RotateConnectionCredentialRefRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var conn = await db.Connections.FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (conn is null) return Error.NotFound("CONNECTION_NOT_FOUND", "Integration connection not found.");

        var prevRef = conn.CredentialReference;
        var res = conn.RotateCredentialReference(request.CredentialReference);
        if (res.IsFailure) return Result.Failure<ConnectionDto>(res.Error);

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "integration.connection.rotate_credential_ref", "IntegrationConnection", conn.Id.ToString("D"), tenantId, "Success",
            BeforeState: new { CredentialReference = prevRef },
            AfterState: new { conn.CredentialReference }), ct);

        return MapConnection(conn);
    }

    public async Task<Result> DeleteConnectionAsync(Guid connectionId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Result.Failure(Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required."));
        var tenantId = tenantContext.TenantId!.Value;

        var conn = await db.Connections.FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (conn is null) return Result.Failure(Error.NotFound("CONNECTION_NOT_FOUND", "Integration connection not found."));

        var hasRunningSync = await db.SyncLogs.AsNoTracking().AnyAsync(s => s.ConnectionId == connectionId && s.Status == SyncStatus.Running, ct);
        if (hasRunningSync)
        {
            return Result.Failure(Error.BusinessRule("CONNECTION_IN_USE", "Cannot delete a connection while a sync operation is currently running."));
        }

        // Remove dependent logs/errors cleanly or disable if historical audit logs exist
        var hasHistory = await db.SyncLogs.AsNoTracking().AnyAsync(s => s.ConnectionId == connectionId, ct)
                         || await db.Errors.AsNoTracking().AnyAsync(e => e.ConnectionId == connectionId, ct);
        if (hasHistory)
        {
            conn.Disable(clock.UtcNow);
            await db.SaveChangesAsync(ct);

            await auditWriter.WriteAsync(new AuditEntryRequest(
                "integration.connection.archive_on_delete", "IntegrationConnection", conn.Id.ToString("D"), tenantId, "Success",
                Reason: "Connection has historical sync/error logs; disabled instead of hard-deleted to preserve history."), ct);
            return Result.Success();
        }

        db.Connections.Remove(conn);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "integration.connection.delete", "IntegrationConnection", connectionId.ToString("D"), tenantId, "Success",
            BeforeState: new { conn.Name, conn.ProviderId }), ct);

        return Result.Success();
    }

    public async Task<Result<SyncLogDto>> RecordSyncAsync(Guid connectionId, RecordSyncRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var conn = await db.Connections.FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (conn is null) return Error.NotFound("CONNECTION_NOT_FOUND", "Integration connection not found.");

        var now = clock.UtcNow;
        var log = SyncLog.Record(tenantId, conn.Id, request.Direction, request.Status, request.RecordsProcessed, request.Summary, now);
        db.SyncLogs.Add(log);

        if (request.Status == SyncStatus.Failed)
        {
            conn.UpdateStatus(ConnectionStatus.Degraded);
            db.Errors.Add(IntegrationError.Record(tenantId, conn.Id, request.ErrorCode ?? "SYNC_FAILED", request.ErrorMessage ?? "Sync failed", 1, now));
        }
        else if (request.Status == SyncStatus.Succeeded)
        {
            conn.UpdateStatus(ConnectionStatus.Connected, now);
        }

        await db.SaveChangesAsync(ct);
        return new SyncLogDto(log.Id, log.ConnectionId, log.Direction, log.Status.ToString(), log.RecordsProcessed, log.Summary, log.StartedAt, log.CompletedAt);
    }

    public async Task<Result<IReadOnlyList<SyncLogDto>>> ListSyncLogsAsync(Guid connectionId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var list = await db.SyncLogs.AsNoTracking()
            .Where(s => s.ConnectionId == connectionId)
            .OrderByDescending(s => s.StartedAt)
            .Take(100)
            .Select(s => new SyncLogDto(s.Id, s.ConnectionId, s.Direction, s.Status.ToString(), s.RecordsProcessed, s.Summary, s.StartedAt, s.CompletedAt))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<IReadOnlyList<IntegrationErrorDto>>> ListErrorsAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var list = await db.Errors.AsNoTracking()
            .OrderByDescending(e => e.OccurredAt)
            .Take(100)
            .Select(e => new IntegrationErrorDto(e.Id, e.TenantId, e.ConnectionId, e.ErrorCode, e.Message, e.AttemptNumber, e.OccurredAt))
            .ToListAsync(ct);

        return list;
    }

    // === Complete Webhook Lifecycle ===

    public async Task<Result<WebhookDto>> RegisterWebhookAsync(CreateWebhookRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var created = WebhookRegistration.Create(tenantId, request.EventType, request.TargetUrl, request.SigningSecretRef, request.Description, applicationCode: request.ApplicationCode);
        if (created.IsFailure) return Result.Failure<WebhookDto>(created.Error);

        var wh = created.Value;
        db.Webhooks.Add(wh);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "integration.webhook.create", "WebhookRegistration", wh.Id.ToString("D"), tenantId, "Success",
            AfterState: new { wh.EventType, wh.TargetUrl, wh.IsActive }), ct);

        return MapWebhook(wh);
    }

    public async Task<Result<IReadOnlyList<WebhookDto>>> ListWebhooksAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var list = await db.Webhooks.AsNoTracking()
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => MapWebhook(w))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<WebhookDto>> UpdateWebhookAsync(Guid webhookId, UpdateWebhookRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var wh = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == webhookId, ct);
        if (wh is null) return Error.NotFound("WEBHOOK_NOT_FOUND", "Webhook registration was not found.");

        var before = MapWebhook(wh);
        var res = wh.Update(request.EventType, request.TargetUrl, request.Description);
        if (res.IsFailure) return Result.Failure<WebhookDto>(res.Error);

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "integration.webhook.update", "WebhookRegistration", wh.Id.ToString("D"), tenantId, "Success",
            BeforeState: before, AfterState: MapWebhook(wh)), ct);

        return MapWebhook(wh);
    }

    public async Task<Result<WebhookDto>> SetWebhookActiveAsync(Guid webhookId, bool isActive, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var wh = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == webhookId, ct);
        if (wh is null) return Error.NotFound("WEBHOOK_NOT_FOUND", "Webhook registration was not found.");

        wh.SetActive(isActive);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            isActive ? "integration.webhook.enable" : "integration.webhook.disable",
            "WebhookRegistration", wh.Id.ToString("D"), tenantId, "Success"), ct);

        return MapWebhook(wh);
    }

    public async Task<Result> DeleteWebhookAsync(Guid webhookId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Result.Failure(Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required."));
        var tenantId = tenantContext.TenantId!.Value;

        var wh = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == webhookId, ct);
        if (wh is null) return Result.Failure(Error.NotFound("WEBHOOK_NOT_FOUND", "Webhook registration was not found."));

        db.Webhooks.Remove(wh);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "integration.webhook.delete", "WebhookRegistration", webhookId.ToString("D"), tenantId, "Success"), ct);

        return Result.Success();
    }

    public async Task<Result<WebhookSecretRotationDto>> RotateWebhookSecretAsync(Guid webhookId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var wh = await db.Webhooks.FirstOrDefaultAsync(w => w.Id == webhookId, ct);
        if (wh is null) return Error.NotFound("WEBHOOK_NOT_FOUND", "Webhook registration was not found.");

        // Generate a fresh high-entropy signing secret, return it ONCE, and persist ONLY its SHA-256 hash + prefix
        var rawSecret = $"whsec_{CryptoHelper.GenerateSecureToken(32)}";
        var hash = CryptoHelper.Sha256Hex(rawSecret);
        var prefix = rawSecret[..12];

        wh.RotateSecret(hash, prefix, clock.UtcNow, $"managed://webhooks/{wh.Id:D}/v{wh.SecretVersion + 1}");
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "integration.webhook.rotate_secret", "WebhookRegistration", wh.Id.ToString("D"), tenantId, "Success",
            AfterState: new { wh.SecretVersion, wh.SecretPrefix, wh.SecretRotatedAt }), ct);

        return new WebhookSecretRotationDto(MapWebhook(wh), rawSecret);
    }

    public async Task<Result<WebhookDeliveryDto>> DispatchWebhookAsync(Guid webhookId, DispatchWebhookRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var wh = await db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == webhookId, ct);
        if (wh is null) return Error.NotFound("WEBHOOK_NOT_FOUND", "Webhook registration was not found.");
        if (!wh.IsActive) return Error.BusinessRule("WEBHOOK_DISABLED", "Cannot dispatch events to a disabled webhook.");

        var now = clock.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var eventId = request.EventId ?? Guid.CreateVersion7();
        var payloadJson = string.IsNullOrWhiteSpace(request.PayloadJson) ? "{}" : request.PayloadJson;
        var signingKey = wh.SigningSecretHash ?? wh.SigningSecretRef;

        var outcome = await webhookDispatcher.DispatchAsync(
            new WebhookDispatchRequest(eventId, wh.EventType, wh.TargetUrl, payloadJson, signingKey, ts, request.MaxAttempts),
            ct);

        var status = outcome.Succeeded ? WebhookDeliveryStatus.Succeeded : WebhookDeliveryStatus.Failed;
        var delivery = WebhookDelivery.Record(
            tenantId,
            wh.Id,
            eventId,
            wh.EventType,
            status,
            outcome.HttpStatusCode,
            ts,
            outcome.SignatureDigestPrefix,
            outcome.FailureReason,
            now,
            outcome.AttemptsMade,
            outcome.ResponseSnippet,
            outcome.DurationMs);

        db.WebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        NexusPlatformMetrics.WebhookDeliveries.Add(1, new KeyValuePair<string, object?>("status", delivery.Status.ToString().ToLowerInvariant()));

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "integration.webhook.dispatch",
            "WebhookDelivery",
            delivery.Id.ToString("D"),
            tenantId,
            outcome.Succeeded ? "Success" : "Failed",
            Reason: outcome.FailureReason,
            AfterState: new { wh.Id, eventId, outcome.HttpStatusCode, outcome.AttemptsMade, outcome.BlockedBySsrf }), ct);

        return MapDelivery(delivery);
    }

    public async Task<Result<WebhookDeliveryDto>> RecordWebhookDeliveryAsync(Guid webhookId, RecordWebhookDeliveryRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var wh = await db.Webhooks.AsNoTracking().FirstOrDefaultAsync(w => w.Id == webhookId, ct);
        if (wh is null) return Error.NotFound("WEBHOOK_NOT_FOUND", "Webhook registration was not found.");
        if (!wh.IsActive) return Error.BusinessRule("WEBHOOK_DISABLED", "Cannot deliver events to a disabled webhook.");

        var now = clock.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var sigPrefix = WebhookSignatureHelper.ComputeSignature(request.PayloadJson ?? "{}", ts, wh.SigningSecretHash ?? wh.SigningSecretRef)[..16];

        var delivery = WebhookDelivery.Record(
            tenantId, wh.Id, request.EventId, wh.EventType,
            request.Status, request.HttpStatusCode, ts, sigPrefix, request.FailureReason, now);

        db.WebhookDeliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        NexusPlatformMetrics.WebhookDeliveries.Add(1, new KeyValuePair<string, object?>("status", delivery.Status.ToString().ToLowerInvariant()));

        return MapDelivery(delivery);
    }

    public async Task<Result<IReadOnlyList<WebhookDeliveryDto>>> ListWebhookDeliveriesAsync(Guid webhookId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var exists = await db.Webhooks.AsNoTracking().AnyAsync(w => w.Id == webhookId, ct);
        if (!exists) return Error.NotFound("WEBHOOK_NOT_FOUND", "Webhook registration was not found.");

        var list = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.WebhookId == webhookId)
            .OrderByDescending(d => d.AttemptedAt)
            .Take(100)
            .Select(d => MapDelivery(d))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<WebhookDeliveryDto>> RetryWebhookDeliveryAsync(Guid deliveryId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var delivery = await db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, ct);
        if (delivery is null) return Error.NotFound("WEBHOOK_DELIVERY_NOT_FOUND", "Webhook delivery record was not found.");

        if (delivery.AttemptCount >= 5)
        {
            return Error.BusinessRule("WEBHOOK_RETRY_LIMIT_EXCEEDED", "Maximum webhook retry attempts (5) reached.");
        }

        var now = clock.UtcNow;
        delivery.RecordRetryAttempt(WebhookDeliveryStatus.Pending, null, now.ToUnixTimeSeconds(), null, now);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "integration.webhook.delivery_retry", "WebhookDelivery", delivery.Id.ToString("D"), tenantId, "Success",
            AfterState: new { delivery.WebhookId, delivery.EventId, delivery.AttemptCount }), ct);

        return MapDelivery(delivery);
    }

    // === AI Gateway ===

    public async Task<Result<AiModelDto>> RegisterAiModelAsync(RegisterAiModelRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.ModelCode) || string.IsNullOrWhiteSpace(request.ProviderName))
        {
            return Error.Validation("AI_MODEL_INVALID", "ModelCode and ProviderName are required.");
        }

        var cfg = AiModelConfiguration.Create(request.ModelCode, request.ProviderName, request.TimeoutSeconds, request.MaxTokensLimit);
        db.AiModels.Add(cfg);
        await db.SaveChangesAsync(ct);

        return new AiModelDto(cfg.Id, cfg.ModelCode, cfg.ProviderName, cfg.TimeoutSeconds, cfg.MaxTokensLimit, cfg.IsEnabled);
    }

    public async Task<Result<AiCompletionResponse>> InvokeAiGatewayAsync(InvokeAiRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var entitled = await entitlementChecker.HasFeatureAsync(tenantId, "AI_GATEWAY", ct);
        if (!entitled)
        {
            return Error.Forbidden("AI_ENTITLEMENT_REQUIRED", "Tenant subscription does not include active AI_GATEWAY entitlement.");
        }

        var model = await db.AiModels.AsNoTracking().FirstOrDefaultAsync(m => m.ModelCode == request.ModelCode && m.IsEnabled, ct);
        if (model is null)
        {
            return Error.NotFound("AI_MODEL_NOT_FOUND", $"AI model '{request.ModelCode}' is not registered or enabled.");
        }

        var sanitizedMeta = SensitiveDataRedactor.SanitizeToJson(request.Metadata);

        if (!aiProvider.IsConfigured)
        {
            db.AiUsageLogs.Add(AiUsageLog.Record(tenantId, currentUser.UserId, model.ModelCode, aiProvider.ProviderName, 0, 0, "ProviderNotConfigured", sanitizedMeta, clock.UtcNow));
            await db.SaveChangesAsync(ct);
            return Error.BusinessRule("AI_PROVIDER_NOT_CONFIGURED", "External AI provider credentials are not configured in this environment.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(model.TimeoutSeconds));

        var maxTokens = Math.Min(request.MaxTokens, model.MaxTokensLimit);
        var response = await aiProvider.CompleteAsync(new AiCompletionRequest(model.ModelCode, request.Prompt, maxTokens), cts.Token);

        db.AiUsageLogs.Add(AiUsageLog.Record(
            tenantId, currentUser.UserId, model.ModelCode, response.ProviderName,
            response.PromptTokens, response.CompletionTokens, "Success", sanitizedMeta, clock.UtcNow));
        await db.SaveChangesAsync(ct);

        await entitlementChecker.RecordUsageAsync(tenantId, "AI_GATEWAY", response.PromptTokens + response.CompletionTokens, ct);
        return response;
    }

    private static ProviderDto MapProvider(ProviderDefinition p) => new(
        p.Id, p.Code, p.Name, p.Category, p.IsActive, p.DefaultMaxRetries, p.DefaultTimeoutSeconds,
        p.Version, p.AllowedHosts, p.RequiresSecretRef);

    private static ConnectionDto MapConnection(IntegrationConnection c) => new(
        c.Id, c.TenantId, c.ProviderId, c.Name, c.CredentialReference, c.Status.ToString(),
        c.MaxRetries, c.TimeoutSeconds, c.LastSyncedAt, c.ApplicationCode, c.HealthStatus,
        c.LastHealthCheckAt, c.DisabledAt, c.NonSecretConfigJson);

    private static WebhookDto MapWebhook(WebhookRegistration w) => new(
        w.Id, w.TenantId, w.EventType, w.TargetUrl, w.IsActive, w.Description, w.SecretVersion, w.SecretPrefix, w.SecretRotatedAt, w.ApplicationCode);

    private static WebhookDeliveryDto MapDelivery(WebhookDelivery d) => new(
        d.Id, d.TenantId, d.WebhookId, d.EventId, d.EventType, d.Status.ToString(),
        d.AttemptCount, d.HttpStatusCode, d.TimestampUnix, d.SignatureDigestPrefix,
        d.FailureReason, d.AttemptedAt, d.CompletedAt, d.ResponseSnippet, d.DurationMs);
}
