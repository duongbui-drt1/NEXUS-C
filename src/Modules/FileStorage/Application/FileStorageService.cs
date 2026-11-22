using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.Modules.FileStorage.Domain;
using Nexus.Modules.FileStorage.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.FileStorage.Application;

public sealed record FileObjectDto(
    Guid Id,
    Guid TenantId,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    string Sha256Hash,
    string Category,
    DateTimeOffset CreatedAt,
    int? RetentionDays = null,
    DateTimeOffset? RetentionExpiresAt = null);

public sealed record FileDownloadResult(Stream Content, string ContentType, string FileName);
public sealed record TemporaryDownloadUrlDto(string DownloadUrl, DateTimeOffset ExpiresAt);

public interface IFileStorageService
{
    Task<Result<FileObjectDto>> UploadAsync(string fileName, string contentType, Stream content, long sizeBytes, string category = "General", int? retentionDays = null, CancellationToken ct = default);
    Task<Result<FileObjectDto>> GetMetadataAsync(Guid fileId, CancellationToken ct = default);
    Task<Result<PagedResult<FileObjectDto>>> ListAsync(PagedRequest paging, CancellationToken ct = default);
    Task<Result<FileDownloadResult>> DownloadAsync(Guid fileId, CancellationToken ct = default);
    Task<Result<TemporaryDownloadUrlDto>> CreateTemporaryDownloadUrlAsync(Guid fileId, int validMinutes = 10, CancellationToken ct = default);
    Task<Result<FileDownloadResult>> DownloadWithSignedTokenAsync(Guid fileId, long expiresUnix, string signature, CancellationToken ct = default);
    Task<Result> DeleteAsync(Guid fileId, CancellationToken ct = default);
}

public sealed class FileStorageService(
    FileStorageDbContext db,
    IObjectStorage objectStorage,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ISystemClock clock,
    IAuditWriter auditWriter,
    IOptions<ObjectStorageOptions> options) : IFileStorageService
{
    public async Task<Result<FileObjectDto>> UploadAsync(
        string fileName,
        string contentType,
        Stream content,
        long sizeBytes,
        string category = "General",
        int? retentionDays = null,
        CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        if (sizeBytes <= 0 || sizeBytes > options.Value.MaxUploadSizeBytes)
        {
            return Error.Validation("FILE_INVALID_SIZE", $"File size must be between 1 and {options.Value.MaxUploadSizeBytes} bytes.");
        }

        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length != sizeBytes)
        {
            sizeBytes = buffer.Length;
        }

        if (sizeBytes <= 0 || sizeBytes > options.Value.MaxUploadSizeBytes)
        {
            return Error.Validation("FILE_INVALID_SIZE", $"File size must be between 1 and {options.Value.MaxUploadSizeBytes} bytes.");
        }

        buffer.Position = 0;
        var hashBytes = await SHA256.HashDataAsync(buffer, ct);
        var hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();

        var created = StoredFileObject.Create(tenantId, fileName, contentType, sizeBytes, options.Value.MaxUploadSizeBytes, hashHex, category, retentionDays, clock.UtcNow);
        if (created.IsFailure) return Result.Failure<FileObjectDto>(created.Error);

        var fileObj = created.Value;

        buffer.Position = 0;
        await objectStorage.PutObjectAsync(fileObj.ObjectKey, buffer, fileObj.ContentType, ct);

        db.Files.Add(fileObj);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "files.upload", "StoredFileObject", fileObj.Id.ToString("D"), tenantId, "Success",
            AfterState: new { fileObj.OriginalFileName, fileObj.ContentType, fileObj.SizeBytes, fileObj.Sha256Hash, fileObj.RetentionDays }), ct);

        return Map(fileObj);
    }

    public async Task<Result<FileObjectDto>> GetMetadataAsync(Guid fileId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var fileObj = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        return fileObj is null ? Error.NotFound("FILE_NOT_FOUND", "File was not found.") : Map(fileObj);
    }

    public async Task<Result<PagedResult<FileObjectDto>>> ListAsync(PagedRequest paging, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");

        var query = db.Files.AsNoTracking();
        var total = await query.LongCountAsync(ct);
        var items = await query.OrderByDescending(f => f.CreatedAt).Skip(paging.Skip).Take(paging.PageSize).Select(f => Map(f)).ToListAsync(ct);
        return new PagedResult<FileObjectDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<Result<FileDownloadResult>> DownloadAsync(Guid fileId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var fileObj = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (fileObj is null) return Error.NotFound("FILE_NOT_FOUND", "File was not found.");

        if (fileObj.IsRetentionExpired(clock.UtcNow))
        {
            return Error.NotFound("FILE_RETENTION_EXPIRED", "File retention period has expired.");
        }

        var stream = await objectStorage.GetObjectAsync(fileObj.ObjectKey, ct);
        if (stream is null) return Error.NotFound("FILE_BLOB_MISSING", "File content is unavailable in object storage.");

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "files.download", "StoredFileObject", fileObj.Id.ToString("D"), tenantId, "Success",
            AfterState: new { fileObj.OriginalFileName, fileObj.SizeBytes }), ct);

        return new FileDownloadResult(stream, fileObj.ContentType, fileObj.OriginalFileName);
    }

    public async Task<Result<TemporaryDownloadUrlDto>> CreateTemporaryDownloadUrlAsync(Guid fileId, int validMinutes = 10, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required.");
        var tenantId = tenantContext.TenantId!.Value;

        var fileObj = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (fileObj is null) return Error.NotFound("FILE_NOT_FOUND", "File was not found.");

        if (fileObj.IsRetentionExpired(clock.UtcNow))
        {
            return Error.NotFound("FILE_RETENTION_EXPIRED", "File retention period has expired.");
        }

        var expiresAt = clock.UtcNow.AddMinutes(Math.Clamp(validMinutes, 1, 60));
        var expiresUnix = expiresAt.ToUnixTimeSeconds();
        var signature = ComputeSignature(fileObj.Id, fileObj.TenantId, expiresUnix);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "files.temporary_url.create", "StoredFileObject", fileObj.Id.ToString("D"), tenantId, "Success",
            AfterState: new { fileObj.OriginalFileName, ExpiresAt = expiresAt }), ct);

        var url = $"/api/v1/platform/files/{fileObj.Id:D}/signed-download?expires={expiresUnix}&sig={signature}";
        return new TemporaryDownloadUrlDto(url, expiresAt);
    }

    public async Task<Result<FileDownloadResult>> DownloadWithSignedTokenAsync(Guid fileId, long expiresUnix, string signature, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Authenticated tenant context is required for signed download.");
        var tenantId = tenantContext.TenantId!.Value;

        if (DateTimeOffset.FromUnixTimeSeconds(expiresUnix) <= clock.UtcNow)
        {
            return Error.Unauthorized("FILE_LINK_EXPIRED", "Temporary download URL has expired.");
        }

        var expected = ComputeSignature(fileId, tenantId, expiresUnix);
        if (!CryptoHelper.FixedTimeEquals(expected, signature ?? string.Empty))
        {
            return Error.Forbidden("FILE_INVALID_SIGNATURE", "Download signature is invalid for this tenant or file.");
        }

        return await DownloadAsync(fileId, ct);
    }

    public async Task<Result> DeleteAsync(Guid fileId, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant) return Result.Failure(Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required."));
        var tenantId = tenantContext.TenantId!.Value;

        var fileObj = await db.Files.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (fileObj is null) return Result.Failure(Error.NotFound("FILE_NOT_FOUND", "File was not found."));

        fileObj.SoftDelete(currentUser.UserId, clock.UtcNow);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "files.delete", "StoredFileObject", fileId.ToString("D"), tenantId, "Success"), ct);

        return Result.Success();
    }

    private string ComputeSignature(Guid fileId, Guid tenantId, long expiresUnix)
    {
        var secret = options.Value.SigningSecret;
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
        {
            throw new InvalidOperationException("ObjectStorage:SigningSecret must be configured to sign file download URLs.");
        }

        return CryptoHelper.ComputeHmacSha256($"{fileId:D}|{tenantId:D}|{expiresUnix}", secret);
    }

    private static FileObjectDto Map(StoredFileObject f) => new(
        f.Id, f.TenantId, f.OriginalFileName, f.ContentType, f.SizeBytes, f.Sha256Hash,
        f.Category, f.CreatedAt, f.RetentionDays, f.RetentionExpiresAt);
}
