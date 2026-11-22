using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Organization.Domain;

public enum OrganizationStatus
{
    Active = 1,
    Inactive = 2
}

public enum OrganizationUnitType
{
    Division = 1,
    Department = 2,
    Team = 3,
    CostCenter = 4,
    Warehouse = 5
}

public sealed class LegalEntity : TenantScopedEntity
{
    private LegalEntity() { }

    public string Code { get; private set; } = string.Empty;
    public string LegalName { get; private set; } = string.Empty;
    public string? TaxIdentificationNumber { get; private set; }
    public string? Address { get; private set; }
    public OrganizationStatus Status { get; private set; } = OrganizationStatus.Active;

    public static Result<LegalEntity> Create(Guid tenantId, string code, string legalName, string? tin, string? address)
    {
        if (tenantId == Guid.Empty)
        {
            return Error.Validation("ORG_TENANT_REQUIRED", "TenantId is required.");
        }

        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32)
        {
            return Error.Validation("ORG_INVALID_CODE", "Legal entity code is required (max 32 chars).");
        }

        if (string.IsNullOrWhiteSpace(legalName) || legalName.Trim().Length > 250)
        {
            return Error.Validation("ORG_INVALID_NAME", "Legal entity name is required (max 250 chars).");
        }

        var entity = new LegalEntity
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            LegalName = legalName.Trim(),
            TaxIdentificationNumber = string.IsNullOrWhiteSpace(tin) ? null : tin.Trim(),
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            Status = OrganizationStatus.Active
        };
        entity.AssignTenant(tenantId);
        return entity;
    }

    public Result Update(string legalName, string? tin, string? address, OrganizationStatus status)
    {
        if (string.IsNullOrWhiteSpace(legalName) || legalName.Trim().Length > 250)
        {
            return Result.Failure(Error.Validation("ORG_INVALID_NAME", "Legal entity name is required (max 250 chars)."));
        }

        LegalName = legalName.Trim();
        TaxIdentificationNumber = string.IsNullOrWhiteSpace(tin) ? null : tin.Trim();
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        Status = status;
        return Result.Success();
    }
}

public sealed class Branch : TenantScopedEntity
{
    private Branch() { }

    public Guid LegalEntityId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Address { get; private set; }
    public OrganizationStatus Status { get; private set; } = OrganizationStatus.Active;

    public static Result<Branch> Create(Guid tenantId, Guid legalEntityId, string code, string name, string? address)
    {
        if (tenantId == Guid.Empty)
        {
            return Error.Validation("ORG_TENANT_REQUIRED", "TenantId is required.");
        }

        if (legalEntityId == Guid.Empty)
        {
            return Error.Validation("ORG_LEGAL_ENTITY_REQUIRED", "LegalEntityId is required.");
        }

        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32)
        {
            return Error.Validation("ORG_INVALID_CODE", "Branch code is required (max 32 chars).");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Error.Validation("ORG_INVALID_NAME", "Branch name is required (max 200 chars).");
        }

        var branch = new Branch
        {
            Id = Guid.CreateVersion7(),
            LegalEntityId = legalEntityId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim(),
            Status = OrganizationStatus.Active
        };
        branch.AssignTenant(tenantId);
        return branch;
    }

    public Result Update(string name, string? address, OrganizationStatus status)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure(Error.Validation("ORG_INVALID_NAME", "Branch name is required (max 200 chars)."));
        }

        Name = name.Trim();
        Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        Status = status;
        return Result.Success();
    }
}

public sealed class OrganizationUnit : TenantScopedEntity
{
    private OrganizationUnit() { }

    public Guid? ParentId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public OrganizationUnitType UnitType { get; private set; }
    public OrganizationStatus Status { get; private set; } = OrganizationStatus.Active;

    public static Result<OrganizationUnit> Create(Guid tenantId, Guid? parentId, string code, string name, OrganizationUnitType unitType)
    {
        if (tenantId == Guid.Empty)
        {
            return Error.Validation("ORG_TENANT_REQUIRED", "TenantId is required.");
        }

        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32)
        {
            return Error.Validation("ORG_INVALID_CODE", "Organization unit code is required (max 32 chars).");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Error.Validation("ORG_INVALID_NAME", "Organization unit name is required (max 200 chars).");
        }

        var unit = new OrganizationUnit
        {
            Id = Guid.CreateVersion7(),
            ParentId = parentId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            UnitType = unitType,
            Status = OrganizationStatus.Active
        };
        unit.AssignTenant(tenantId);
        return unit;
    }

    public Result Update(Guid? parentId, string name, OrganizationUnitType unitType, OrganizationStatus status = OrganizationStatus.Active)
    {
        if (parentId.HasValue && parentId.Value == Id)
        {
            return Result.Failure(Error.BusinessRule("ORG_UNIT_SELF_PARENT", "An organization unit cannot be its own parent."));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            return Result.Failure(Error.Validation("ORG_INVALID_NAME", "Organization unit name is required (max 200 chars)."));
        }

        ParentId = parentId;
        Name = name.Trim();
        UnitType = unitType;
        Status = status;
        return Result.Success();
    }
}

public sealed class UserOrganizationAssignment : TenantScopedEntity
{
    private UserOrganizationAssignment() { }

    public Guid UserId { get; private set; }
    public Guid? BranchId { get; private set; }
    public Guid? OrganizationUnitId { get; private set; }
    public string? Title { get; private set; }
    public bool IsPrimary { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset EffectiveFrom { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EffectiveTo { get; private set; }

    public static UserOrganizationAssignment Create(
        Guid tenantId,
        Guid userId,
        Guid? branchId,
        Guid? orgUnitId,
        string? title,
        bool isPrimary,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null)
    {
        var assignment = new UserOrganizationAssignment
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            BranchId = branchId,
            OrganizationUnitId = orgUnitId,
            Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
            IsPrimary = isPrimary,
            IsActive = true,
            EffectiveFrom = effectiveFrom ?? DateTimeOffset.UtcNow,
            EffectiveTo = effectiveTo
        };
        assignment.AssignTenant(tenantId);
        return assignment;
    }

    public void Deactivate(DateTimeOffset utcNow)
    {
        IsActive = false;
        EffectiveTo = utcNow;
    }

    public bool IsEffectiveAt(DateTimeOffset utcNow)
        => IsActive && EffectiveFrom <= utcNow && (!EffectiveTo.HasValue || EffectiveTo.Value > utcNow);
}
