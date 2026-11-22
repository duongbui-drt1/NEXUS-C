using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Configuration.Application;
using Nexus.SharedKernel.Security;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform")]
public sealed class FxController(IReferenceDataService service) : ControllerBase
{
    [HttpGet("currencies")]
    [Authorize]
    public async Task<IActionResult> ListCurrencies(
        [FromQuery] string? search = null,
        [FromQuery] bool includeInactive = false,
        CancellationToken ct = default)
        => Ok(await service.ListCurrenciesAsync(search, includeInactive, ct));

    [HttpGet("fx/rates")]
    [Authorize]
    public async Task<IActionResult> GetLatestRates([FromQuery] string baseCurrency = "USD", CancellationToken ct = default)
        => Ok(await service.GetLatestFxRatesAsync(baseCurrency, ct));

    [HttpPost("fx/refresh")]
    [RequirePermission(Permissions.ReferenceDataManage)]
    public async Task<IActionResult> RefreshRates([FromQuery] string baseCurrency = "USD", CancellationToken ct = default)
        => (await service.RefreshFxRatesAsync(baseCurrency, ct)).ToActionResult(HttpContext);

    [HttpPost("fx/observations")]
    [RequirePermission(Permissions.ReferenceDataManage)]
    public async Task<IActionResult> RecordObservation([FromBody] RecordFxObservationRequest request, CancellationToken ct)
        => (await service.RecordFxObservationAsync(request, ct)).ToActionResult(HttpContext, StatusCodes.Status201Created);

    [HttpGet("fx/convert")]
    [Authorize]
    public async Task<IActionResult> Convert(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] decimal amount,
        CancellationToken ct = default)
        => (await service.ConvertCurrencyAsync(from, to, amount, ct)).ToActionResult(HttpContext);
}
