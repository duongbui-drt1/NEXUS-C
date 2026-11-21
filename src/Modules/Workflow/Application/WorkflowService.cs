using Microsoft.EntityFrameworkCore;
using Nexus.Modules.Workflow.Domain;
using Nexus.Modules.Workflow.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Events;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Workflow.Application;

public sealed record CreateWorkflowDefinitionRequest(
    string Code,
    string Name,
    string ResourceType,
    bool RequireMakerChecker = true,
    int RequiredApprovals = 1,
    string? ApproverRoleName = null,
    int ExpirationHours = 72);

public sealed record WorkflowDefinitionDto(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    string ResourceType,
    bool RequireMakerChecker,
    int CurrentVersionNumber,
    Guid LatestVersionId);

public sealed record CreateWorkflowInstanceRequest(
    Guid DefinitionId,
    string ResourceId,
    string? PayloadSummaryJson = null,
    bool SubmitImmediately = true);

public sealed record DecideApprovalTaskRequest(bool Approve, string? Comment = null);

public sealed record ApprovalTaskDto(
    Guid Id,
    Guid WorkflowInstanceId,
    int StepOrder,
    string? AssignedRoleName,
    string Status,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt);

public sealed record WorkflowInstanceDto(
    Guid Id,
    Guid TenantId,
    Guid DefinitionId,
    string ResourceType,
    string ResourceId,
    Guid InitiatorUserId,
    string Status,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? ExpiresAt,
    IReadOnlyList<ApprovalTaskDto> Tasks);

public interface IWorkflowService
{
    Task<Result<WorkflowDefinitionDto>> CreateDefinitionAsync(CreateWorkflowDefinitionRequest request, CancellationToken ct = default);
    Task<Result<WorkflowInstanceDto>> CreateInstanceAsync(CreateWorkflowInstanceRequest request, CancellationToken ct = default);
    Task<Result<WorkflowInstanceDto>> GetInstanceAsync(Guid instanceId, CancellationToken ct = default);
    Task<Result<PagedResult<WorkflowInstanceDto>>> ListInstancesAsync(PagedRequest paging, string? status = null, CancellationToken ct = default);
    Task<Result<WorkflowInstanceDto>> DecideTaskAsync(Guid taskId, DecideApprovalTaskRequest request, CancellationToken ct = default);
    Task<Result<WorkflowInstanceDto>> CancelInstanceAsync(Guid instanceId, string? reason, CancellationToken ct = default);
}

public sealed class WorkflowService(
    WorkflowDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ICorrelationContext correlationContext,
    ISystemClock clock,
    IAuditWriter auditWriter) : IWorkflowService
{
    public async Task<Result<WorkflowDefinitionDto>> CreateDefinitionAsync(CreateWorkflowDefinitionRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var created = WorkflowDefinition.Create(tenantId, request.Code, request.Name, request.ResourceType, request.RequireMakerChecker);
        if (created.IsFailure) return Result.Failure<WorkflowDefinitionDto>(created.Error);

        var def = created.Value;
        if (await db.Definitions.AsNoTracking().AnyAsync(d => d.Code == def.Code, ct))
        {
            return Error.Conflict("WORKFLOW_DEF_DUPLICATE", $"Workflow definition '{def.Code}' already exists.");
        }

        var version = WorkflowVersion.Create(tenantId, def.Id, 1, request.RequiredApprovals, request.ApproverRoleName, request.ExpirationHours);

        db.Definitions.Add(def);
        db.Versions.Add(version);
        await db.SaveChangesAsync(ct);

        return new WorkflowDefinitionDto(def.Id, def.TenantId, def.Code, def.Name, def.ResourceType, def.RequireMakerChecker, def.CurrentVersionNumber, version.Id);
    }

    public async Task<Result<WorkflowInstanceDto>> CreateInstanceAsync(CreateWorkflowInstanceRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || !currentUser.UserId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var tenantId = tenantContext.TenantId!.Value;
        var def = await db.Definitions.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DefinitionId, ct);
        if (def is null) return Error.NotFound("WORKFLOW_DEF_NOT_FOUND", "Workflow definition not found.");

        var version = await db.Versions.AsNoTracking()
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(v => v.DefinitionId == def.Id, ct);
        if (version is null) return Error.NotFound("WORKFLOW_VERSION_NOT_FOUND", "Workflow version not found.");

        var instance = WorkflowInstance.Create(
            tenantId, def.Id, version.Id, def.ResourceType, request.ResourceId, currentUser.UserId.Value, request.PayloadSummaryJson);

        db.Instances.Add(instance);
        var tasks = new List<ApprovalTask>();

        if (request.SubmitImmediately)
        {
            var submitRes = instance.Submit(clock.UtcNow, version.ExpirationHours);
            if (submitRes.IsFailure) return Result.Failure<WorkflowInstanceDto>(submitRes.Error);

            for (var step = 1; step <= version.RequiredApprovals; step++)
            {
                var task = ApprovalTask.Create(tenantId, instance.Id, step, version.ApproverRoleName);
                db.ApprovalTasks.Add(task);
                tasks.Add(task);
            }

            db.History.Add(WorkflowHistory.Record(tenantId, instance.Id, "Draft", "Pending", currentUser.UserId, "Submitted for approval", clock.UtcNow));
        }

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "workflow.instance.create", "WorkflowInstance", instance.Id.ToString("D"), tenantId, "Success",
            AfterState: new { instance.Id, def.Code, instance.ResourceId, Status = instance.Status.ToString() }), ct);

        return Map(instance, tasks);
    }

    public async Task<Result<WorkflowInstanceDto>> GetInstanceAsync(Guid instanceId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var instance = await db.Instances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null) return Error.NotFound("WORKFLOW_INSTANCE_NOT_FOUND", "Workflow instance not found.");

        var tasks = await db.ApprovalTasks.AsNoTracking()
            .Where(t => t.WorkflowInstanceId == instance.Id)
            .OrderBy(t => t.StepOrder)
            .ToListAsync(ct);

        return Map(instance, tasks);
    }

    public async Task<Result<PagedResult<WorkflowInstanceDto>>> ListInstancesAsync(PagedRequest paging, string? status = null, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var query = db.Instances.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<WorkflowStatus>(status, true, out var parsed))
        {
            query = query.Where(i => i.Status == parsed);
        }

        var total = await query.LongCountAsync(ct);
        var instances = await query.OrderByDescending(i => i.CreatedAt).Skip(paging.Skip).Take(paging.PageSize).ToListAsync(ct);
        var ids = instances.Select(i => i.Id).ToList();
        var tasks = await db.ApprovalTasks.AsNoTracking().Where(t => ids.Contains(t.WorkflowInstanceId)).ToListAsync(ct);

        var dtos = instances.Select(i => Map(i, tasks.Where(t => t.WorkflowInstanceId == i.Id).OrderBy(t => t.StepOrder).ToList())).ToList();
        return new PagedResult<WorkflowInstanceDto>(dtos, paging.Page, paging.PageSize, total);
    }

    public async Task<Result<WorkflowInstanceDto>> DecideTaskAsync(Guid taskId, DecideApprovalTaskRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || !currentUser.UserId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var tenantId = tenantContext.TenantId!.Value;
        var actorId = currentUser.UserId.Value;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Lock approval task row FOR UPDATE so two concurrent approvers cannot both approve the same task!
        // IgnoreQueryFilters avoids EF Core subquery wrapping around FOR UPDATE; PostgreSQL RLS + explicit tenantId check enforce isolation.
        var task = (await db.ApprovalTasks
            .FromSqlInterpolated($"SELECT *, xmin FROM workflow.approval_tasks WHERE \"Id\" = {taskId} FOR UPDATE")
            .IgnoreQueryFilters()
            .ToListAsync(ct))
            .FirstOrDefault();

        if (task is null || task.TenantId != tenantId)
        {
            await tx.RollbackAsync(ct);
            return Error.NotFound("WORKFLOW_TASK_NOT_FOUND", "Approval task not found.");
        }

        var instance = await db.Instances.FirstOrDefaultAsync(i => i.Id == task.WorkflowInstanceId, ct);
        if (instance is null)
        {
            await tx.RollbackAsync(ct);
            return Error.NotFound("WORKFLOW_INSTANCE_NOT_FOUND", "Workflow instance not found.");
        }

        var def = await db.Definitions.AsNoTracking().FirstAsync(d => d.Id == instance.DefinitionId, ct);

        // Maker-Checker Separation of Duties enforcement!
        if (def.RequireMakerChecker && instance.InitiatorUserId == actorId)
        {
            await tx.RollbackAsync(ct);
            return Error.Forbidden("WORKFLOW_MAKER_CHECKER_VIOLATION", "Separation of duties: workflow initiator cannot approve or reject their own request.");
        }

        var now = clock.UtcNow;
        if (instance.ExpiresAt.HasValue && instance.ExpiresAt.Value <= now)
        {
            instance.MarkExpired(now);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Error.BusinessRule("WORKFLOW_EXPIRED", "Workflow instance has expired.");
        }

        var decideRes = task.Decide(request.Approve, actorId, now);
        if (decideRes.IsFailure)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure<WorkflowInstanceDto>(decideRes.Error);
        }

        db.ApprovalDecisions.Add(ApprovalDecision.Create(tenantId, task.Id, instance.Id, actorId, request.Approve, request.Comment, now));

        var allTasks = await db.ApprovalTasks.Where(t => t.WorkflowInstanceId == instance.Id).OrderBy(t => t.StepOrder).ToListAsync(ct);

        if (!request.Approve)
        {
            var prev = instance.Status.ToString();
            instance.MarkRejected(now);
            foreach (var pending in allTasks.Where(t => t.Id != task.Id && t.Status == ApprovalTaskStatus.Pending))
            {
                pending.Cancel();
            }

            db.History.Add(WorkflowHistory.Record(tenantId, instance.Id, prev, "Rejected", actorId, request.Comment, now));
            db.StageOutboxEvent(EventEnvelope.Create(
                PlatformEventTypes.WorkflowRejectedV1,
                tenantId,
                correlationContext.CorrelationId,
                new { instance.Id, instance.ResourceType, instance.ResourceId, ActorUserId = actorId, request.Comment },
                occurredAt: now));
        }
        else if (allTasks.All(t => t.Status == ApprovalTaskStatus.Approved))
        {
            var prev = instance.Status.ToString();
            instance.MarkApproved(now);
            db.History.Add(WorkflowHistory.Record(tenantId, instance.Id, prev, "Approved", actorId, request.Comment, now));
            db.StageOutboxEvent(EventEnvelope.Create(
                PlatformEventTypes.WorkflowApprovedV1,
                tenantId,
                correlationContext.CorrelationId,
                new { instance.Id, instance.ResourceType, instance.ResourceId, ActorUserId = actorId },
                occurredAt: now));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            request.Approve ? "workflow.task.approve" : "workflow.task.reject",
            "ApprovalTask",
            task.Id.ToString("D"),
            tenantId,
            "Success",
            Reason: request.Comment,
            AfterState: new { instance.Id, WorkflowStatus = instance.Status.ToString(), TaskStatus = task.Status.ToString() }), ct);

        return Map(instance, allTasks);
    }

    public async Task<Result<WorkflowInstanceDto>> CancelInstanceAsync(Guid instanceId, string? reason, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var instance = await db.Instances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null) return Error.NotFound("WORKFLOW_INSTANCE_NOT_FOUND", "Workflow instance not found.");

        var prev = instance.Status.ToString();
        var res = instance.Cancel(clock.UtcNow);
        if (res.IsFailure) return Result.Failure<WorkflowInstanceDto>(res.Error);

        var tasks = await db.ApprovalTasks.Where(t => t.WorkflowInstanceId == instance.Id).ToListAsync(ct);
        foreach (var t in tasks) t.Cancel();

        db.History.Add(WorkflowHistory.Record(tenantId, instance.Id, prev, "Cancelled", currentUser.UserId, reason, clock.UtcNow));
        await db.SaveChangesAsync(ct);

        return Map(instance, tasks);
    }

    private static WorkflowInstanceDto Map(WorkflowInstance i, IReadOnlyList<ApprovalTask> tasks) => new(
        i.Id, i.TenantId, i.DefinitionId, i.ResourceType, i.ResourceId, i.InitiatorUserId,
        i.Status.ToString(), i.SubmittedAt, i.CompletedAt, i.ExpiresAt,
        tasks.Select(t => new ApprovalTaskDto(t.Id, t.WorkflowInstanceId, t.StepOrder, t.AssignedRoleName, t.Status.ToString(), t.DecidedByUserId, t.DecidedAt)).ToList());
}
