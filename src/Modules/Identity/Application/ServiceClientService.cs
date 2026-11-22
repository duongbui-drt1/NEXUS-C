using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.BuildingBlocks.Observability;
using Nexus.Modules.Identity.Domain;
using Nexus.Modules.Identity.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Events;
using Nexus.SharedKernel.Results;
using Nexus.SharedKernel.Security;

namespace Nexus.Modules.Identity.Application;

public sealed record CreateServiceClientRequest(
    string ClientId,
    string Name,
    string? ApplicationCode,
    Guid? TenantId,
    IReadOnlyList<string> AllowedScopes,
    int RateLimitPerMinute = 300);

public sealed record ServiceClientDto(
    Guid Id,
    string ClientId,
    string Name,
    string? ApplicationCode,
    Guid? TenantId,
    string SecretPrefix,
    IReadOnlyList<string> AllowedScopes,
    int RateLimitPerMinute,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? SecretRotatedAt,
    DateTimeOffset? RevokedAt);

public sealed record ServiceClientWithSecretDto(ServiceClientDto Client, string ClientSecret);

public sealed record ClientCredentialsTokenRequest(
    string GrantType,
    string ClientId,
    string ClientSecret,
    string? Scope = null,
    Guid? TenantId = null);

public sealed record ServiceTokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresInSeconds,
    DateTimeOffset ExpiresAt,
    string ClientId,
    Guid? TenantId,
    IReadOnlyList<string> Scopes);

public sealed record CreateApiKeyRequest(
    string Name,
    IReadOnlyList<string> AllowedScopes,
    DateTimeOffset? ExpiresAt = null,
    Guid? TenantId = null);

public sealed record ApiKeyDto(
    Guid Id,
    string Name,
    Guid? TenantId,
    string KeyPrefix,
    IReadOnlyList<string> AllowedScopes,
    DateTimeOffset? ExpiresAt,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset? RevokedAt);

public sealed record ApiKeyWithSecretDto(ApiKeyDto ApiKey, string RawApiKey);

public interface IServiceClientService
{
    Task<Result<ServiceClientWithSecretDto>> CreateServiceClientAsync(CreateServiceClientRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ServiceClientDto>>> ListServiceClientsAsync(CancellationToken ct = default);
    Task<Result<ServiceClientWithSecretDto>> RotateServiceClientSecretAsync(Guid id, CancellationToken ct = default);
    Task<Result> RevokeServiceClientAsync(Guid id, CancellationToken ct = default);
    Task<Result<ServiceTokenResponse>> IssueClientCredentialsTokenAsync(ClientCredentialsTokenRequest request, CancellationToken ct = default);

    Task<Result<ApiKeyWithSecretDto>> CreateApiKeyAsync(CreateApiKeyRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ApiKeyDto>>> ListApiKeysAsync(CancellationToken ct = default);
    Task<Result<ApiKeyWithSecretDto>> RotateApiKeyAsync(Guid id, DateTimeOffset? newExpiresAt = null, CancellationToken ct = default);
    Task<Result> RevokeApiKeyAsync(Guid id, CancellationToken ct = default);
}

public sealed class ServiceClientService(
    IdentityModuleDbContext db,
    ITokenGenerator tokenGenerator,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ICorrelationContext correlationContext,
    ISystemClock clock,
    IAuditWriter auditWriter) : IServiceClientService, IServicePrincipalValidator
{
    private static readonly HashSet<string> KnownPermissions = Permissions.Catalog
        .Select(c => c.Code)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> PlatformOnlyPermissions = Permissions.Catalog
        .Where(c => c.Scope == PermissionScope.Platform)
        .Select(c => c.Code)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task<Result<ServiceClientWithSecretDto>> CreateServiceClientAsync(CreateServiceClientRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsPlatformUser)
        {
            return Error.Forbidden("SERVICE_CLIENT_PLATFORM_ONLY", "Only platform administrators may register service clients.");
        }

        var scopeCheck = ValidateScopes(request.AllowedScopes, allowPlatformScopes: !request.TenantId.HasValue);
        if (scopeCheck.IsFailure) return Result.Failure<ServiceClientWithSecretDto>(scopeCheck.Error);

        var normalizedId = request.ClientId?.Trim().ToLowerInvariant() ?? string.Empty;
        if (await db.ServiceClients.AsNoTracking().AnyAsync(c => c.ClientId == normalizedId, ct))
        {
            return Error.Conflict("SERVICE_CLIENT_DUPLICATE", $"Service client '{normalizedId}' already exists.");
        }

        var rawSecret = $"nxs_{CryptoHelper.GenerateSecureToken(36)}";
        var salt = CryptoHelper.GenerateSecureToken(16);
        var hash = HashSecret(rawSecret, salt);
        var prefix = rawSecret[..12];

        var created = ServiceClient.Create(
            normalizedId, request.Name, request.ApplicationCode, request.TenantId,
            salt, hash, prefix, scopeCheck.Value, request.RateLimitPerMinute);
        if (created.IsFailure) return Result.Failure<ServiceClientWithSecretDto>(created.Error);

        var client = created.Value;
        client.SetCreatedAudit(currentUser.UserId, clock.UtcNow);
        db.ServiceClients.Add(client);

        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.ServiceClientCreatedV1,
            client.TenantId,
            correlationContext.CorrelationId,
            new { client.Id, client.ClientId, client.ApplicationCode, client.TenantId, Scopes = scopeCheck.Value },
            occurredAt: clock.UtcNow));

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.service_client.create", "ServiceClient", client.Id.ToString("D"), client.TenantId, "Success",
            AfterState: new { client.ClientId, client.Name, client.ApplicationCode, client.TenantId, Scopes = scopeCheck.Value },
            IsCrossTenantPlatformAccess: true), ct);

        return new ServiceClientWithSecretDto(MapClient(client), rawSecret);
    }

    public async Task<Result<IReadOnlyList<ServiceClientDto>>> ListServiceClientsAsync(CancellationToken ct = default)
    {
        var query = db.ServiceClients.AsNoTracking();
        if (!currentUser.IsPlatformUser)
        {
            if (!tenantContext.HasTenant)
            {
                return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
            }
            var tid = tenantContext.TenantId!.Value;
            query = query.Where(c => c.TenantId == tid);
        }

        var items = await query.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        return items.Select(MapClient).ToList();
    }

    public async Task<Result<ServiceClientWithSecretDto>> RotateServiceClientSecretAsync(Guid id, CancellationToken ct = default)
    {
        if (!currentUser.IsPlatformUser)
        {
            return Error.Forbidden("SERVICE_CLIENT_PLATFORM_ONLY", "Only platform administrators may rotate service client secrets.");
        }

        var client = await db.ServiceClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return Error.NotFound("SERVICE_CLIENT_NOT_FOUND", "Service client was not found.");
        if (!client.IsActive) return Error.BusinessRule("SERVICE_CLIENT_REVOKED", "Cannot rotate secret of a revoked service client.");

        var rawSecret = $"nxs_{CryptoHelper.GenerateSecureToken(36)}";
        var salt = CryptoHelper.GenerateSecureToken(16);
        var hash = HashSecret(rawSecret, salt);
        var now = clock.UtcNow;

        client.RotateSecret(salt, hash, rawSecret[..12], now);
        client.SetUpdatedAudit(currentUser.UserId, now);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.service_client.rotate_secret", "ServiceClient", client.Id.ToString("D"), client.TenantId, "Success",
            AfterState: new { client.ClientId, client.SecretPrefix, client.SecretRotatedAt },
            IsCrossTenantPlatformAccess: true), ct);

        return new ServiceClientWithSecretDto(MapClient(client), rawSecret);
    }

    public async Task<Result> RevokeServiceClientAsync(Guid id, CancellationToken ct = default)
    {
        if (!currentUser.IsPlatformUser)
        {
            return Result.Failure(Error.Forbidden("SERVICE_CLIENT_PLATFORM_ONLY", "Only platform administrators may revoke service clients."));
        }

        var client = await db.ServiceClients.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (client is null) return Result.Failure(Error.NotFound("SERVICE_CLIENT_NOT_FOUND", "Service client was not found."));

        var now = clock.UtcNow;
        client.Revoke(now);
        client.SetUpdatedAudit(currentUser.UserId, now);

        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.ServiceClientRevokedV1,
            client.TenantId,
            correlationContext.CorrelationId,
            new { client.Id, client.ClientId, client.ApplicationCode, client.TenantId },
            occurredAt: now));

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.service_client.revoke", "ServiceClient", client.Id.ToString("D"), client.TenantId, "Success",
            AfterState: new { client.ClientId, client.RevokedAt },
            IsCrossTenantPlatformAccess: true), ct);

        return Result.Success();
    }

    public async Task<Result<ServiceTokenResponse>> IssueClientCredentialsTokenAsync(ClientCredentialsTokenRequest request, CancellationToken ct = default)
    {
        if (!string.Equals(request.GrantType?.Trim(), "client_credentials", StringComparison.OrdinalIgnoreCase))
        {
            return Error.Validation("UNSUPPORTED_GRANT_TYPE", "Only grant_type 'client_credentials' is supported.");
        }

        if (string.IsNullOrWhiteSpace(request.ClientId) || string.IsNullOrWhiteSpace(request.ClientSecret))
        {
            return Error.Unauthorized("INVALID_CLIENT", "client_id and client_secret are required.");
        }

        var normalizedId = request.ClientId.Trim().ToLowerInvariant();
        var client = await db.ServiceClients.FirstOrDefaultAsync(c => c.ClientId == normalizedId, ct);
        if (client is null || !client.IsActive)
        {
            NexusPlatformMetrics.AuthAttempts.Add(1, new KeyValuePair<string, object?>("mode", "client_credentials"), new KeyValuePair<string, object?>("result", "failure"));
            return Error.Unauthorized("INVALID_CLIENT", "Invalid or revoked service client credentials.");
        }

        var candidateHash = HashSecret(request.ClientSecret.Trim(), client.SecretSalt);
        if (!CryptoHelper.FixedTimeEquals(candidateHash, client.ClientSecretHash))
        {
            NexusPlatformMetrics.AuthAttempts.Add(1, new KeyValuePair<string, object?>("mode", "client_credentials"), new KeyValuePair<string, object?>("result", "failure"));
            return Error.Unauthorized("INVALID_CLIENT", "Invalid or revoked service client credentials.");
        }

        var allowed = client.GetAllowedScopes();
        IReadOnlyList<string> grantedScopes;
        if (string.IsNullOrWhiteSpace(request.Scope))
        {
            grantedScopes = allowed;
        }
        else
        {
            var requested = request.Scope.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var allowedSet = allowed.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (requested.Length == 0 || requested.Any(s => !allowedSet.Contains(s)))
            {
                return Error.Forbidden("INVALID_SCOPE", "Requested scope exceeds scopes granted to this service client.");
            }
            grantedScopes = requested.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        Guid? effectiveTenantId = client.TenantId;
        if (request.TenantId.HasValue)
        {
            if (client.TenantId.HasValue && client.TenantId.Value != request.TenantId.Value)
            {
                return Error.Forbidden("CLIENT_TENANT_MISMATCH", "Service client is bound to a different tenant.");
            }
            effectiveTenantId = request.TenantId.Value;
        }

        var now = clock.UtcNow;
        client.MarkUsed(now);
        await db.SaveChangesAsync(ct);

        var lifetime = TimeSpan.FromMinutes(10);
        var (token, expiresAt) = tokenGenerator.GenerateServiceAccessToken(
            client.Id, client.ClientId, client.ApplicationCode, effectiveTenantId, grantedScopes, lifetime);

        NexusPlatformMetrics.AuthAttempts.Add(1, new KeyValuePair<string, object?>("mode", "client_credentials"), new KeyValuePair<string, object?>("result", "success"));

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.service_client.token_issued", "ServiceClient", client.Id.ToString("D"), effectiveTenantId, "Success",
            AfterState: new { client.ClientId, Scopes = grantedScopes, TenantId = effectiveTenantId }), ct);

        return new ServiceTokenResponse(
            token, "Bearer", (int)lifetime.TotalSeconds, expiresAt, client.ClientId, effectiveTenantId, grantedScopes);
    }

    // === Scoped API Keys ===

    public async Task<Result<ApiKeyWithSecretDto>> CreateApiKeyAsync(CreateApiKeyRequest request, CancellationToken ct = default)
    {
        var isPlatformCaller = currentUser.IsPlatformUser;
        Guid? targetTenantId = isPlatformCaller ? request.TenantId : tenantContext.TenantId;

        if (!isPlatformCaller && !targetTenantId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required to create an API key.");
        }

        if (!isPlatformCaller && request.TenantId.HasValue && request.TenantId.Value != targetTenantId!.Value)
        {
            return Error.Forbidden("CROSS_TENANT_API_KEY_DENIED", "Cannot create an API key for another tenant.");
        }

        var scopeCheck = ValidateScopes(request.AllowedScopes, allowPlatformScopes: isPlatformCaller && !targetTenantId.HasValue);
        if (scopeCheck.IsFailure) return Result.Failure<ApiKeyWithSecretDto>(scopeCheck.Error);

        var rawKey = $"nxk_{CryptoHelper.GenerateSecureToken(32)}";
        var keyHash = CryptoHelper.Sha256Hex(rawKey);
        var prefix = rawKey[..12];

        var created = ApiKeyRecord.Create(request.Name, targetTenantId, prefix, keyHash, scopeCheck.Value, request.ExpiresAt, currentUser.UserId);
        if (created.IsFailure) return Result.Failure<ApiKeyWithSecretDto>(created.Error);

        var key = created.Value;
        key.SetCreatedAudit(currentUser.UserId, clock.UtcNow);
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.api_key.create", "ApiKeyRecord", key.Id.ToString("D"), targetTenantId, "Success",
            AfterState: new { key.Name, key.KeyPrefix, Scopes = scopeCheck.Value, key.ExpiresAt }), ct);

        return new ApiKeyWithSecretDto(MapKey(key), rawKey);
    }

    public async Task<Result<IReadOnlyList<ApiKeyDto>>> ListApiKeysAsync(CancellationToken ct = default)
    {
        var query = db.ApiKeys.AsNoTracking();
        if (!currentUser.IsPlatformUser)
        {
            if (!tenantContext.HasTenant)
            {
                return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
            }
            var tid = tenantContext.TenantId!.Value;
            query = query.Where(k => k.TenantId == tid);
        }

        var items = await query.OrderByDescending(k => k.CreatedAt).ToListAsync(ct);
        return items.Select(MapKey).ToList();
    }

    public async Task<Result<ApiKeyWithSecretDto>> RotateApiKeyAsync(Guid id, DateTimeOffset? newExpiresAt = null, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null) return Error.NotFound("API_KEY_NOT_FOUND", "API key was not found.");

        if (!currentUser.IsPlatformUser && key.TenantId != tenantContext.TenantId)
        {
            return Error.Forbidden("CROSS_TENANT_API_KEY_DENIED", "Cannot rotate an API key belonging to another tenant.");
        }

        if (!key.IsActive) return Error.BusinessRule("API_KEY_REVOKED", "Cannot rotate a revoked API key.");

        var rawKey = $"nxk_{CryptoHelper.GenerateSecureToken(32)}";
        key.Rotate(rawKey[..12], CryptoHelper.Sha256Hex(rawKey), newExpiresAt);
        key.SetUpdatedAudit(currentUser.UserId, clock.UtcNow);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.api_key.rotate", "ApiKeyRecord", key.Id.ToString("D"), key.TenantId, "Success",
            AfterState: new { key.Name, key.KeyPrefix, key.ExpiresAt }), ct);

        return new ApiKeyWithSecretDto(MapKey(key), rawKey);
    }

    public async Task<Result> RevokeApiKeyAsync(Guid id, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct);
        if (key is null) return Result.Failure(Error.NotFound("API_KEY_NOT_FOUND", "API key was not found."));

        if (!currentUser.IsPlatformUser && key.TenantId != tenantContext.TenantId)
        {
            return Result.Failure(Error.Forbidden("CROSS_TENANT_API_KEY_DENIED", "Cannot revoke an API key belonging to another tenant."));
        }

        key.Revoke(clock.UtcNow);
        key.SetUpdatedAudit(currentUser.UserId, clock.UtcNow);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.api_key.revoke", "ApiKeyRecord", key.Id.ToString("D"), key.TenantId, "Success"), ct);

        return Result.Success();
    }

    // === IServicePrincipalValidator ===

    public async Task<ServicePrincipalValidationResult> ValidateServiceClientAsync(string clientId, CancellationToken ct = default)
    {
        var normalized = clientId.Trim().ToLowerInvariant();
        var client = await db.ServiceClients.AsNoTracking().FirstOrDefaultAsync(c => c.ClientId == normalized, ct);
        if (client is null || !client.IsActive)
        {
            return new ServicePrincipalValidationResult(false, null, null, null, [], "Service client is revoked or inactive.");
        }

        return new ServicePrincipalValidationResult(true, client.Id, client.ClientId, client.TenantId, client.GetAllowedScopes());
    }

    public async Task<ServicePrincipalValidationResult> ValidateApiKeyAsync(string rawApiKey, CancellationToken ct = default)
    {
        var hash = CryptoHelper.Sha256Hex(rawApiKey.Trim());
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.KeyHash == hash, ct);
        if (key is null || !key.IsActive)
        {
            return new ServicePrincipalValidationResult(false, null, null, null, [], "API key is invalid or revoked.");
        }

        if (key.ExpiresAt.HasValue && key.ExpiresAt.Value <= clock.UtcNow)
        {
            return new ServicePrincipalValidationResult(false, null, null, null, [], "API key has expired.");
        }

        key.MarkUsed(clock.UtcNow);
        await db.SaveChangesAsync(ct);

        return new ServicePrincipalValidationResult(true, key.Id, $"apikey:{key.KeyPrefix}", key.TenantId, key.GetAllowedScopes());
    }

    private static Result<IReadOnlyList<string>> ValidateScopes(IReadOnlyList<string> scopes, bool allowPlatformScopes)
    {
        if (scopes is null || scopes.Count == 0)
        {
            return Error.Validation("SCOPES_REQUIRED", "At least one explicit permission scope must be specified.");
        }

        var clean = scopes
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (clean.Count == 0 || clean.Any(s => s == "*" || s.Contains('*')))
        {
            return Error.Forbidden("WILDCARD_SCOPE_FORBIDDEN", "Wildcard scopes are prohibited. Explicit least-privilege permission codes are required.");
        }

        var unknown = clean.Where(s => !KnownPermissions.Contains(s)).ToList();
        if (unknown.Count > 0)
        {
            return Error.Validation("UNKNOWN_SCOPE", $"Unknown permission scope(s): {string.Join(", ", unknown)}");
        }

        if (!allowPlatformScopes && clean.Any(PlatformOnlyPermissions.Contains))
        {
            return Error.Forbidden("PLATFORM_SCOPE_FORBIDDEN", "Tenant-bound service clients or API keys cannot be granted platform-only scopes.");
        }

        return clean;
    }

    private static string HashSecret(string rawSecret, string salt)
        => CryptoHelper.Sha256Hex($"{salt}:{rawSecret}");

    private static ServiceClientDto MapClient(ServiceClient c) => new(
        c.Id, c.ClientId, c.Name, c.ApplicationCode, c.TenantId, c.SecretPrefix,
        c.GetAllowedScopes(), c.RateLimitPerMinute, c.IsActive, c.CreatedAt,
        c.LastUsedAt, c.SecretRotatedAt, c.RevokedAt);

    private static ApiKeyDto MapKey(ApiKeyRecord k) => new(
        k.Id, k.Name, k.TenantId, k.KeyPrefix, k.GetAllowedScopes(),
        k.ExpiresAt, k.IsActive, k.CreatedAt, k.LastUsedAt, k.RevokedAt);
}
