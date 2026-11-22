using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.FileStorage.Application;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/files")]
public sealed class FilesController(IFileStorageService fileService) : ControllerBase
{
    [HttpPost]
    [RequirePermission(Permissions.FilesUpload)]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Upload(IFormFile? file, [FromForm] string category = "General", [FromForm] int? retentionDays = null, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
        {
            return ApiResultMapper.ToProblemResult(Error.Validation("FILE_REQUIRED", "A non-empty file is required."), HttpContext);
        }

        await using var stream = file.OpenReadStream();
        var result = await fileService.UploadAsync(file.FileName, file.ContentType, stream, file.Length, category, retentionDays, ct);
        return result.ToActionResult(HttpContext, StatusCodes.Status201Created);
    }

    [HttpGet]
    [RequirePermission(Permissions.FilesRead)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => (await fileService.ListAsync(new PagedRequest { Page = page, PageSize = pageSize }, ct)).ToActionResult(HttpContext);

    [HttpGet("{fileId:guid}")]
    [RequirePermission(Permissions.FilesRead)]
    public async Task<IActionResult> GetMetadata(Guid fileId, CancellationToken ct)
        => (await fileService.GetMetadataAsync(fileId, ct)).ToActionResult(HttpContext);

    [HttpGet("{fileId:guid}/download")]
    [RequirePermission(Permissions.FilesRead)]
    public async Task<IActionResult> Download(Guid fileId, CancellationToken ct)
    {
        var result = await fileService.DownloadAsync(fileId, ct);
        if (result.IsFailure) return ApiResultMapper.ToProblemResult(result.Error, HttpContext);
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpPost("{fileId:guid}/temporary-url")]
    [RequirePermission(Permissions.FilesRead)]
    public async Task<IActionResult> CreateTemporaryUrl(Guid fileId, [FromQuery] int validMinutes = 10, CancellationToken ct = default)
        => (await fileService.CreateTemporaryDownloadUrlAsync(fileId, validMinutes, ct)).ToActionResult(HttpContext);

    [HttpGet("{fileId:guid}/signed-download")]
    [RequirePermission(Permissions.FilesRead)]
    public async Task<IActionResult> SignedDownload(Guid fileId, [FromQuery] long expires, [FromQuery] string sig, CancellationToken ct)
    {
        var result = await fileService.DownloadWithSignedTokenAsync(fileId, expires, sig, ct);
        if (result.IsFailure) return ApiResultMapper.ToProblemResult(result.Error, HttpContext);
        return File(result.Value.Content, result.Value.ContentType, result.Value.FileName);
    }

    [HttpDelete("{fileId:guid}")]
    [RequirePermission(Permissions.FilesDelete)]
    public async Task<IActionResult> Delete(Guid fileId, CancellationToken ct)
        => (await fileService.DeleteAsync(fileId, ct)).ToActionResult(HttpContext);
}
