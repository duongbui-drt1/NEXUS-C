using Microsoft.EntityFrameworkCore;
using Nexus.BuildingBlocks.Observability;
using Nexus.Modules.Workflow.Domain;
using Nexus.Modules.Workflow.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Events;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;
using Nexus.SharedKernel.Security;

namespace Nexus.Modules.Workflow.Application;

public sealed record CreateWorkflowDefinitionRequest(
    string Code,
    string Name,
    string ResourceType,
    bool RequireMakerChecker = true,
    int RequiredApprovals = 1,
    string? ApproverRoleName = null,
    int ExpirationHours = 72,
    ApprovalMode ApprovalMode = ApprovalMode.Sequential,
    string? ConditionExpressionJson = null,
    int? EscalationHours = null,
    string? EscalationRoleName = null);

public sealed record PublishWorkflowVersionRequest(
    int RequiredApprovals = 1,
    string? ApproverRoleName = null,
    int ExpirationHours = 72,
    bool RequireMakerChecker = true,
    ApprovalMode ApprovalMode = ApprovalMode.Sequential,
    string? ConditionExpressionJson = null,
    int? EscalationHours = null,
    string? EscalationRoleName = null);

public sealed record WorkflowDefinitionDto(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    string ResourceType,
    bool RequireMakerChecker,
    int CurrentVersionNumber,
    Guid LatestVersionId,
    bool IsActive = true,
    string ApprovalMode = "Sequential",
    int? EscalationHours = null,
    string? EscalationRoleName = null);

public sealed record CreateWorkflowInstanceRequest(
    Guid DefinitionId,
    string ResourceId,
    string? PayloadSummaryJson = null,
    bool SubmitImmediately = true);

public sealed record DecideApprovalTaskRequest(bool Approve, string? Comment = null);
public sealed record DelegateApprovalTaskRequest(Guid TargetUserId, string? Reason = null);
public sealed record CreateApprovalDelegationRequest(
    Guid DelegateUserId,
    Guid? WorkflowDefinitionId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    string? Reason = null);

public sealed record ApprovalDelegationDto(
    Guid Id,
    Guid TenantId,
    Guid DelegatorUserId,
    Guid DelegateUserId,
    Guid? WorkflowDefinitionId,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    bool IsActive,
    string? Reason);

public sealed record ApprovalTaskDto(
    Guid Id,
    Guid WorkflowInstanceId,
    int StepOrder,
    string? AssignedRoleName,
    string Status,
    Guid? DecidedByUserId,
    DateTimeOffset? DecidedAt,
    Guid? AssignedUserId = null,
    Guid? DelegatedFromUserId = null,
    DateTimeOffset? SlaDueAt = null,
    DateTimeOffset? EscalatedAt = null,
    string? EscalatedToRoleName = null);

public sealed record WorkflowHistoryDto(
    Guid Id,
    Guid WorkflowInstanceId,
    string FromStatus,
    string ToStatus,
    Guid? ActorUserId,
    string? Note,
    DateTimeOffset OccurredAt);

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
    IReadOnlyList<ApprovalTaskDto> Tasks,
    Guid? VersionId = null);

public interface IWorkflowService
{
    Task<Result<WorkflowDefinitionDto>> CreateDefinitionAsync(CreateWorkflowDefinitionRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<WorkflowDefinitionDto>>> ListDefinitionsAsync(CancellationToken ct = default);
    Task<Result<WorkflowDefinitionDto>> PublishVersionAsync(Guid definitionId, PublishWorkflowVersionRequest request, CancellationToken ct = default);
    Task<Result<WorkflowDefinitionDto>> SetDefinitionActiveAsync(Guid definitionId, bool isActive, CancellationToken ct = default);

    Task<Result<WorkflowInstanceDto>> CreateInstanceAsync(CreateWorkflowInstanceRequest request, CancellationToken ct = default);
    Task<Result<WorkflowInstanceDto>> GetInstanceAsync(Guid instanceId, CancellationToken ct = default);
    Task<Result<PagedResult<WorkflowInstanceDto>>> ListInstancesAsync(PagedRequest paging, string? status = null, CancellationToken ct = default);
    Task<Result<IReadOnlyList<WorkflowHistoryDto>>> GetHistoryAsync(Guid instanceId, CancellationToken ct = default);
    Task<Result<WorkflowInstanceDto>> DecideTaskAsync(Guid taskId, DecideApprovalTaskRequest request, CancellationToken ct = default);
    Task<Result<WorkflowInstanceDto>> DelegateTaskAsync(Guid taskId, DelegateApprovalTaskRequest request, CancellationToken ct = default);
    Task<Result<WorkflowInstanceDto>> EscalateInstanceAsync(Guid instanceId, CancellationToken ct = default);
    Task<Result<WorkflowInstanceDto>> CancelInstanceAsync(Guid instanceId, string? reason, CancellationToken ct = default);

    Task<Result<ApprovalDelegationDto>> CreateDelegationAsync(CreateApprovalDelegationRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ApprovalDelegationDto>>> ListDelegationsAsync(CancellationToken ct = default);
    Task<Result> RevokeDelegationAsync(Guid delegationId, CancellationToken ct = default);
}

public sealed class WorkflowService(
    WorkflowDbContext db,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    IPermissionEvaluator permissionEvaluator,
    IUserDirectoryReader userDirectory,
    ITenantAccessValidator tenantValidator,
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

        var version = WorkflowVersion.Create(
            tenantId, def.Id, 1,
            request.RequiredApprovals, request.ApproverRoleName, request.ExpirationHours,
            request.RequireMakerChecker, request.ApprovalMode, request.ConditionExpressionJson,
            request.EscalationHours, request.EscalationRoleName);

        db.Definitions.Add(def);
        db.Versions.Add(version);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "workflow.definition.create", "WorkflowDefinition", def.Id.ToString("D"), tenantId, "Success",
            AfterState: new { def.Code, def.Name, version.VersionNumber, version.RequiredApprovals, ApprovalMode = version.ApprovalMode.ToString() }), ct);

        return MapDefinition(def, version);
    }

    public async Task<Result<IReadOnlyList<WorkflowDefinitionDto>>> ListDefinitionsAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var defs = await db.Definitions.AsNoTracking().OrderBy(d => d.Code).ToListAsync(ct);
        var defIds = defs.Select(d => d.Id).ToList();
        var versions = await db.Versions.AsNoTracking()
            .Where(v => defIds.Contains(v.DefinitionId))
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);

        var latestByDef = versions.GroupBy(v => v.DefinitionId).ToDictionary(g => g.Key, g => g.First());
        var dtos = defs.Where(d => latestByDef.ContainsKey(d.Id))
            .Select(d => MapDefinition(d, latestByDef[d.Id]))
            .ToList();

        return dtos;
    }

    public async Task<Result<WorkflowDefinitionDto>> PublishVersionAsync(Guid definitionId, PublishWorkflowVersionRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var def = await db.Definitions.FirstOrDefaultAsync(d => d.Id == definitionId, ct);
        if (def is null) return Error.NotFound("WORKFLOW_DEF_NOT_FOUND", "Workflow definition not found.");

        var nextVerNum = def.IncrementVersion();
        var version = WorkflowVersion.Create(
            tenantId, def.Id, nextVerNum,
            request.RequiredApprovals, request.ApproverRoleName, request.ExpirationHours,
            request.RequireMakerChecker, request.ApprovalMode, request.ConditionExpressionJson,
            request.EscalationHours, request.EscalationRoleName);

        db.Versions.Add(version);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "workflow.definition.publish_version", "WorkflowVersion", version.Id.ToString("D"), tenantId, "Success",
            AfterState: new { def.Code, version.VersionNumber, version.RequiredApprovals, version.RequireMakerChecker, ApprovalMode = version.ApprovalMode.ToString() }), ct);

        return MapDefinition(def, version);
    }

    public async Task<Result<WorkflowDefinitionDto>> SetDefinitionActiveAsync(Guid definitionId, bool isActive, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var def = await db.Definitions.FirstOrDefaultAsync(d => d.Id == definitionId, ct);
        if (def is null) return Error.NotFound("WORKFLOW_DEF_NOT_FOUND", "Workflow definition not found.");

        if (isActive) def.Activate(); else def.Deactivate();
        await db.SaveChangesAsync(ct);

        var latest = await db.Versions.AsNoTracking()
            .Where(v => v.DefinitionId == def.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            isActive ? "workflow.definition.activate" : "workflow.definition.deactivate",
            "WorkflowDefinition", def.Id.ToString("D"), tenantId, "Success"), ct);

        return MapDefinition(def, latest);
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
        if (!def.IsActive) return Error.BusinessRule("WORKFLOW_DEF_INACTIVE", "Cannot create a workflow instance from an inactive definition.");

        var version = await db.Versions.AsNoTracking()
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(v => v.DefinitionId == def.Id, ct);
        if (version is null) return Error.NotFound("WORKFLOW_VERSION_NOT_FOUND", "Workflow version not found.");

        var now = clock.UtcNow;
        var instance = WorkflowInstance.Create(
            tenantId, def.Id, version.Id, def.ResourceType, request.ResourceId, currentUser.UserId.Value, request.PayloadSummaryJson);

        db.Instances.Add(instance);
        var tasks = new List<ApprovalTask>();

        if (request.SubmitImmediately)
        {
            var submitRes = instance.Submit(now, version.ExpirationHours);
            if (submitRes.IsFailure) return Result.Failure<WorkflowInstanceDto>(submitRes.Error);

            DateTimeOffset? slaDueAt = version.EscalationHours.HasValue ? now.AddHours(version.EscalationHours.Value) : null;

            for (var step = 1; step <= version.RequiredApprovals; step++)
            {
                var task = ApprovalTask.Create(tenantId, instance.Id, step, version.ApproverRoleName, slaDueAt);
                db.ApprovalTasks.Add(task);
                tasks.Add(task);
            }

            db.History.Add(WorkflowHistory.Record(tenantId, instance.Id, "Draft", "Pending", currentUser.UserId, $"Submitted on version v{version.VersionNumber}", now));
            db.StageOutboxEvent(EventEnvelope.Create(
                PlatformEventTypes.WorkflowSubmittedV1,
                tenantId,
                correlationContext.CorrelationId,
                new { instance.Id, def.Code, version.VersionNumber, instance.ResourceType, instance.ResourceId, instance.InitiatorUserId },
                occurredAt: now));
        }

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "workflow.instance.create", "WorkflowInstance", instance.Id.ToString("D"), tenantId, "Success",
            AfterState: new { instance.Id, def.Code, version.VersionNumber, instance.ResourceId, Status = instance.Status.ToString() }), ct);

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

    public async Task<Result<IReadOnlyList<WorkflowHistoryDto>>> GetHistoryAsync(Guid instanceId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var exists = await db.Instances.AsNoTracking().AnyAsync(i => i.Id == instanceId, ct);
        if (!exists) return Error.NotFound("WORKFLOW_INSTANCE_NOT_FOUND", "Workflow instance not found.");

        var history = await db.History.AsNoTracking()
            .Where(h => h.WorkflowInstanceId == instanceId)
            .OrderBy(h => h.OccurredAt)
            .Select(h => new WorkflowHistoryDto(h.Id, h.WorkflowInstanceId, h.FromStatus, h.ToStatus, h.ActorUserId, h.Note, h.OccurredAt))
            .ToListAsync(ct);

        return history;
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

        // Pin all governance rules strictly to the instance's original WorkflowVersion!
        var version = await db.Versions.AsNoTracking().FirstAsync(v => v.Id == instance.VersionId, ct);

        if (version.RequireMakerChecker && instance.InitiatorUserId == actorId)
        {
            await tx.RollbackAsync(ct);
            await auditWriter.WriteAsync(new AuditEntryRequest(
                "workflow.task.decide_denied", "ApprovalTask", task.Id.ToString("D"), tenantId, "Denied",
                Reason: "Maker-Checker violation: initiator cannot approve/reject own workflow."), ct);
            return Error.Forbidden("WORKFLOW_MAKER_CHECKER_VIOLATION", "Separation of duties: workflow initiator cannot approve or reject their own request.");
        }

        var now = clock.UtcNow;
        if (instance.ExpiresAt.HasValue && instance.ExpiresAt.Value <= now)
        {
            instance.MarkExpired(now);
            var expTasks = await db.ApprovalTasks.Where(t => t.WorkflowInstanceId == instance.Id).ToListAsync(ct);
            foreach (var et in expTasks) et.Expire();
            db.History.Add(WorkflowHistory.Record(tenantId, instance.Id, "Pending", "Expired", actorId, "Workflow expired before decision", now));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Error.BusinessRule("WORKFLOW_EXPIRED", "Workflow instance has expired.");
        }

        // Enforce sequential ordering when version.ApprovalMode == Sequential
        var allTasks = await db.ApprovalTasks.Where(t => t.WorkflowInstanceId == instance.Id).OrderBy(t => t.StepOrder).ToListAsync(ct);
        if (version.ApprovalMode == ApprovalMode.Sequential &&
            allTasks.Any(t => t.StepOrder < task.StepOrder && t.Status == ApprovalTaskStatus.Pending))
        {
            await tx.RollbackAsync(ct);
            return Error.BusinessRule("WORKFLOW_SEQUENTIAL_ORDER_VIOLATION", "Previous sequential approval step must be completed before deciding this task.");
        }

        // Enforce approver role or active unexpired delegation eligibility!
        var eligible = await IsActorEligibleForTaskAsync(actorId, tenantId, instance.DefinitionId, task, now, ct);
        if (!eligible)
        {
            await tx.RollbackAsync(ct);
            await auditWriter.WriteAsync(new AuditEntryRequest(
                "workflow.task.decide_denied", "ApprovalTask", task.Id.ToString("D"), tenantId, "Denied",
                Reason: "Actor does not hold the required approver role or an active valid delegation."), ct);
            return Error.Forbidden("WORKFLOW_APPROVER_NOT_ELIGIBLE", "You are not assigned to this approval task and hold no active valid delegation.");
        }

        var decideRes = task.Decide(request.Approve, actorId, now);
        if (decideRes.IsFailure)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure<WorkflowInstanceDto>(decideRes.Error);
        }

        db.ApprovalDecisions.Add(ApprovalDecision.Create(tenantId, task.Id, instance.Id, actorId, request.Approve, request.Comment, now));

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
        else
        {
            db.History.Add(WorkflowHistory.Record(tenantId, instance.Id, "Pending", "Pending", actorId, $"Step {task.StepOrder} approved: {request.Comment}", now));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        NexusPlatformMetrics.WorkflowDecisions.Add(1, new KeyValuePair<string, object?>("decision", request.Approve ? "approved" : "rejected"));

        await auditWriter.WriteAsync(new AuditEntryRequest(
            request.Approve ? "workflow.task.approve" : "workflow.task.reject",
            "ApprovalTask",
            task.Id.ToString("D"),
            tenantId,
            "Success",
            Reason: request.Comment,
            AfterState: new { instance.Id, WorkflowStatus = instance.Status.ToString(), TaskStatus = task.Status.ToString(), task.StepOrder }), ct);

        return Map(instance, allTasks);
    }

    public async Task<Result<WorkflowInstanceDto>> DelegateTaskAsync(Guid taskId, DelegateApprovalTaskRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || !currentUser.UserId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var tenantId = tenantContext.TenantId!.Value;
        var actorId = currentUser.UserId.Value;

        var task = await db.ApprovalTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null) return Error.NotFound("WORKFLOW_TASK_NOT_FOUND", "Approval task not found.");

        var instance = await db.Instances.FirstOrDefaultAsync(i => i.Id == task.WorkflowInstanceId, ct);
        if (instance is null) return Error.NotFound("WORKFLOW_INSTANCE_NOT_FOUND", "Workflow instance not found.");

        var version = await db.Versions.AsNoTracking().FirstAsync(v => v.Id == instance.VersionId, ct);
        if (version.RequireMakerChecker && request.TargetUserId == instance.InitiatorUserId)
        {
            return Error.Forbidden("WORKFLOW_MAKER_CHECKER_VIOLATION", "Cannot delegate an approval task to the workflow initiator.");
        }

        var now = clock.UtcNow;
        var isEligibleToDelegate = await IsActorEligibleForTaskAsync(actorId, tenantId, instance.DefinitionId, task, now, ct)
            || await permissionEvaluator.HasPermissionAsync(actorId, tenantId, Permissions.WorkflowsManage, currentUser.IsPlatformUser, ct);
        if (!isEligibleToDelegate)
        {
            return Error.Forbidden("WORKFLOW_DELEGATE_FORBIDDEN", "Only an eligible task approver or workflow manager may delegate this task.");
        }

        var targetUser = await userDirectory.GetByIdAsync(request.TargetUserId, ct);
        if (targetUser is null || !targetUser.IsActive)
        {
            return Error.NotFound("USER_NOT_FOUND", "Target delegate user was not found or is inactive.");
        }

        var membership = await tenantValidator.ValidateMembershipAsync(request.TargetUserId, tenantId, ct);
        if (membership is null || !membership.IsActiveMember)
        {
            return Error.Forbidden("USER_NOT_TENANT_MEMBER", "Delegate user must be an active member of the current tenant.");
        }

        var res = task.Delegate(request.TargetUserId, actorId);
        if (res.IsFailure) return Result.Failure<WorkflowInstanceDto>(res.Error);

        db.History.Add(WorkflowHistory.Record(tenantId, instance.Id, instance.Status.ToString(), instance.Status.ToString(), actorId,
            $"Task step {task.StepOrder} delegated to {request.TargetUserId:D}. {request.Reason}", now));

        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.WorkflowDelegatedV1,
            tenantId,
            correlationContext.CorrelationId,
            new { instance.Id, TaskId = task.Id, FromUserId = actorId, ToUserId = request.TargetUserId, request.Reason },
            occurredAt: now));

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "workflow.task.delegate", "ApprovalTask", task.Id.ToString("D"), tenantId, "Success",
            Reason: request.Reason,
            AfterState: new { task.Id, DelegatedTo = request.TargetUserId, DelegatedFrom = actorId }), ct);

        var allTasks = await db.ApprovalTasks.AsNoTracking().Where(t => t.WorkflowInstanceId == instance.Id).OrderBy(t => t.StepOrder).ToListAsync(ct);
        return Map(instance, allTasks);
    }

    public async Task<Result<WorkflowInstanceDto>> EscalateInstanceAsync(Guid instanceId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var instance = await db.Instances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null) return Error.NotFound("WORKFLOW_INSTANCE_NOT_FOUND", "Workflow instance not found.");
        if (instance.Status != WorkflowStatus.Pending)
        {
            return Error.BusinessRule("WORKFLOW_NOT_PENDING", "Only pending workflow instances can be escalated.");
        }

        var version = await db.Versions.AsNoTracking().FirstAsync(v => v.Id == instance.VersionId, ct);
        var tasks = await db.ApprovalTasks.Where(t => t.WorkflowInstanceId == instance.Id).OrderBy(t => t.StepOrder).ToListAsync(ct);
        var now = clock.UtcNow;

        var escalatedCount = 0;
        foreach (var task in tasks.Where(t => t.Status == ApprovalTaskStatus.Pending && !t.EscalatedAt.HasValue))
        {
            task.Escalate(version.EscalationRoleName, now);
            escalatedCount++;
        }

        if (escalatedCount > 0)
        {
            db.History.Add(WorkflowHistory.Record(tenantId, instance.Id, "Pending", "Pending", currentUser.UserId,
                $"Escalated {escalatedCount} pending task(s) to '{version.EscalationRoleName ?? "Supervisor"}'", now));
            db.StageOutboxEvent(EventEnvelope.Create(
                PlatformEventTypes.WorkflowEscalatedV1,
                tenantId,
                correlationContext.CorrelationId,
                new { instance.Id, EscalatedTasks = escalatedCount, EscalationRoleName = version.EscalationRoleName },
                occurredAt: now));

            await db.SaveChangesAsync(ct);

            await auditWriter.WriteAsync(new AuditEntryRequest(
                "workflow.instance.escalate", "WorkflowInstance", instance.Id.ToString("D"), tenantId, "Success",
                AfterState: new { instance.Id, EscalatedTasks = escalatedCount, version.EscalationRoleName }), ct);
        }

        return Map(instance, tasks);
    }

    public async Task<Result<WorkflowInstanceDto>> CancelInstanceAsync(Guid instanceId, string? reason, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var instance = await db.Instances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
        if (instance is null) return Error.NotFound("WORKFLOW_INSTANCE_NOT_FOUND", "Workflow instance not found.");

        var now = clock.UtcNow;
        var prev = instance.Status.ToString();
        var res = instance.Cancel(now);
        if (res.IsFailure) return Result.Failure<WorkflowInstanceDto>(res.Error);

        var tasks = await db.ApprovalTasks.Where(t => t.WorkflowInstanceId == instance.Id).ToListAsync(ct);
        foreach (var t in tasks) t.Cancel();

        db.History.Add(WorkflowHistory.Record(tenantId, instance.Id, prev, "Cancelled", currentUser.UserId, reason, now));
        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.WorkflowCancelledV1,
            tenantId,
            correlationContext.CorrelationId,
            new { instance.Id, instance.ResourceType, instance.ResourceId, ActorUserId = currentUser.UserId, Reason = reason },
            occurredAt: now));

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "workflow.instance.cancel", "WorkflowInstance", instance.Id.ToString("D"), tenantId, "Success",
            Reason: reason), ct);

        return Map(instance, tasks);
    }

    public async Task<Result<ApprovalDelegationDto>> CreateDelegationAsync(CreateApprovalDelegationRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || !currentUser.UserId.HasValue)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required.");
        }

        var tenantId = tenantContext.TenantId!.Value;
        var delegatorId = currentUser.UserId.Value;

        var delegateUser = await userDirectory.GetByIdAsync(request.DelegateUserId, ct);
        if (delegateUser is null || !delegateUser.IsActive)
        {
            return Error.NotFound("USER_NOT_FOUND", "Delegate user not found or inactive.");
        }

        var membership = await tenantValidator.ValidateMembershipAsync(request.DelegateUserId, tenantId, ct);
        if (membership is null || !membership.IsActiveMember)
        {
            return Error.Forbidden("USER_NOT_TENANT_MEMBER", "Delegate user must hold an active membership in this tenant.");
        }

        var created = ApprovalDelegation.Create(tenantId, delegatorId, request.DelegateUserId, request.WorkflowDefinitionId, request.ValidFrom, request.ValidTo, request.Reason);
        if (created.IsFailure) return Result.Failure<ApprovalDelegationDto>(created.Error);

        var delegation = created.Value;
        db.Delegations.Add(delegation);
        await db.SaveChangesAsync(ct);

        var dto = MapDelegation(delegation);
        await auditWriter.WriteAsync(new AuditEntryRequest(
            "workflow.delegation.create", "ApprovalDelegation", delegation.Id.ToString("D"), tenantId, "Success",
            AfterState: dto), ct);

        return dto;
    }

    public async Task<Result<IReadOnlyList<ApprovalDelegationDto>>> ListDelegationsAsync(CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var list = await db.Delegations.AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => MapDelegation(d))
            .ToListAsync(ct);

        return list;
    }

    public async Task<Result> RevokeDelegationAsync(Guid delegationId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant || !currentUser.UserId.HasValue)
        {
            return Result.Failure(Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required."));
        }
        var tenantId = tenantContext.TenantId!.Value;
        var actorId = currentUser.UserId.Value;

        var delegation = await db.Delegations.FirstOrDefaultAsync(d => d.Id == delegationId, ct);
        if (delegation is null) return Result.Failure(Error.NotFound("WORKFLOW_DELEGATION_NOT_FOUND", "Delegation not found."));

        if (delegation.DelegatorUserId != actorId)
        {
            var canManage = await permissionEvaluator.HasPermissionAsync(actorId, tenantId, Permissions.WorkflowsManage, currentUser.IsPlatformUser, ct);
            if (!canManage)
            {
                return Result.Failure(Error.Forbidden("WORKFLOW_DELEGATION_REVOKE_FORBIDDEN", "Only the delegator or a workflow manager may revoke this delegation."));
            }
        }

        delegation.Revoke();
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "workflow.delegation.revoke", "ApprovalDelegation", delegationId.ToString("D"), tenantId, "Success"), ct);

        return Result.Success();
    }

    private async Task<bool> IsActorEligibleForTaskAsync(
        Guid actorId,
        Guid tenantId,
        Guid definitionId,
        ApprovalTask task,
        DateTimeOffset now,
        CancellationToken ct)
    {
        // Case 1: Explicitly assigned to a specific user (e.g., via task delegation)
        if (task.AssignedUserId.HasValue)
        {
            if (task.AssignedUserId.Value == actorId)
            {
                return true;
            }

            // Or actor holds an active unexpired standing delegation from AssignedUserId
            return await db.Delegations.AsNoTracking().AnyAsync(d =>
                d.DelegatorUserId == task.AssignedUserId.Value &&
                d.DelegateUserId == actorId &&
                d.IsActive &&
                d.ValidFrom <= now &&
                d.ValidTo > now &&
                (d.WorkflowDefinitionId == null || d.WorkflowDefinitionId == definitionId), ct);
        }

        // Case 2: No specific role constraint configured on the workflow version -> any user with platform.workflows.approve is eligible
        if (string.IsNullOrWhiteSpace(task.AssignedRoleName) && string.IsNullOrWhiteSpace(task.EscalatedToRoleName))
        {
            return true;
        }

        // Case 3: Role-constrained task -> check actor's roles OR active unexpired delegation from a holder of that role
        var actorRoles = await permissionEvaluator.GetRoleNamesAsync(actorId, tenantId, isPlatformScope: false, ct);
        if ((!string.IsNullOrWhiteSpace(task.AssignedRoleName) && actorRoles.Contains(task.AssignedRoleName, StringComparer.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(task.EscalatedToRoleName) && actorRoles.Contains(task.EscalatedToRoleName, StringComparer.OrdinalIgnoreCase)))
        {
            return true;
        }

        var activeDelegators = await db.Delegations.AsNoTracking()
            .Where(d => d.DelegateUserId == actorId
                        && d.IsActive
                        && d.ValidFrom <= now
                        && d.ValidTo > now
                        && (d.WorkflowDefinitionId == null || d.WorkflowDefinitionId == definitionId))
            .Select(d => d.DelegatorUserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var delegatorId in activeDelegators)
        {
            var delegatorRoles = await permissionEvaluator.GetRoleNamesAsync(delegatorId, tenantId, isPlatformScope: false, ct);
            if ((!string.IsNullOrWhiteSpace(task.AssignedRoleName) && delegatorRoles.Contains(task.AssignedRoleName, StringComparer.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(task.EscalatedToRoleName) && delegatorRoles.Contains(task.EscalatedToRoleName, StringComparer.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static WorkflowDefinitionDto MapDefinition(WorkflowDefinition d, WorkflowVersion v) => new(
        d.Id, d.TenantId, d.Code, d.Name, d.ResourceType, v.RequireMakerChecker, d.CurrentVersionNumber,
        v.Id, d.IsActive, v.ApprovalMode.ToString(), v.EscalationHours, v.EscalationRoleName);

    private static WorkflowInstanceDto Map(WorkflowInstance i, IReadOnlyList<ApprovalTask> tasks) => new(
        i.Id, i.TenantId, i.DefinitionId, i.ResourceType, i.ResourceId, i.InitiatorUserId,
        i.Status.ToString(), i.SubmittedAt, i.CompletedAt, i.ExpiresAt,
        tasks.Select(t => new ApprovalTaskDto(
            t.Id, t.WorkflowInstanceId, t.StepOrder, t.AssignedRoleName, t.Status.ToString(),
            t.DecidedByUserId, t.DecidedAt, t.AssignedUserId, t.DelegatedFromUserId,
            t.SlaDueAt, t.EscalatedAt, t.EscalatedToRoleName)).ToList(),
        i.VersionId);

    private static ApprovalDelegationDto MapDelegation(ApprovalDelegation d) => new(
        d.Id, d.TenantId, d.DelegatorUserId, d.DelegateUserId, d.WorkflowDefinitionId,
        d.ValidFrom, d.ValidTo, d.IsActive, d.Reason);
}
