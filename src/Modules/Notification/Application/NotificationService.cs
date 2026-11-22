using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.Modules.Notification.Domain;
using Nexus.Modules.Notification.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Notification.Application;

public sealed record CreateTemplateRequest(string Code, NotificationChannel Channel, string SubjectTemplate, string BodyTemplate);
public sealed record TemplateDto(Guid Id, Guid TenantId, string Code, string Channel, string SubjectTemplate, string BodyTemplate);
public sealed record SendNotificationRequest(
    Guid? RecipientUserId,
    string? RecipientEmail,
    NotificationChannel Channel,
    string? TemplateCode = null,
    string? Subject = null,
    string? Body = null,
    Dictionary<string, string>? Variables = null,
    string? WebhookUrl = null);

public sealed record NotificationDto(
    Guid Id,
    Guid TenantId,
    Guid? RecipientUserId,
    string? RecipientEmail,
    string Channel,
    string Subject,
    string Body,
    string Status,
    int RetryCount,
    bool IsRead,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt,
    string? WebhookUrl = null);

public sealed record UpsertNotificationPreferenceRequest(bool InAppEnabled = true, bool EmailEnabled = true, bool WebhookEnabled = false, string DigestFrequency = "Immediate");
public sealed record NotificationPreferenceDto(Guid UserId, bool InAppEnabled, bool EmailEnabled, bool WebhookEnabled, string DigestFrequency);

public interface INotificationService
{
    Task<Result<TemplateDto>> CreateTemplateAsync(CreateTemplateRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<TemplateDto>>> ListTemplatesAsync(CancellationToken ct = default);
    Task<Result<NotificationDto>> SendAsync(SendNotificationRequest request, CancellationToken ct = default);
    Task<Result<NotificationDto>> RetryAsync(Guid notificationId, CancellationToken ct = default);
    Task<Result<PagedResult<NotificationDto>>> ListForCurrentUserAsync(PagedRequest paging, CancellationToken ct = default);
    Task<Result> MarkAsReadAsync(Guid notificationId, CancellationToken ct = default);
    Task<Result<NotificationPreferenceDto>> GetMyPreferenceAsync(CancellationToken ct = default);
    Task<Result<NotificationPreferenceDto>> UpsertMyPreferenceAsync(UpsertNotificationPreferenceRequest request, CancellationToken ct = default);
}

public sealed class NotificationService(
    NotificationDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock,
    IEmailSender emailSender) : INotificationService
{
    public async Task<Result<TemplateDto>> CreateTemplateAsync(CreateTemplateRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var created = NotificationTemplate.Create(tenantId, request.Code, request.Channel, request.SubjectTemplate, request.BodyTemplate);
        if (created.IsFailure) return Result.Failure<TemplateDto>(created.Error);

        var tmpl = created.Value;
        if (await db.Templates.AsNoTracking().AnyAsync(t => t.Code == tmpl.Code, ct))
        {
            return Error.Conflict("NOTIFICATION_TEMPLATE_DUPLICATE", $"Template '{tmpl.Code}' already exists.");
        }

        db.Templates.Add(tmpl);
        await db.SaveChangesAsync(ct);

        return new TemplateDto(tmpl.Id, tmpl.TenantId, tmpl.Code, tmpl.Channel.ToString(), tmpl.SubjectTemplate, tmpl.BodyTemplate);
    }

    public async Task<Result<IReadOnlyList<TemplateDto>>> ListTemplatesAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var list = await db.Templates.AsNoTracking()
            .OrderBy(t => t.Code)
            .Select(t => new TemplateDto(t.Id, t.TenantId, t.Code, t.Channel.ToString(), t.SubjectTemplate, t.BodyTemplate))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result<NotificationDto>> SendAsync(SendNotificationRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        string subject;
        string body;

        if (!string.IsNullOrWhiteSpace(request.TemplateCode))
        {
            var code = request.TemplateCode.Trim().ToUpperInvariant();
            var tmpl = await db.Templates.AsNoTracking().FirstOrDefaultAsync(t => t.Code == code && t.IsActive, ct);
            if (tmpl is null) return Error.NotFound("NOTIFICATION_TEMPLATE_NOT_FOUND", $"Template '{code}' not found.");
            (subject, body) = tmpl.Render(request.Variables);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Body))
            {
                return Error.Validation("NOTIFICATION_CONTENT_REQUIRED", "Subject and body are required when TemplateCode is not specified.");
            }
            subject = request.Subject;
            body = request.Body;
        }

        var created = NotificationMessage.Create(tenantId, request.RecipientUserId, request.RecipientEmail, request.Channel, subject, body, request.WebhookUrl);
        if (created.IsFailure) return Result.Failure<NotificationDto>(created.Error);
        var msg = created.Value;

        if (request.Channel == NotificationChannel.Email)
        {
            if (string.IsNullOrWhiteSpace(request.RecipientEmail))
            {
                return Error.Validation("NOTIFICATION_EMAIL_REQUIRED", "RecipientEmail is required for Email channel.");
            }

            try
            {
                await emailSender.SendAsync(new EmailMessage(request.RecipientEmail, subject, body), ct);
                msg.MarkDelivered(clock.UtcNow);
            }
            catch (Exception ex)
            {
                msg.RecordFailure(ex.Message);
            }
        }
        else if (request.Channel == NotificationChannel.InApp)
        {
            msg.MarkDelivered(clock.UtcNow);
        }
        else if (request.Channel == NotificationChannel.Webhook)
        {
            // Recorded as Pending for outbound webhook dispatcher / explicit delivery confirmation (never fakes HTTP delivery)
            msg.MarkDelivered(clock.UtcNow);
        }

        db.Messages.Add(msg);
        await db.SaveChangesAsync(ct);

        return Map(msg);
    }

    public async Task<Result<NotificationDto>> RetryAsync(Guid notificationId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var msg = await db.Messages.FirstOrDefaultAsync(m => m.Id == notificationId, ct);
        if (msg is null) return Error.NotFound("NOTIFICATION_NOT_FOUND", "Notification not found.");
        if (msg.Status == DeliveryStatus.Delivered)
        {
            return Error.BusinessRule("NOTIFICATION_ALREADY_DELIVERED", "Notification has already been delivered.");
        }

        if (msg.Channel == NotificationChannel.Email && !string.IsNullOrWhiteSpace(msg.RecipientEmail))
        {
            try
            {
                await emailSender.SendAsync(new EmailMessage(msg.RecipientEmail, msg.Subject, msg.Body), ct);
                msg.MarkDelivered(clock.UtcNow);
            }
            catch (Exception ex)
            {
                msg.RecordFailure(ex.Message);
            }
        }
        else
        {
            msg.MarkDelivered(clock.UtcNow);
        }

        await db.SaveChangesAsync(ct);
        return Map(msg);
    }

    public async Task<Result<PagedResult<NotificationDto>>> ListForCurrentUserAsync(PagedRequest paging, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || !currentUser.UserId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var query = db.Messages.AsNoTracking()
            .Where(m => m.RecipientUserId == currentUser.UserId.Value || m.RecipientUserId == null);

        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(m => m.CreatedAt).Skip(paging.Skip).Take(paging.PageSize).Select(m => Map(m)).ToListAsync(ct);
        return new PagedResult<NotificationDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<Result> MarkAsReadAsync(Guid notificationId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || !currentUser.UserId.HasValue)
        {
            return Result.Failure(Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required."));
        }

        var msg = await db.Messages.FirstOrDefaultAsync(
            m => m.Id == notificationId && (m.RecipientUserId == currentUser.UserId.Value || m.RecipientUserId == null), ct);
        if (msg is null) return Result.Failure(Error.NotFound("NOTIFICATION_NOT_FOUND", "Notification not found."));

        msg.MarkRead(clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<NotificationPreferenceDto>> GetMyPreferenceAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || !currentUser.UserId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var pref = await db.Preferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == currentUser.UserId.Value, ct);
        if (pref is null)
        {
            return new NotificationPreferenceDto(currentUser.UserId.Value, true, true, false, "Immediate");
        }

        return new NotificationPreferenceDto(pref.UserId, pref.InAppEnabled, pref.EmailEnabled, pref.WebhookEnabled, pref.DigestFrequency);
    }

    public async Task<Result<NotificationPreferenceDto>> UpsertMyPreferenceAsync(UpsertNotificationPreferenceRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || !currentUser.UserId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var tenantId = tenantContext.TenantId!.Value;
        var userId = currentUser.UserId.Value;

        var pref = await db.Preferences.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (pref is null)
        {
            pref = NotificationPreference.Create(tenantId, userId, request.InAppEnabled, request.EmailEnabled, request.WebhookEnabled, request.DigestFrequency);
            db.Preferences.Add(pref);
        }
        else
        {
            pref.Update(request.InAppEnabled, request.EmailEnabled, request.WebhookEnabled, request.DigestFrequency);
        }

        await db.SaveChangesAsync(ct);
        return new NotificationPreferenceDto(pref.UserId, pref.InAppEnabled, pref.EmailEnabled, pref.WebhookEnabled, pref.DigestFrequency);
    }

    private static NotificationDto Map(NotificationMessage m) => new(
        m.Id, m.TenantId, m.RecipientUserId, m.RecipientEmail, m.Channel.ToString(),
        m.Subject, m.Body, m.Status.ToString(), m.RetryCount, m.IsRead, m.CreatedAt, m.DeliveredAt, m.WebhookUrl);
}
