using Nexus.SharedKernel.Primitives;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.FileStorage.Domain;

public sealed class StoredFileObject : TenantScopedEntity, ISoftDeletable
{
    private StoredFileObject() { }

    public string OriginalFileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string Sha256Hash { get; private set; } = string.Empty;
    public string ObjectKey { get; private set; } = string.Empty;
    public string Category { get; private set; } = "General";
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public Guid? DeletedBy { get; private set; }

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "text/plain",
        "text/csv",
        "application/json",
        "image/png",
        "image/jpeg",
        "image/webp",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    };

    public static Result<StoredFileObject> Create(
        Guid tenantId,
        string originalFileName,
        string contentType,
        long sizeBytes,
        long maxAllowedBytes,
        string sha256Hash,
        string category)
    {
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            return Error.Validation("FILE_INVALID_NAME", "File name is required.");
        }

        if (sizeBytes <= 0 || sizeBytes > maxAllowedBytes)
        {
            return Error.Validation("FILE_INVALID_SIZE", $"File size must be between 1 byte and {maxAllowedBytes} bytes.");
        }

        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType.Trim()))
        {
            return Error.Validation("FILE_UNSUPPORTED_CONTENT_TYPE", $"Content type '{contentType}' is not permitted.");
        }

        var id = Guid.CreateVersion7();
        var cleanName = Path.GetFileName(originalFileName).Trim();
        var objectKey = $"tenants/{tenantId:D}/{id:D}/{cleanName}";

        var obj = new StoredFileObject
        {
            Id = id,
            OriginalFileName = cleanName,
            ContentType = contentType.Trim().ToLowerInvariant(),
            SizeBytes = sizeBytes,
            Sha256Hash = sha256Hash,
            ObjectKey = objectKey,
            Category = string.IsNullOrWhiteSpace(category) ? "General" : category.Trim()
        };
        obj.AssignTenant(tenantId);
        return obj;
    }

    public void SoftDelete(Guid? actorUserId, DateTimeOffset utcNow)
    {
        IsDeleted = true;
        DeletedAt = utcNow;
        DeletedBy = actorUserId;
    }
}
