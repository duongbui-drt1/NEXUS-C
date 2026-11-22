using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Configuration.Application;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/configuration")]
public sealed class ConfigurationController(IConfigurationService configService) : ControllerBase
{
    [HttpPut("tenant")]
    [RequirePermission(Permissions.ConfigurationManage)]
    public async Task<IActionResult> UpsertTenantSetting([FromBody] UpsertSettingRequest request, CancellationToken ct)
        => (await configService.UpsertTenantSettingAsync(request, ct)).ToActionResult(HttpContext);

    [HttpGet("tenant")]
    [RequirePermission(Permissions.ConfigurationRead)]
    public async Task<IActionResult> ListTenantSettings([FromQuery] string? category = null, CancellationToken ct = default)
        => (await configService.ListTenantSettingsAsync(category, ct)).ToActionResult(HttpContext);

    [HttpPut("global")]
    [RequirePermission(Permissions.PlatformConfigurationManage)]
    public async Task<IActionResult> UpsertPlatformSetting([FromBody] UpsertSettingRequest request, CancellationToken ct)
        => (await configService.UpsertPlatformSettingAsync(request, ct)).ToActionResult(HttpContext);

    [HttpGet("global")]
    [RequirePermission(Permissions.ConfigurationRead)]
    public async Task<IActionResult> ListPlatformSettings([FromQuery] string? category = null, CancellationToken ct = default)
        => (await configService.ListPlatformSettingsAsync(category, ct)).ToActionResult(HttpContext);

    [HttpGet("history")]
    [RequirePermission(Permissions.ConfigurationRead)]
    public async Task<IActionResult> ListHistory([FromQuery] string? key = null, CancellationToken ct = default)
        => (await configService.ListHistoryAsync(key, ct)).ToActionResult(HttpContext);

    [HttpPut("feature-flags")]
    [RequirePermission(Permissions.ConfigurationManage)]
    public async Task<IActionResult> UpsertTenantFlag([FromBody] UpsertFeatureFlagRequest request, CancellationToken ct)
        => (await configService.UpsertFeatureFlagAsync(request, isPlatformDefault: false, ct)).ToActionResult(HttpContext);

    [HttpPut("feature-flags/global")]
    [RequirePermission(Permissions.PlatformConfigurationManage)]
    public async Task<IActionResult> UpsertGlobalFlag([FromBody] UpsertFeatureFlagRequest request, CancellationToken ct)
        => (await configService.UpsertFeatureFlagAsync(request, isPlatformDefault: true, ct)).ToActionResult(HttpContext);

    [HttpGet("feature-flags")]
    [RequirePermission(Permissions.ConfigurationRead)]
    public async Task<IActionResult> ListFlags(CancellationToken ct)
        => (await configService.ListFeatureFlagsAsync(ct)).ToActionResult(HttpContext);

    [HttpGet("feature-flags/{key}/evaluate")]
    [RequirePermission(Permissions.ConfigurationRead)]
    public async Task<IActionResult> EvaluateFlag(string key, CancellationToken ct)
        => (await configService.EvaluateFeatureFlagAsync(key, ct)).ToActionResult(HttpContext);
}
