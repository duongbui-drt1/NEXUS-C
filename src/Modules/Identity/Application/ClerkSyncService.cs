using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.Modules.Identity.Domain;
using Nexus.Modules.Identity.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Results;
using Npgsql;

namespace Nexus.Modules.Identity.Application;

public sealed class ClerkOptions
{
    public const string SectionName = "Clerk";

    public bool Enabled { get; set; }
    public string AppId { get; set; } = "app_3MTU0OPO0cFJboGQYPkeOLjZohj";
    public string PublishableKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string JwksUri { get; set; } = string.Empty;
    public string[] AuthorizedParties { get; set; } = [];
    public string WebhookSecret { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.clerk.com/v1";
    public bool AutoLinkVerifiedEmail { get; set; }
}

public sealed record ClerkWebhookResultDto(
    string SvixId,
    string EventType,
    string Status,
    bool Deduplicated,
    Guid? InternalUserId);

public sealed record ClerkWebhookEventDto(
    Guid Id,
    string SvixId,
    string EventType,
    string? ProviderSubject,
    DateTimeOffset EventTimestamp,
    string Status,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    string? ErrorMessage);

public sealed record ClerkSyncStatusDto(
    bool Enabled,
    bool SecretKeyConfigured,
    bool WebhookSecretConfigured,
    string AppId,
    string Issuer,
    int LinkedUserCount,
    int FailedEventCount,
    DateTimeOffset? LastSyncedAt,
    IReadOnlyList<ClerkWebhookEventDto> RecentEvents);

public sealed record LinkClerkAccountRequest(
    Guid UserId,
    string ClerkSubject,
    string? ProviderEmail = null,
    bool EmailVerified = true);

public sealed record ClerkReconcileResultDto(
    int InspectedCount,
    int CreatedCount,
    int UpdatedCount,
    int SkippedStaleCount,
    DateTimeOffset CompletedAt);

public interface IClerkSyncService
{
    Task<Result<ClerkWebhookResultDto>> ProcessWebhookAsync(
        string rawBody,
        string? svixId,
        string? svixTimestamp,
        string? svixSignature,
        CancellationToken ct = default);

    Task<Result<ClerkSyncStatusDto>> GetSyncStatusAsync(CancellationToken ct = default);

    Task<Result> LinkUserToClerkSubjectAsync(LinkClerkAccountRequest request, CancellationToken ct = default);

    Task<Result<ClerkReconcileResultDto>> ReconcileFromClerkApiAsync(CancellationToken ct = default);
}

public sealed class ClerkSyncService(
    IdentityModuleDbContext db,
    UserManager<ApplicationUser> userManager,
    IOptions<ClerkOptions> options,
    IHttpClientFactory httpClientFactory,
    ICurrentUser currentUser,
    ISystemClock clock,
    IAuditWriter auditWriter,
    ILogger<ClerkSyncService> logger) : IClerkSyncService
{
    public const string ProviderName = "Clerk";

    public async Task<Result<ClerkWebhookResultDto>> ProcessWebhookAsync(
        string rawBody,
        string? svixId,
        string? svixTimestamp,
        string? svixSignature,
        CancellationToken ct = default)
    {
        var cfg = options.Value;
        if (string.IsNullOrWhiteSpace(cfg.WebhookSecret))
        {
            return Error.BusinessRule("CLERK_WEBHOOK_NOT_CONFIGURED", "Clerk webhook signing secret is not configured on this server.");
        }

        var now = clock.UtcNow;
        if (!SvixSignatureVerifier.Verify(svixId, svixTimestamp, svixSignature, rawBody, cfg.WebhookSecret, now, maxToleranceSeconds: 300))
        {
            logger.LogWarning("Rejected Clerk webhook with invalid Svix signature or expired timestamp (svix-id={SvixId})", svixId);
            return Error.Unauthorized("CLERK_WEBHOOK_INVALID_SIGNATURE", "Invalid Clerk Svix webhook signature or expired timestamp.");
        }

        string eventType;
        string? clerkSubject;
        DateTimeOffset eventTimestamp;
        JsonElement data;

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            eventType = root.TryGetProperty("type", out var tp) ? tp.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(eventType) || !root.TryGetProperty("data", out var dataElem))
            {
                return Error.Validation("CLERK_WEBHOOK_MALFORMED", "Clerk webhook payload is missing required 'type' or 'data' fields.");
            }

            data = dataElem.Clone();
            clerkSubject = data.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

            if (root.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.Number && tsEl.TryGetInt64(out var tsMs))
            {
                eventTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsMs);
            }
            else if (data.TryGetProperty("updated_at", out var updEl) && updEl.ValueKind == JsonValueKind.Number && updEl.TryGetInt64(out var updMs))
            {
                eventTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(updMs);
            }
            else
            {
                eventTimestamp = long.TryParse(svixTimestamp, out var headerSec)
                    ? DateTimeOffset.FromUnixTimeSeconds(headerSec)
                    : now;
            }
        }
        catch (JsonException)
        {
            return Error.Validation("CLERK_WEBHOOK_INVALID_JSON", "Clerk webhook body is not valid JSON.");
        }

        // Durable inbox deduplication BEFORE any side effects
        var inboxRecord = ClerkWebhookEvent.Create(svixId!, eventType, clerkSubject, eventTimestamp, now);
        db.ClerkWebhookEvents.Add(inboxRecord);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return new ClerkWebhookResultDto(svixId!, eventType, "Deduplicated", true, null);
        }

        try
        {
            var (status, userId) = await ApplyClerkEventAsync(eventType, clerkSubject, data, eventTimestamp, now, cfg.AutoLinkVerifiedEmail, ct);
            inboxRecord.MarkProcessed(status, clock.UtcNow);
            await db.SaveChangesAsync(ct);

            await auditWriter.WriteAsync(new AuditEntryRequest(
                $"identity.clerk.webhook.{eventType}",
                "ExternalIdentity",
                clerkSubject ?? svixId!,
                null,
                "Success",
                AfterState: new { SvixId = svixId, EventType = eventType, Status = status, InternalUserId = userId }), ct);

            return new ClerkWebhookResultDto(svixId!, eventType, status, false, userId);
        }
        catch (Exception ex)
        {
            inboxRecord.MarkProcessed("Failed", clock.UtcNow, ex.Message);
            await db.SaveChangesAsync(ct);
            logger.LogError(ex, "Error processing Clerk webhook event {SvixId} ({EventType})", svixId, eventType);
            return Error.Failure("CLERK_WEBHOOK_PROCESSING_FAILED", "Failed to process Clerk webhook event.");
        }
    }

    public async Task<Result<ClerkSyncStatusDto>> GetSyncStatusAsync(CancellationToken ct = default)
    {
        var cfg = options.Value;
        var linkedCount = await db.ExternalIdentities.AsNoTracking().CountAsync(e => e.Provider == ProviderName, ct);
        var failedCount = await db.ClerkWebhookEvents.AsNoTracking().CountAsync(e => e.Status == "Failed", ct);
        var lastSynced = await db.ExternalIdentities.AsNoTracking()
            .Where(e => e.Provider == ProviderName && e.SyncedAt != null)
            .OrderByDescending(e => e.SyncedAt)
            .Select(e => e.SyncedAt)
            .FirstOrDefaultAsync(ct);

        var recent = await db.ClerkWebhookEvents.AsNoTracking()
            .OrderByDescending(e => e.ReceivedAt)
            .Take(25)
            .Select(e => new ClerkWebhookEventDto(
                e.Id, e.SvixId, e.EventType, e.ProviderSubject, e.EventTimestamp, e.Status, e.ReceivedAt, e.ProcessedAt, e.ErrorMessage))
            .ToListAsync(ct);

        return new ClerkSyncStatusDto(
            cfg.Enabled,
            !string.IsNullOrWhiteSpace(cfg.SecretKey),
            !string.IsNullOrWhiteSpace(cfg.WebhookSecret),
            cfg.AppId,
            cfg.Issuer,
            linkedCount,
            failedCount,
            lastSynced,
            recent);
    }

    public async Task<Result> LinkUserToClerkSubjectAsync(LinkClerkAccountRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsPlatformUser)
        {
            return Result.Failure(Error.Forbidden("CLERK_LINK_PLATFORM_ONLY", "Only platform administrators may link Clerk identities."));
        }

        if (string.IsNullOrWhiteSpace(request.ClerkSubject))
        {
            return Result.Failure(Error.Validation("CLERK_INVALID_SUBJECT", "ClerkSubject is required."));
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", "Target internal NEXUS user was not found."));
        }

        var subject = request.ClerkSubject.Trim();
        var existingBySubject = await db.ExternalIdentities.FirstOrDefaultAsync(
            e => e.Provider == ProviderName && e.ProviderSubject == subject, ct);

        if (existingBySubject is not null && existingBySubject.UserId != request.UserId)
        {
            return Result.Failure(Error.Conflict("CLERK_SUBJECT_ALREADY_LINKED", "This Clerk subject is already linked to a different NEXUS user."));
        }

        if (existingBySubject is null)
        {
            var link = ExternalIdentity.Create(request.UserId, ProviderName, subject, request.ProviderEmail ?? user.Email, request.EmailVerified, clock.UtcNow);
            link.SetCreatedAudit(currentUser.UserId, clock.UtcNow);
            db.ExternalIdentities.Add(link);
            await db.SaveChangesAsync(ct);
        }

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.clerk.link",
            "ExternalIdentity",
            subject,
            null,
            "Success",
            AfterState: new { request.UserId, ClerkSubject = subject, request.EmailVerified },
            IsCrossTenantPlatformAccess: true), ct);

        return Result.Success();
    }

    public async Task<Result<ClerkReconcileResultDto>> ReconcileFromClerkApiAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsPlatformUser)
        {
            return Error.Forbidden("CLERK_RECONCILE_PLATFORM_ONLY", "Only platform administrators may trigger Clerk reconciliation.");
        }

        var cfg = options.Value;
        if (string.IsNullOrWhiteSpace(cfg.SecretKey))
        {
            return Error.BusinessRule("CLERK_SECRET_NOT_CONFIGURED", "Clerk:SecretKey is not configured. Run `npx -y clerk@latest auth login` and supply CLERK_SECRET_KEY before reconciliation.");
        }

        var client = httpClientFactory.CreateClient("ClerkBackendApi");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cfg.SecretKey.Trim());

        var inspected = 0;
        var created = 0;
        var updated = 0;
        var skippedStale = 0;
        var offset = 0;
        const int pageSize = 100;
        var now = clock.UtcNow;

        while (true)
        {
            var url = $"{cfg.ApiBaseUrl.TrimEnd('/')}/users?limit={pageSize}&offset={offset}";
            using var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return Error.Failure("CLERK_API_ERROR", $"Clerk Backend API returned HTTP {(int)resp.StatusCode} during user reconciliation.");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                break;
            }

            var batchCount = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                batchCount++;
                inspected++;
                var sub = item.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(sub)) continue;

                var eventTs = item.TryGetProperty("updated_at", out var updEl) && updEl.ValueKind == JsonValueKind.Number && updEl.TryGetInt64(out var ms)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
                    : now;

                var (status, _) = await ApplyClerkEventAsync("user.updated", sub, item, eventTs, now, cfg.AutoLinkVerifiedEmail, ct);
                if (status == "Created") created++;
                else if (status == "Updated") updated++;
                else if (status == "IgnoredStale") skippedStale++;
            }

            if (batchCount < pageSize) break;
            offset += pageSize;
        }

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.clerk.reconcile",
            "ExternalIdentity",
            "Clerk",
            null,
            "Success",
            AfterState: new { inspected, created, updated, skippedStale },
            IsCrossTenantPlatformAccess: true), ct);

        return new ClerkReconcileResultDto(inspected, created, updated, skippedStale, clock.UtcNow);
    }

    private async Task<(string Status, Guid? InternalUserId)> ApplyClerkEventAsync(
        string eventType,
        string? clerkSubject,
        JsonElement data,
        DateTimeOffset eventTimestamp,
        DateTimeOffset now,
        bool autoLinkVerifiedEmail,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(clerkSubject))
        {
            return ("IgnoredNoSubject", null);
        }

        var ext = await db.ExternalIdentities.FirstOrDefaultAsync(
            e => e.Provider == ProviderName && e.ProviderSubject == clerkSubject, ct);

        if (ext is not null && !ext.CanApplyEvent(eventTimestamp))
        {
            // Strict out-of-order protection: never let a stale Clerk event overwrite newer local data!
            return ("IgnoredStale", ext.UserId);
        }

        if (eventType is "user.deleted")
        {
            if (ext is null) return ("IgnoredUnknownSubject", null);

            var user = await userManager.FindByIdAsync(ext.UserId.ToString("D"));
            if (user is not null)
            {
                user.IsActive = false;
                user.DisabledAt = now;
                user.DisabledReason = "ClerkUserDeleted";
                user.UpdatedAt = now;
                await userManager.UpdateAsync(user);

                var sessions = await db.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null).ToListAsync(ct);
                foreach (var s in sessions) s.Revoke("ClerkUserDeleted", now);
            }

            ext.MarkDeactivated(eventTimestamp, now);
            await db.SaveChangesAsync(ct);
            return ("Deactivated", ext.UserId);
        }

        if (eventType is "user.banned" or "user.unbanned")
        {
            if (ext is null) return ("IgnoredUnknownSubject", null);

            var banned = eventType == "user.banned";
            var user = await userManager.FindByIdAsync(ext.UserId.ToString("D"));
            if (user is not null)
            {
                user.IsActive = !banned;
                user.DisabledAt = banned ? now : null;
                user.DisabledReason = banned ? "ClerkUserBanned" : null;
                user.UpdatedAt = now;
                await userManager.UpdateAsync(user);

                if (banned)
                {
                    var sessions = await db.UserSessions.Where(s => s.UserId == user.Id && s.RevokedAt == null).ToListAsync(ct);
                    foreach (var s in sessions) s.Revoke("ClerkUserBanned", now);
                }
            }

            ext.ApplySyncUpdate(ext.ProviderEmail, ext.EmailVerified, banned, eventTimestamp, now);
            await db.SaveChangesAsync(ct);
            return (banned ? "Banned" : "Unbanned", ext.UserId);
        }

        if (eventType is "user.created" or "user.updated")
        {
            var (email, emailVerified) = ExtractPrimaryEmail(data);
            var firstName = data.TryGetProperty("first_name", out var fn) && fn.ValueKind == JsonValueKind.String ? fn.GetString() : null;
            var lastName = data.TryGetProperty("last_name", out var ln) && ln.ValueKind == JsonValueKind.String ? ln.GetString() : null;
            var isBanned = data.TryGetProperty("banned", out var bn) && bn.ValueKind == JsonValueKind.True;
            var fullName = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            if (string.IsNullOrWhiteSpace(fullName)) fullName = email ?? clerkSubject;

            if (ext is not null)
            {
                var existingUser = await userManager.FindByIdAsync(ext.UserId.ToString("D"));
                if (existingUser is not null)
                {
                    if (isBanned && existingUser.IsActive)
                    {
                        existingUser.IsActive = false;
                        existingUser.DisabledAt = now;
                        existingUser.DisabledReason = "ClerkUserBanned";
                    }
                    existingUser.EmailConfirmed = emailVerified || existingUser.EmailConfirmed;
                    existingUser.UpdatedAt = now;
                    await userManager.UpdateAsync(existingUser);

                    var prof = await db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == existingUser.Id, ct);
                    if (prof is not null)
                    {
                        prof.Update(fullName, null, null, null, firstName, lastName);
                    }
                }

                ext.ApplySyncUpdate(email, emailVerified, isBanned, eventTimestamp, now);
                await db.SaveChangesAsync(ct);
                return ("Updated", ext.UserId);
            }

            // New Clerk subject: NEVER auto-link on unverified email!
            ApplicationUser? linkedUser = null;
            if (autoLinkVerifiedEmail && emailVerified && !string.IsNullOrWhiteSpace(email))
            {
                linkedUser = await userManager.FindByEmailAsync(email);
            }

            if (linkedUser is null)
            {
                var safeEmail = !string.IsNullOrWhiteSpace(email) ? email : $"{clerkSubject}@clerk.external.invalid";
                linkedUser = new ApplicationUser
                {
                    Id = Guid.CreateVersion7(),
                    UserName = safeEmail,
                    Email = safeEmail,
                    EmailConfirmed = emailVerified,
                    IsActive = !isBanned,
                    IsPlatformUser = false, // NEVER grant platform privileges from Clerk events!
                    CreatedAt = now,
                    UpdatedAt = now
                };

                var createRes = await userManager.CreateAsync(linkedUser);
                if (!createRes.Succeeded)
                {
                    throw new InvalidOperationException($"Failed to provision local NEXUS user for Clerk subject '{clerkSubject}': {string.Join("; ", createRes.Errors.Select(e => e.Description))}");
                }

                var profile = UserProfile.Create(linkedUser.Id, fullName);
                profile.Update(fullName, null, null, null, firstName, lastName);
                db.UserProfiles.Add(profile);
            }

            // Create immutable ExternalIdentity mapping — ZERO roles, memberships, or entitlements granted!
            db.ExternalIdentities.Add(ExternalIdentity.Create(linkedUser.Id, ProviderName, clerkSubject, email, emailVerified, eventTimestamp));
            await db.SaveChangesAsync(ct);
            return ("Created", linkedUser.Id);
        }

        return ("IgnoredUnsupportedType", null);
    }

    private static (string? Email, bool Verified) ExtractPrimaryEmail(JsonElement data)
    {
        var primaryId = data.TryGetProperty("primary_email_address_id", out var pid) && pid.ValueKind == JsonValueKind.String
            ? pid.GetString()
            : null;

        if (!data.TryGetProperty("email_addresses", out var emails) || emails.ValueKind != JsonValueKind.Array)
        {
            return (null, false);
        }

        string? fallbackEmail = null;
        var fallbackVerified = false;

        foreach (var item in emails.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String ? idEl.GetString() : null;
            var addr = item.TryGetProperty("email_address", out var addrEl) && addrEl.ValueKind == JsonValueKind.String ? addrEl.GetString()?.Trim().ToLowerInvariant() : null;
            var verified = item.TryGetProperty("verification", out var ver) &&
                           ver.ValueKind == JsonValueKind.Object &&
                           ver.TryGetProperty("status", out var st) &&
                           string.Equals(st.GetString(), "verified", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(primaryId) && string.Equals(id, primaryId, StringComparison.Ordinal))
            {
                return (addr, verified);
            }

            if (fallbackEmail is null && !string.IsNullOrWhiteSpace(addr))
            {
                fallbackEmail = addr;
                fallbackVerified = verified;
            }
        }

        return (fallbackEmail, fallbackVerified);
    }
}
