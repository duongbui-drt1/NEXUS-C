namespace Nexus.SharedKernel.Primitives;

public interface ITenantScoped
{
    Guid TenantId { get; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAt { get; }
    Guid? DeletedBy { get; }
}

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}

public abstract class AuditableEntity : Entity
{
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public Guid? CreatedBy { get; protected set; }
    public Guid? UpdatedBy { get; protected set; }
    public uint RowVersion { get; protected set; }

    public void SetCreatedAudit(Guid? actorId, DateTimeOffset utcNow)
    {
        CreatedAt = utcNow;
        UpdatedAt = utcNow;
        CreatedBy = actorId;
        UpdatedBy = actorId;
    }

    public void SetUpdatedAudit(Guid? actorId, DateTimeOffset utcNow)
    {
        UpdatedAt = utcNow;
        UpdatedBy = actorId;
    }
}

public abstract class TenantScopedEntity : AuditableEntity, ITenantScoped
{
    public Guid TenantId { get; protected set; }

    public void AssignTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
        }

        if (TenantId != Guid.Empty && TenantId != tenantId)
        {
            throw new InvalidOperationException("Cannot reassign an entity to a different tenant.");
        }

        TenantId = tenantId;
    }
}
