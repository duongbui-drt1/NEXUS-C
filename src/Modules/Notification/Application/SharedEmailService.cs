using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.Modules.Notification.Domain;
using Nexus.Modules.Notification.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;
using Npgsql;

namespace Nexus.Modules.Notification.Application;

public sealed record SendTransactionalEmailRequest(
    string To,
    string Subject,
    string Body,
    bool IsHtml = false,
    string? IdempotencyKey = null,
    string? TemplateKey = null,
    int TemplateVersion = 1);

public sealed record EmailDeliveryDto(
    Guid Id,
    Guid? TenantId,
    string Provider,
    string? ProviderMessageId,
    string? TemplateKey,
    int TemplateVersion,
    string RecipientMasked,
    string Subject,
    string Status,
    int AttemptCount,
    bool IsTransientError,
    string? LastError,
    string? CorrelationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SentAt,
    DateTimeOffset? DeliveredAt);

public sealed record EmailSuppressionDto(
    Guid Id,
    string RecipientMasked,
    string Reason,
    string Provider,
    bool IsActive,
    DateTimeOffset SuppressedAt);

public sealed record EmailProviderStatusDto(
    string ActiveProvider,
    bool ApiKeyConfigured,
    bool WebhookSecretConfigured,
    string DefaultFromAddress,
    string DefaultFromName,
    long TotalDeliveries,
    long DeliveredOrSentCount,
    long FailedCount,
    long SuppressedCount);

public sealed record ResendWebhookResultDto(
    string SvixId,
    string EventType,
    bool Deduplicated,
    string? ProviderMessageId);

public interface ISharedEmailService
{
    Task<Result<EmailDeliveryDto>> SendTransactionalEmailAsync(SendTransactionalEmailRequest request, CancellationToken ct = default);
    Task<Result<EmailProviderStatusDto>> GetProviderStatusAsync(CancellationToken ct = default);
    Task<Result<PagedResult<EmailDeliveryDto>>> ListDeliveriesAsync(PagedRequest paging, string? status = null, CancellationToken ct = default);
    Task<Result<IReadOnlyList<EmailSuppressionDto>>> ListSuppressionsAsync(CancellationToken ct = default);
    Task<Result<ResendWebhookResultDto>> ProcessResendWebhookAsync(string rawBody, string? svixId, string? svixTimestamp, string? svixSignature, CancellationToken ct = default);
}

public sealed class SharedEmailService(
    NotificationDbContext db,
    IEmailSender emailSender,
    IOptions<EmailOptions> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ICorrelationContext correlationContext,
    ISystemClock clock,
    IAuditWriter auditWriter) : ISharedEmailService
{
    public async Task<Result<EmailDeliveryDto>> SendTransactionalEmailAsync(SendTransactionalEmailRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.To) || !request.To.Contains('@') ||
            string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
        {
            return Error.Validation("EMAIL_INVALID_INPUT", "Valid recipient email, subject, and body are required.");
        }

        var normalizedEmail = request.To.Trim().ToLowerInvariant();
        var recipientHash = CryptoHelper.Sha256Hex(normalizedEmail);
        var masked = LoggingEmailSender.MaskEmail(normalizedEmail);
        var idemKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();

        // 1. Idempotency / deduplication check
        if (idemKey is not null)
        {
            var existing = await db.EmailDeliveries.AsNoTracking()
                .FirstOrDefaultAsync(e => e.IdempotencyKey == idemKey, ct);
            if (existing is not null)
            {
                return Map(existing);
            }
        }

        var record = EmailDeliveryRecord.Create(
            tenantContext.TenantId,
            emailSender.ProviderName,
            masked,
            recipientHash,
            request.Subject.Trim(),
            idemKey,
            request.TemplateKey,
            request.TemplateVersion,
            correlationContext.CorrelationId);

        // 2. Suppression check (bounces/complaints)
        var suppressed = await db.EmailSuppressions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.RecipientHash == recipientHash && s.IsActive, ct);
        if (suppressed is not null)
        {
            record.MarkSuppressed($"Recipient suppressed due to prior {suppressed.Reason}.");
            db.EmailDeliveries.Add(record);
            await db.SaveChangesAsync(ct);
            return Map(record);
        }

        // 3. Dispatch via provider-neutral IEmailSender (Resend in production/configured env)
        var dispatch = await emailSender.SendWithResultAsync(new EmailMessage(
            normalizedEmail,
            request.Subject.Trim(),
            request.Body,
            request.IsHtml,
            idemKey,
            CorrelationId: correlationContext.CorrelationId), ct);

        if (dispatch.Succeeded)
        {
            record.MarkSent(dispatch.ProviderMessageId, clock.UtcNow);
        }
        else
        {
            record.MarkFailed(dispatch.ErrorMessage ?? "Email provider rejected message.", dispatch.IsTransientFailure);
        }

        db.EmailDeliveries.Add(record);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (idemKey is not null && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            var existing = await db.EmailDeliveries.AsNoTracking().FirstAsync(e => e.IdempotencyKey == idemKey, ct);
            return Map(existing);
        }

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "platform.email.send",
            "EmailDeliveryRecord",
            record.Id.ToString("D"),
            tenantContext.TenantId,
            dispatch.Succeeded ? "Success" : "Failed",
            AfterState: new { record.Provider, record.ProviderMessageId, record.RecipientMasked, Status = record.Status.ToString() }), ct);

        return Map(record);
    }

    public async Task<Result<EmailProviderStatusDto>> GetProviderStatusAsync(CancellationToken ct = default)
    {
        var cfg = options.Value;
        var hasApiKey = !string.IsNullOrWhiteSpace(cfg.Resend.ApiKey) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RESEND_API_KEY"));
        var hasWebhookSecret = !string.IsNullOrWhiteSpace(cfg.Resend.WebhookSecret);

        var total = await db.EmailDeliveries.AsNoTracking().LongCountAsync(ct);
        var sentOrDelivered = await db.EmailDeliveries.AsNoTracking().LongCountAsync(
            e => e.Status == EmailDeliveryStatus.Sent || e.Status == EmailDeliveryStatus.Delivered, ct);
        var failed = await db.EmailDeliveries.AsNoTracking().LongCountAsync(
            e => e.Status == EmailDeliveryStatus.Failed || e.Status == EmailDeliveryStatus.Bounced, ct);
        var suppressed = await db.EmailSuppressions.AsNoTracking().LongCountAsync(s => s.IsActive, ct);

        return new EmailProviderStatusDto(
            emailSender.ProviderName,
            hasApiKey,
            hasWebhookSecret,
            cfg.DefaultFromAddress,
            cfg.DefaultFromName,
            total,
            sentOrDelivered,
            failed,
            suppressed);
    }

    public async Task<Result<PagedResult<EmailDeliveryDto>>> ListDeliveriesAsync(PagedRequest paging, string? status = null, CancellationToken ct = default)
    {
        var query = db.EmailDeliveries.AsNoTracking();
        if (!currentUser.IsPlatformUser)
        {
            if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
            var tid = tenantContext.TenantId!.Value;
            query = query.Where(e => e.TenantId == tid);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<EmailDeliveryStatus>(status, true, out var parsed))
        {
            query = query.Where(e => e.Status == parsed);
        }

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(e => e.CreatedAt).Skip(paging.Skip).Take(paging.PageSize).Select(e => Map(e)).ToListAsync(ct);
        return new PagedResult<EmailDeliveryDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<Result<IReadOnlyList<EmailSuppressionDto>>> ListSuppressionsAsync(CancellationToken ct = default)
    {
        var list = await db.EmailSuppressions.AsNoTracking()
            .OrderByDescending(s => s.SuppressedAt)
            .Take(200)
            .Select(s => new EmailSuppressionDto(s.Id, s.RecipientMasked, s.Reason, s.Provider, s.IsActive, s.SuppressedAt))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<ResendWebhookResultDto>> ProcessResendWebhookAsync(
        string rawBody,
        string? svixId,
        string? svixTimestamp,
        string? svixSignature,
        CancellationToken ct = default)
    {
        var secret = options.Value.Resend.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Error.BusinessRule("RESEND_WEBHOOK_NOT_CONFIGURED", "Resend webhook signing secret is not configured.");
        }

        var now = clock.UtcNow;
        if (!SvixSignatureVerifier.Verify(svixId, svixTimestamp, svixSignature, rawBody, secret, now, maxToleranceSeconds: 300))
        {
            return Error.Unauthorized("RESEND_WEBHOOK_INVALID_SIGNATURE", "Invalid Resend Svix webhook signature or expired timestamp.");
        }

        string eventType;
        string? providerMsgId = null;
        string? recipientEmail = null;

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            eventType = root.TryGetProperty("type", out var tp) ? tp.GetString() ?? string.Empty : string.Empty;
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("email_id", out var idEl)) providerMsgId = idEl.GetString();
                if (data.TryGetProperty("to", out var toEl) && toEl.ValueKind == JsonValueKind.Array && toEl.GetArrayLength() > 0)
                {
                    recipientEmail = toEl[0].GetString();
                }
            }
        }
        catch (JsonException)
        {
            return Error.Validation("RESEND_WEBHOOK_INVALID_JSON", "Resend webhook payload is not valid JSON.");
        }

        db.EmailWebhookEvents.Add(EmailWebhookEvent.Create(svixId!, eventType, providerMsgId, now));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return new ResendWebhookResultDto(svixId!, eventType, true, providerMsgId);
        }

        using var bypass = tenantContext.BeginScope(null, platformBypass: true, bypassReason: "resend-webhook-status-update");
        if (!string.IsNullOrWhiteSpace(providerMsgId))
        {
            var delivery = await db.EmailDeliveries.FirstOrDefaultAsync(e => e.ProviderMessageId == providerMsgId, ct);
            if (delivery is not null)
            {
                switch (eventType.ToLowerInvariant())
                {
                    case "email.delivered":
                        delivery.ApplyWebhookStatus(EmailDeliveryStatus.Delivered, now);
                        break;
                    case "email.bounced":
                        delivery.ApplyWebhookStatus(EmailDeliveryStatus.Bounced, now, "Provider reported permanent bounce.");
                        await UpsertSuppressionAsync(delivery.RecipientHash, delivery.RecipientMasked, "Bounce", now, ct);
                        break;
                    case "email.complained":
                        delivery.ApplyWebhookStatus(EmailDeliveryStatus.Complained, now, "Recipient marked email as spam/complaint.");
                        await UpsertSuppressionAsync(delivery.RecipientHash, delivery.RecipientMasked, "Complaint", now, ct);
                        break;
                    case "email.delivery_delayed":
                        delivery.ApplyWebhookStatus(EmailDeliveryStatus.Retrying, now, "Provider reported temporary delivery delay.");
                        break;
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(recipientEmail) && eventType is "email.bounced" or "email.complained")
        {
            var norm = recipientEmail.Trim().ToLowerInvariant();
            await UpsertSuppressionAsync(CryptoHelper.Sha256Hex(norm), LoggingEmailSender.MaskEmail(norm), eventType == "email.bounced" ? "Bounce" : "Complaint", now, ct);
        }

        await db.SaveChangesAsync(ct);
        return new ResendWebhookResultDto(svixId!, eventType, false, providerMsgId);
    }

    private async Task UpsertSuppressionAsync(string hash, string masked, string reason, DateTimeOffset now, CancellationToken ct)
    {
        var exists = await db.EmailSuppressions.AnyAsync(s => s.RecipientHash == hash, ct);
        if (!exists)
        {
            db.EmailSuppressions.Add(EmailSuppression.Create(hash, masked, reason, "Resend", now));
        }
    }

    private static EmailDeliveryDto Map(EmailDeliveryRecord e) => new(
        e.Id, e.TenantId, e.Provider, e.ProviderMessageId, e.TemplateKey, e.TemplateVersion,
        e.RecipientMasked, e.Subject, e.Status.ToString(), e.AttemptCount, e.IsTransientError,
        e.LastError, e.CorrelationId, e.CreatedAt, e.SentAt, e.DeliveredAt);
}
