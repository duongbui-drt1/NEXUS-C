using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Workflow.Domain;

public enum WorkflowStatus
{
    Draft = 1,
    Pending = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
    Expired = 6
}

public enum ApprovalTaskStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Expired = 5
}

public sealed class WorkflowDefinition : TenantScopedEntity
{
    private WorkflowDefinition() { }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public bool RequireMakerChecker { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    public int CurrentVersionNumber { get; private set; } = 1;

    public static Result<WorkflowDefinition> Create(Guid tenantId, string code, string name, string resourceType, bool requireMakerChecker = true)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(resourceType))
        {
            return Error.Validation("WORKFLOW_INVALID_DEFINITION", "Code, name, and resource type are required.");
        }

        var def = new WorkflowDefinition
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            ResourceType = resourceType.Trim(),
            RequireMakerChecker = requireMakerChecker,
            IsActive = true,
            CurrentVersionNumber = 1
        };
        def.AssignTenant(tenantId);
        return def;
    }

    public int IncrementVersion() => ++CurrentVersionNumber;
}

public sealed class WorkflowVersion : TenantScopedEntity
{
    private WorkflowVersion() { }

    public Guid DefinitionId { get; private set; }
    public int VersionNumber { get; private set; }
    public int RequiredApprovals { get; private set; } = 1;
    public string? ApproverRoleName { get; private set; }
    public int ExpirationHours { get; private set; } = 72;

    public static WorkflowVersion Create(Guid tenantId, Guid definitionId, int versionNumber, int requiredApprovals, string? approverRoleName, int expirationHours)
    {
        var ver = new WorkflowVersion
        {
            Id = Guid.CreateVersion7(),
            DefinitionId = definitionId,
            VersionNumber = versionNumber,
            RequiredApprovals = Math.Max(1, requiredApprovals),
            ApproverRoleName = string.IsNullOrWhiteSpace(approverRoleName) ? null : approverRoleName.Trim(),
            ExpirationHours = Math.Clamp(expirationHours, 1, 720)
        };
        ver.AssignTenant(tenantId);
        return ver;
    }
}

public sealed class WorkflowInstance : TenantScopedEntity
{
    private WorkflowInstance() { }

    public Guid DefinitionId { get; private set; }
    public Guid VersionId { get; private set; }
    public string ResourceType { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public Guid InitiatorUserId { get; private set; }
    public WorkflowStatus Status { get; private set; } = WorkflowStatus.Draft;
    public string? PayloadSummaryJson { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }

    public static WorkflowInstance Create(
        Guid tenantId,
        Guid definitionId,
        Guid versionId,
        string resourceType,
        string resourceId,
        Guid initiatorUserId,
        string? payloadSummaryJson)
    {
        var inst = new WorkflowInstance
        {
            Id = Guid.CreateVersion7(),
            DefinitionId = definitionId,
            VersionId = versionId,
            ResourceType = resourceType.Trim(),
            ResourceId = resourceId.Trim(),
            InitiatorUserId = initiatorUserId,
            Status = WorkflowStatus.Draft,
            PayloadSummaryJson = payloadSummaryJson
        };
        inst.AssignTenant(tenantId);
        return inst;
    }

    public Result Submit(DateTimeOffset utcNow, int expirationHours)
    {
        if (Status != WorkflowStatus.Draft)
        {
            return Result.Failure(Error.BusinessRule("WORKFLOW_INVALID_TRANSITION", $"Cannot submit workflow in status '{Status}'."));
        }

        Status = WorkflowStatus.Pending;
        SubmittedAt = utcNow;
        ExpiresAt = utcNow.AddHours(expirationHours);
        return Result.Success();
    }

    public Result MarkApproved(DateTimeOffset utcNow)
    {
        if (Status != WorkflowStatus.Pending)
        {
            return Result.Failure(Error.BusinessRule("WORKFLOW_INVALID_TRANSITION", $"Cannot approve workflow in status '{Status}'."));
        }

        Status = WorkflowStatus.Approved;
        CompletedAt = utcNow;
        return Result.Success();
    }

    public Result MarkRejected(DateTimeOffset utcNow)
    {
        if (Status != WorkflowStatus.Pending)
        {
            return Result.Failure(Error.BusinessRule("WORKFLOW_INVALID_TRANSITION", $"Cannot reject workflow in status '{Status}'."));
        }

        Status = WorkflowStatus.Rejected;
        CompletedAt = utcNow;
        return Result.Success();
    }

    public Result Cancel(DateTimeOffset utcNow)
    {
        if (Status is not (WorkflowStatus.Draft or WorkflowStatus.Pending))
        {
            return Result.Failure(Error.BusinessRule("WORKFLOW_INVALID_TRANSITION", $"Cannot cancel workflow in status '{Status}'."));
        }

        Status = WorkflowStatus.Cancelled;
        CompletedAt = utcNow;
        return Result.Success();
    }

    public Result MarkExpired(DateTimeOffset utcNow)
    {
        if (Status != WorkflowStatus.Pending)
        {
            return Result.Failure(Error.BusinessRule("WORKFLOW_INVALID_TRANSITION", $"Cannot expire workflow in status '{Status}'."));
        }

        Status = WorkflowStatus.Expired;
        CompletedAt = utcNow;
        return Result.Success();
    }
}

public sealed class ApprovalTask : TenantScopedEntity
{
    private ApprovalTask() { }

    public Guid WorkflowInstanceId { get; private set; }
    public int StepOrder { get; private set; }
    public string? AssignedRoleName { get; private set; }
    public ApprovalTaskStatus Status { get; private set; } = ApprovalTaskStatus.Pending;
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? DecidedByUserId { get; private set; }

    public static ApprovalTask Create(Guid tenantId, Guid instanceId, int stepOrder, string? assignedRoleName)
    {
        var task = new ApprovalTask
        {
            Id = Guid.CreateVersion7(),
            WorkflowInstanceId = instanceId,
            StepOrder = stepOrder,
            AssignedRoleName = assignedRoleName,
            Status = ApprovalTaskStatus.Pending
        };
        task.AssignTenant(tenantId);
        return task;
    }

    public Result Decide(bool approve, Guid actorUserId, DateTimeOffset utcNow)
    {
        if (Status != ApprovalTaskStatus.Pending)
        {
            return Result.Failure(Error.Conflict("WORKFLOW_TASK_ALREADY_DECIDED", $"Approval task is already in status '{Status}'."));
        }

        Status = approve ? ApprovalTaskStatus.Approved : ApprovalTaskStatus.Rejected;
        DecidedByUserId = actorUserId;
        DecidedAt = utcNow;
        return Result.Success();
    }

    public void Cancel()
    {
        if (Status == ApprovalTaskStatus.Pending)
        {
            Status = ApprovalTaskStatus.Cancelled;
        }
    }
}

public sealed class ApprovalDecision : TenantScopedEntity
{
    private ApprovalDecision() { }

    public Guid ApprovalTaskId { get; private set; }
    public Guid WorkflowInstanceId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public bool Approved { get; private set; }
    public string? Comment { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }

    public static ApprovalDecision Create(Guid tenantId, Guid taskId, Guid instanceId, Guid actorUserId, bool approved, string? comment, DateTimeOffset utcNow)
    {
        var d = new ApprovalDecision
        {
            Id = Guid.CreateVersion7(),
            ApprovalTaskId = taskId,
            WorkflowInstanceId = instanceId,
            ActorUserId = actorUserId,
            Approved = approved,
            Comment = comment?.Length > 1000 ? comment[..1000] : comment,
            DecidedAt = utcNow
        };
        d.AssignTenant(tenantId);
        return d;
    }
}

public sealed class WorkflowHistory : TenantScopedEntity
{
    private WorkflowHistory() { }

    public Guid WorkflowInstanceId { get; private set; }
    public string FromStatus { get; private set; } = string.Empty;
    public string ToStatus { get; private set; } = string.Empty;
    public Guid? ActorUserId { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }

    public static WorkflowHistory Record(Guid tenantId, Guid instanceId, string fromStatus, string toStatus, Guid? actorUserId, string? note, DateTimeOffset utcNow)
    {
        var h = new WorkflowHistory
        {
            Id = Guid.CreateVersion7(),
            WorkflowInstanceId = instanceId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            ActorUserId = actorUserId,
            Note = note,
            OccurredAt = utcNow
        };
        h.AssignTenant(tenantId);
        return h;
    }
}
