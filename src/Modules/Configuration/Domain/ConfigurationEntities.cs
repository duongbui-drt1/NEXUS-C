using Nexus.BuildingBlocks.Web;
using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Configuration.Domain;

public sealed class PlatformSetting : AuditableEntity
{
    private PlatformSetting() { }

    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string Category { get; private set; } = "General";
    public int Version { get; private set; } = 1;

    public static Result<PlatformSetting> Create(string key, string value, string category)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 120)
        {
            return Error.Validation("CONFIG_INVALID_KEY", "Setting key is required (max 120 chars).");
        }

        if (SensitiveDataRedactor.IsSensitiveKey(key))
        {
            return Error.BusinessRule("CONFIG_SECRET_FORBIDDEN", "Plaintext secrets cannot be stored in the Configuration service. Use an external Secret Manager reference.");
        }

        return new PlatformSetting
        {
            Id = Guid.CreateVersion7(),
            Key = key.Trim().ToLowerInvariant(),
            Value = value ?? string.Empty,
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
            Version = 1
        };
    }

    public void UpdateValue(string value)
    {
        Value = value ?? string.Empty;
        Version++;
    }
}

public sealed class TenantSetting : TenantScopedEntity
{
    private TenantSetting() { }

    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public string Category { get; private set; } = "General";
    public int Version { get; private set; } = 1;

    public static Result<TenantSetting> Create(Guid tenantId, string key, string value, string category)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 120)
        {
            return Error.Validation("CONFIG_INVALID_KEY", "Setting key is required (max 120 chars).");
        }

        if (SensitiveDataRedactor.IsSensitiveKey(key))
        {
            return Error.BusinessRule("CONFIG_SECRET_FORBIDDEN", "Plaintext secrets cannot be stored in tenant configuration.");
        }

        var setting = new TenantSetting
        {
            Id = Guid.CreateVersion7(),
            Key = key.Trim().ToLowerInvariant(),
            Value = value ?? string.Empty,
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim(),
            Version = 1
        };
        setting.AssignTenant(tenantId);
        return setting;
    }

    public void UpdateValue(string value)
    {
        Value = value ?? string.Empty;
        Version++;
    }
}

public sealed class FeatureFlag : AuditableEntity
{
    private FeatureFlag() { }

    public Guid? TenantId { get; private set; } // null = platform default; set = tenant override
    public string Key { get; private set; } = string.Empty;
    public bool IsEnabled { get; private set; }
    public string? Description { get; private set; }
    public int Version { get; private set; } = 1;

    public static Result<FeatureFlag> Create(Guid? tenantId, string key, bool isEnabled, string? description)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 100)
        {
            return Error.Validation("FLAG_INVALID_KEY", "Feature flag key is required (max 100 chars).");
        }

        return new FeatureFlag
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Key = key.Trim().ToLowerInvariant(),
            IsEnabled = isEnabled,
            Description = description?.Trim(),
            Version = 1
        };
    }

    public void SetEnabled(bool isEnabled, string? description = null)
    {
        IsEnabled = isEnabled;
        if (description is not null) Description = description.Trim();
        Version++;
    }
}

public sealed class SettingHistory : Entity
{
    private SettingHistory() { }

    public Guid? TenantId { get; private set; }
    public string Scope { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string? PreviousValue { get; private set; }
    public string NewValue { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public Guid? ChangedByUserId { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }

    public static SettingHistory Record(Guid? tenantId, string scope, string key, string? previousValue, string newValue, int version, Guid? actorId, DateTimeOffset utcNow)
        => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Scope = scope,
            Key = key,
            PreviousValue = previousValue,
            NewValue = newValue,
            Version = version,
            ChangedByUserId = actorId,
            ChangedAt = utcNow
        };
}

public enum RateLimitScopeType
{
    AnonymousIp = 1,
    HumanSession = 2,
    ServiceClient = 3,
    ApiKey = 4,
    Expensive = 5
}

public sealed class RateLimitPolicyRecord : AuditableEntity
{
    private RateLimitPolicyRecord() { }

    public string Name { get; private set; } = string.Empty;
    public string RoutePattern { get; private set; } = "/api/v1/*";
    public RateLimitScopeType ScopeType { get; private set; } = RateLimitScopeType.HumanSession;
    public int PermitLimit { get; private set; } = 120;
    public int WindowSeconds { get; private set; } = 60;
    public int BurstLimit { get; private set; }
    public bool IsEnabled { get; private set; } = true;
    public int Priority { get; private set; } = 100;
    public string? Description { get; private set; }

    public static Result<RateLimitPolicyRecord> Create(
        string name,
        string routePattern,
        RateLimitScopeType scopeType,
        int permitLimit,
        int windowSeconds,
        int burstLimit = 0,
        bool isEnabled = true,
        int priority = 100,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            return Error.Validation("RATE_LIMIT_INVALID_NAME", "Policy name is required (max 100 chars).");
        }

        if (string.IsNullOrWhiteSpace(routePattern) || !routePattern.Trim().StartsWith('/') || routePattern.Trim().Length > 200)
        {
            return Error.Validation("RATE_LIMIT_INVALID_ROUTE", "Route pattern must start with '/' (max 200 chars).");
        }

        if (permitLimit < 1 || permitLimit > 1_000_000)
        {
            return Error.Validation("RATE_LIMIT_INVALID_PERMIT", "PermitLimit must be between 1 and 1,000,000.");
        }

        if (windowSeconds < 1 || windowSeconds > 86_400)
        {
            return Error.Validation("RATE_LIMIT_INVALID_WINDOW", "WindowSeconds must be between 1 and 86,400.");
        }

        if (burstLimit < 0 || burstLimit > permitLimit)
        {
            return Error.Validation("RATE_LIMIT_INVALID_BURST", "BurstLimit must be between 0 and PermitLimit.");
        }

        return new RateLimitPolicyRecord
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim().ToLowerInvariant(),
            RoutePattern = routePattern.Trim(),
            ScopeType = scopeType,
            PermitLimit = permitLimit,
            WindowSeconds = windowSeconds,
            BurstLimit = burstLimit,
            IsEnabled = isEnabled,
            Priority = priority,
            Description = description?.Trim()
        };
    }

    public Result Update(
        string routePattern,
        RateLimitScopeType scopeType,
        int permitLimit,
        int windowSeconds,
        int burstLimit,
        bool isEnabled,
        int priority,
        string? description)
    {
        if (string.IsNullOrWhiteSpace(routePattern) || !routePattern.Trim().StartsWith('/') || routePattern.Trim().Length > 200)
        {
            return Result.Failure(Error.Validation("RATE_LIMIT_INVALID_ROUTE", "Route pattern must start with '/' (max 200 chars)."));
        }

        if (permitLimit < 1 || permitLimit > 1_000_000)
        {
            return Result.Failure(Error.Validation("RATE_LIMIT_INVALID_PERMIT", "PermitLimit must be between 1 and 1,000,000."));
        }

        if (windowSeconds < 1 || windowSeconds > 86_400)
        {
            return Result.Failure(Error.Validation("RATE_LIMIT_INVALID_WINDOW", "WindowSeconds must be between 1 and 86,400."));
        }

        if (burstLimit < 0 || burstLimit > permitLimit)
        {
            return Result.Failure(Error.Validation("RATE_LIMIT_INVALID_BURST", "BurstLimit must be between 0 and PermitLimit."));
        }

        RoutePattern = routePattern.Trim();
        ScopeType = scopeType;
        PermitLimit = permitLimit;
        WindowSeconds = windowSeconds;
        BurstLimit = burstLimit;
        IsEnabled = isEnabled;
        Priority = priority;
        Description = description?.Trim();
        return Result.Success();
    }
}

public sealed class ReferenceDataItem : AuditableEntity
{
    private ReferenceDataItem() { }

    public string Category { get; private set; } = string.Empty; // country, timezone, locale, language, status
    public string Code { get; private set; } = string.Empty;
    public string Label { get; private set; } = string.Empty;
    public string? LocalizedLabelsJson { get; private set; }
    public string? MetadataJson { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    public static Result<ReferenceDataItem> Create(
        string category,
        string code,
        string label,
        int sortOrder = 0,
        bool isActive = true,
        string? localizedLabelsJson = null,
        string? metadataJson = null)
    {
        if (string.IsNullOrWhiteSpace(category) || category.Trim().Length > 64)
        {
            return Error.Validation("REFDATA_INVALID_CATEGORY", "Category is required (max 64 chars).");
        }

        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 64)
        {
            return Error.Validation("REFDATA_INVALID_CODE", "Code is required (max 64 chars).");
        }

        if (string.IsNullOrWhiteSpace(label) || label.Trim().Length > 200)
        {
            return Error.Validation("REFDATA_INVALID_LABEL", "Label is required (max 200 chars).");
        }

        return new ReferenceDataItem
        {
            Id = Guid.CreateVersion7(),
            Category = category.Trim().ToLowerInvariant(),
            Code = code.Trim(),
            Label = label.Trim(),
            SortOrder = sortOrder,
            IsActive = isActive,
            LocalizedLabelsJson = string.IsNullOrWhiteSpace(localizedLabelsJson) ? null : localizedLabelsJson.Trim(),
            MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson.Trim()
        };
    }

    public void Update(string label, bool isActive, int sortOrder, string? localizedLabelsJson = null, string? metadataJson = null)
    {
        if (!string.IsNullOrWhiteSpace(label)) Label = label.Trim();
        IsActive = isActive;
        SortOrder = sortOrder;
        if (localizedLabelsJson is not null) LocalizedLabelsJson = string.IsNullOrWhiteSpace(localizedLabelsJson) ? null : localizedLabelsJson.Trim();
        if (metadataJson is not null) MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson.Trim();
    }
}

public sealed class CurrencyDefinition : AuditableEntity
{
    private CurrencyDefinition() { }

    public string Code { get; private set; } = string.Empty; // ISO 4217 3-letter uppercase
    public string Name { get; private set; } = string.Empty;
    public string Symbol { get; private set; } = string.Empty;
    public int MinorUnitPrecision { get; private set; } = 2;
    public bool IsActive { get; private set; } = true;

    public static Result<CurrencyDefinition> Create(string code, string name, string symbol, int minorUnitPrecision = 2, bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 3)
        {
            return Error.Validation("CURRENCY_INVALID_CODE", "Currency code must be a 3-letter ISO 4217 code.");
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120)
        {
            return Error.Validation("CURRENCY_INVALID_NAME", "Currency name is required (max 120 chars).");
        }

        if (minorUnitPrecision < 0 || minorUnitPrecision > 6)
        {
            return Error.Validation("CURRENCY_INVALID_PRECISION", "MinorUnitPrecision must be between 0 and 6.");
        }

        return new CurrencyDefinition
        {
            Id = Guid.CreateVersion7(),
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Symbol = string.IsNullOrWhiteSpace(symbol) ? code.Trim().ToUpperInvariant() : symbol.Trim(),
            MinorUnitPrecision = minorUnitPrecision,
            IsActive = isActive
        };
    }
}

public sealed class ExchangeRateObservation : Entity
{
    private ExchangeRateObservation() { }

    public string BaseCurrency { get; private set; } = "USD";
    public string QuoteCurrency { get; private set; } = string.Empty;
    public decimal Rate { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public DateTimeOffset ObservedAt { get; private set; }
    public DateTimeOffset FetchedAt { get; private set; }
    public DateTimeOffset StaleAfter { get; private set; }
    public string? SourceReference { get; private set; }

    public static Result<ExchangeRateObservation> Create(
        string baseCurrency,
        string quoteCurrency,
        decimal rate,
        string provider,
        DateTimeOffset observedAt,
        DateTimeOffset fetchedAt,
        DateTimeOffset staleAfter,
        string? sourceReference = null)
    {
        if (string.IsNullOrWhiteSpace(baseCurrency) || baseCurrency.Trim().Length != 3 ||
            string.IsNullOrWhiteSpace(quoteCurrency) || quoteCurrency.Trim().Length != 3)
        {
            return Error.Validation("FX_INVALID_CURRENCY", "BaseCurrency and QuoteCurrency must be 3-letter ISO 4217 codes.");
        }

        if (rate <= 0m)
        {
            return Error.Validation("FX_INVALID_RATE", "Exchange rate must be strictly greater than zero.");
        }

        return new ExchangeRateObservation
        {
            Id = Guid.CreateVersion7(),
            BaseCurrency = baseCurrency.Trim().ToUpperInvariant(),
            QuoteCurrency = quoteCurrency.Trim().ToUpperInvariant(),
            Rate = decimal.Round(rate, 8, MidpointRounding.AwayFromZero),
            Provider = string.IsNullOrWhiteSpace(provider) ? "Manual" : provider.Trim(),
            ObservedAt = observedAt,
            FetchedAt = fetchedAt,
            StaleAfter = staleAfter,
            SourceReference = string.IsNullOrWhiteSpace(sourceReference) ? null : sourceReference.Trim()
        };
    }
}
