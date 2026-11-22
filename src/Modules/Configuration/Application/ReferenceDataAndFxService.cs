using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.Modules.Configuration.Domain;
using Nexus.Modules.Configuration.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Configuration.Application;

public sealed record ReferenceDataItemDto(
    Guid Id,
    string Category,
    string Code,
    string Label,
    bool IsActive,
    int SortOrder,
    string? LocalizedLabelsJson,
    string? MetadataJson);

public sealed record CreateReferenceDataRequest(
    string Category,
    string Code,
    string Label,
    int SortOrder = 0,
    bool IsActive = true,
    string? LocalizedLabelsJson = null,
    string? MetadataJson = null);

public sealed record UpdateReferenceDataRequest(
    string Label,
    bool IsActive,
    int SortOrder,
    string? LocalizedLabelsJson = null,
    string? MetadataJson = null);

public sealed record CurrencyDto(
    Guid Id,
    string Code,
    string Name,
    string Symbol,
    int MinorUnitPrecision,
    bool IsActive);

public sealed record FxRateDto(
    Guid Id,
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    string Provider,
    DateTimeOffset ObservedAt,
    DateTimeOffset FetchedAt,
    DateTimeOffset StaleAfter,
    bool IsStale,
    string? SourceReference,
    string Disclaimer);

public sealed record FxRefreshResultDto(
    string BaseCurrency,
    string Provider,
    int RatesUpdated,
    DateTimeOffset ObservedAt,
    DateTimeOffset FetchedAt,
    DateTimeOffset StaleAfter,
    string Disclaimer);

public sealed record RecordFxObservationRequest(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    string Provider = "CorporateTreasury",
    DateTimeOffset? ObservedAt = null,
    int TtlHours = 24,
    string? SourceReference = null);

public sealed record FxConversionResultDto(
    string FromCurrency,
    string ToCurrency,
    decimal InputAmount,
    decimal ConvertedAmount,
    int TargetMinorUnitPrecision,
    decimal EffectiveRate,
    string Provider,
    DateTimeOffset ObservedAt,
    DateTimeOffset FetchedAt,
    bool IsStale,
    string Disclaimer);

public sealed record FxProviderFetchResult(
    string BaseCurrency,
    string Provider,
    DateTimeOffset ObservedAt,
    string SourceReference,
    IReadOnlyDictionary<string, decimal> Rates);

public interface IFxRateProvider
{
    string ProviderName { get; }
    Task<Result<FxProviderFetchResult>> FetchLatestAsync(string baseCurrency, CancellationToken ct = default);
}

public sealed class FrankfurterFxRateProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<FrankfurterFxRateProvider> logger) : IFxRateProvider
{
    public const string HttpClientName = "NexusFxProvider";
    public string ProviderName => "FrankfurterECB";

    public async Task<Result<FxProviderFetchResult>> FetchLatestAsync(string baseCurrency, CancellationToken ct = default)
    {
        var baseCode = baseCurrency.Trim().ToUpperInvariant();
        if (baseCode.Length != 3)
        {
            return Error.Validation("FX_INVALID_BASE", "BaseCurrency must be a 3-letter ISO 4217 code.");
        }

        var url = $"https://api.frankfurter.app/latest?from={Uri.EscapeDataString(baseCode)}";
        var uri = new Uri(url);

        var ssrf = await SsrfGuard.ValidateUriAndResolveDnsAsync(uri, allowedHosts: ["api.frankfurter.app"], ct: ct);
        if (!ssrf.IsSafe)
        {
            return Error.BusinessRule("FX_PROVIDER_BLOCKED", $"FX provider host check failed: {ssrf.FailureReason}");
        }

        try
        {
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
            {
                return Error.BusinessRule("FX_PROVIDER_HTTP_ERROR", $"FX provider returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            var dateStr = root.TryGetProperty("date", out var dProp) ? dProp.GetString() : null;
            var observedAt = DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate)
                ? parsedDate
                : DateTimeOffset.UtcNow;

            var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("rates", out var ratesElem) && ratesElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in ratesElem.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetDecimal(out var dec) && dec > 0m)
                    {
                        rates[prop.Name.ToUpperInvariant()] = dec;
                    }
                }
            }

            if (rates.Count == 0)
            {
                return Error.BusinessRule("FX_PROVIDER_EMPTY", "FX provider returned no usable exchange rates.");
            }

            return new FxProviderFetchResult(baseCode, ProviderName, observedAt, url, rates);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch latest FX rates from FrankfurterECB for base {Base}", baseCode);
            return Error.BusinessRule("FX_PROVIDER_UNAVAILABLE", $"FX provider unavailable: {ex.Message}");
        }
    }
}

public interface IReferenceDataService
{
    Task SeedReferenceDataAndCurrenciesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ReferenceDataItemDto>> ListByCategoryAsync(string category, string? search = null, bool includeInactive = false, CancellationToken ct = default);
    Task<Result<ReferenceDataItemDto>> CreateItemAsync(CreateReferenceDataRequest request, CancellationToken ct = default);
    Task<Result<ReferenceDataItemDto>> UpdateItemAsync(Guid id, UpdateReferenceDataRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<CurrencyDto>> ListCurrenciesAsync(string? search = null, bool includeInactive = false, CancellationToken ct = default);
    Task<IReadOnlyList<FxRateDto>> GetLatestFxRatesAsync(string baseCurrency = "USD", CancellationToken ct = default);
    Task<Result<FxRefreshResultDto>> RefreshFxRatesAsync(string baseCurrency = "USD", CancellationToken ct = default);
    Task<Result<FxRateDto>> RecordFxObservationAsync(RecordFxObservationRequest request, CancellationToken ct = default);
    Task<Result<FxConversionResultDto>> ConvertCurrencyAsync(string fromCurrency, string toCurrency, decimal amount, CancellationToken ct = default);
}

public sealed class ReferenceDataService(
    ConfigurationDbContext db,
    IFxRateProvider fxProvider,
    ISystemClock clock,
    IAuditWriter auditWriter,
    IPlatformRealtimePublisher? realtimePublisher = null) : IReferenceDataService
{
    public const string IndicativeDisclaimer = "Indicative FX reference rate — not a settlement rate.";

    public async Task SeedReferenceDataAndCurrenciesAsync(CancellationToken ct = default)
    {
        var existingRefKeys = (await db.ReferenceDataItems.AsNoTracking()
            .Select(r => r.Category + ":" + r.Code)
            .ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var itemsToAdd = new List<ReferenceDataItem>();

        void Add(string cat, string code, string label, int sort, string? meta = null)
        {
            if (existingRefKeys.Contains($"{cat}:{code}")) return;
            var res = ReferenceDataItem.Create(cat, code, label, sort, true, null, meta);
            if (res.IsSuccess)
            {
                itemsToAdd.Add(res.Value);
                existingRefKeys.Add($"{cat}:{code}");
            }
        }

        // Countries (ISO 3166-1 alpha-2)
        (string Code, string Label, string Meta)[] countries =
        [
            ("VN", "Vietnam", "{\"dialCode\":\"+84\",\"region\":\"APAC\"}"),
            ("US", "United States", "{\"dialCode\":\"+1\",\"region\":\"NA\"}"),
            ("SG", "Singapore", "{\"dialCode\":\"+65\",\"region\":\"APAC\"}"),
            ("GB", "United Kingdom", "{\"dialCode\":\"+44\",\"region\":\"EMEA\"}"),
            ("JP", "Japan", "{\"dialCode\":\"+81\",\"region\":\"APAC\"}"),
            ("KR", "South Korea", "{\"dialCode\":\"+82\",\"region\":\"APAC\"}"),
            ("AU", "Australia", "{\"dialCode\":\"+61\",\"region\":\"APAC\"}"),
            ("CA", "Canada", "{\"dialCode\":\"+1\",\"region\":\"NA\"}"),
            ("DE", "Germany", "{\"dialCode\":\"+49\",\"region\":\"EMEA\"}"),
            ("FR", "France", "{\"dialCode\":\"+33\",\"region\":\"EMEA\"}"),
            ("NL", "Netherlands", "{\"dialCode\":\"+31\",\"region\":\"EMEA\"}"),
            ("CH", "Switzerland", "{\"dialCode\":\"+41\",\"region\":\"EMEA\"}"),
            ("SE", "Sweden", "{\"dialCode\":\"+46\",\"region\":\"EMEA\"}"),
            ("NO", "Norway", "{\"dialCode\":\"+47\",\"region\":\"EMEA\"}"),
            ("DK", "Denmark", "{\"dialCode\":\"+45\",\"region\":\"EMEA\"}"),
            ("FI", "Finland", "{\"dialCode\":\"+358\",\"region\":\"EMEA\"}"),
            ("IE", "Ireland", "{\"dialCode\":\"+353\",\"region\":\"EMEA\"}"),
            ("ES", "Spain", "{\"dialCode\":\"+34\",\"region\":\"EMEA\"}"),
            ("IT", "Italy", "{\"dialCode\":\"+39\",\"region\":\"EMEA\"}"),
            ("PT", "Portugal", "{\"dialCode\":\"+351\",\"region\":\"EMEA\"}"),
            ("BE", "Belgium", "{\"dialCode\":\"+32\",\"region\":\"EMEA\"}"),
            ("AT", "Austria", "{\"dialCode\":\"+43\",\"region\":\"EMEA\"}"),
            ("PL", "Poland", "{\"dialCode\":\"+48\",\"region\":\"EMEA\"}"),
            ("CN", "China", "{\"dialCode\":\"+86\",\"region\":\"APAC\"}"),
            ("HK", "Hong Kong", "{\"dialCode\":\"+852\",\"region\":\"APAC\"}"),
            ("TW", "Taiwan", "{\"dialCode\":\"+886\",\"region\":\"APAC\"}"),
            ("TH", "Thailand", "{\"dialCode\":\"+66\",\"region\":\"APAC\"}"),
            ("MY", "Malaysia", "{\"dialCode\":\"+60\",\"region\":\"APAC\"}"),
            ("ID", "Indonesia", "{\"dialCode\":\"+62\",\"region\":\"APAC\"}"),
            ("PH", "Philippines", "{\"dialCode\":\"+63\",\"region\":\"APAC\"}"),
            ("IN", "India", "{\"dialCode\":\"+91\",\"region\":\"APAC\"}"),
            ("NZ", "New Zealand", "{\"dialCode\":\"+64\",\"region\":\"APAC\"}"),
            ("AE", "United Arab Emirates", "{\"dialCode\":\"+971\",\"region\":\"EMEA\"}"),
            ("SA", "Saudi Arabia", "{\"dialCode\":\"+966\",\"region\":\"EMEA\"}"),
            ("IL", "Israel", "{\"dialCode\":\"+972\",\"region\":\"EMEA\"}"),
            ("BR", "Brazil", "{\"dialCode\":\"+55\",\"region\":\"LATAM\"}"),
            ("MX", "Mexico", "{\"dialCode\":\"+52\",\"region\":\"LATAM\"}"),
            ("ZA", "South Africa", "{\"dialCode\":\"+27\",\"region\":\"EMEA\"}")
        ];
        for (var i = 0; i < countries.Length; i++)
        {
            Add("country", countries[i].Code, countries[i].Label, i + 1, countries[i].Meta);
        }

        // IANA Timezones
        (string Code, string Label)[] timezones =
        [
            ("UTC", "UTC (Coordinated Universal Time)"),
            ("Asia/Ho_Chi_Minh", "Asia/Ho_Chi_Minh (UTC+07:00)"),
            ("Asia/Singapore", "Asia/Singapore (UTC+08:00)"),
            ("Asia/Bangkok", "Asia/Bangkok (UTC+07:00)"),
            ("Asia/Tokyo", "Asia/Tokyo (UTC+09:00)"),
            ("Asia/Seoul", "Asia/Seoul (UTC+09:00)"),
            ("Asia/Hong_Kong", "Asia/Hong_Kong (UTC+08:00)"),
            ("Asia/Shanghai", "Asia/Shanghai (UTC+08:00)"),
            ("Asia/Kolkata", "Asia/Kolkata (UTC+05:30)"),
            ("Asia/Dubai", "Asia/Dubai (UTC+04:00)"),
            ("Europe/London", "Europe/London (UTC+00:00)"),
            ("Europe/Paris", "Europe/Paris (UTC+01:00)"),
            ("Europe/Berlin", "Europe/Berlin (UTC+01:00)"),
            ("Europe/Amsterdam", "Europe/Amsterdam (UTC+01:00)"),
            ("Europe/Zurich", "Europe/Zurich (UTC+01:00)"),
            ("America/New_York", "America/New_York (UTC-05:00)"),
            ("America/Chicago", "America/Chicago (UTC-06:00)"),
            ("America/Denver", "America/Denver (UTC-07:00)"),
            ("America/Los_Angeles", "America/Los_Angeles (UTC-08:00)"),
            ("America/Toronto", "America/Toronto (UTC-05:00)"),
            ("America/Sao_Paulo", "America/Sao_Paulo (UTC-03:00)"),
            ("Australia/Sydney", "Australia/Sydney (UTC+10:00)"),
            ("Pacific/Auckland", "Pacific/Auckland (UTC+12:00)")
        ];
        for (var i = 0; i < timezones.Length; i++)
        {
            Add("timezone", timezones[i].Code, timezones[i].Label, i + 1);
        }

        // Locales (BCP-47)
        (string Code, string Label)[] locales =
        [
            ("en-US", "English (United States)"),
            ("vi-VN", "Vietnamese (Vietnam)"),
            ("en-GB", "English (United Kingdom)"),
            ("en-SG", "English (Singapore)"),
            ("en-AU", "English (Australia)"),
            ("ja-JP", "Japanese (Japan)"),
            ("ko-KR", "Korean (South Korea)"),
            ("zh-CN", "Chinese (Simplified, China)"),
            ("zh-TW", "Chinese (Traditional, Taiwan)"),
            ("fr-FR", "French (France)"),
            ("de-DE", "German (Germany)"),
            ("es-ES", "Spanish (Spain)"),
            ("th-TH", "Thai (Thailand)"),
            ("id-ID", "Indonesian (Indonesia)"),
            ("ms-MY", "Malay (Malaysia)")
        ];
        for (var i = 0; i < locales.Length; i++)
        {
            Add("locale", locales[i].Code, locales[i].Label, i + 1);
        }

        // Languages (ISO 639-1)
        (string Code, string Label)[] languages =
        [
            ("en", "English"),
            ("vi", "Vietnamese"),
            ("ja", "Japanese"),
            ("ko", "Korean"),
            ("zh", "Chinese"),
            ("fr", "French"),
            ("de", "German"),
            ("es", "Spanish"),
            ("th", "Thai"),
            ("id", "Indonesian"),
            ("ms", "Malay")
        ];
        for (var i = 0; i < languages.Length; i++)
        {
            Add("language", languages[i].Code, languages[i].Label, i + 1);
        }

        // Lifecycle Statuses
        string[] statuses = ["ACTIVE", "INACTIVE", "PENDING", "SUSPENDED", "ARCHIVED", "DRAFT", "APPROVED", "REJECTED"];
        for (var i = 0; i < statuses.Length; i++)
        {
            Add("status", statuses[i], statuses[i], i + 1);
        }

        if (itemsToAdd.Count > 0)
        {
            db.ReferenceDataItems.AddRange(itemsToAdd);
        }

        // ISO 4217 Currencies
        var existingCurrencies = (await db.Currencies.AsNoTracking()
            .Select(c => c.Code)
            .ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        (string Code, string Name, string Symbol, int Precision)[] currencies =
        [
            ("USD", "US Dollar", "$", 2),
            ("EUR", "Euro", "€", 2),
            ("VND", "Vietnamese Dong", "₫", 0),
            ("GBP", "British Pound Sterling", "£", 2),
            ("JPY", "Japanese Yen", "¥", 0),
            ("SGD", "Singapore Dollar", "S$", 2),
            ("AUD", "Australian Dollar", "A$", 2),
            ("CAD", "Canadian Dollar", "C$", 2),
            ("CHF", "Swiss Franc", "CHF", 2),
            ("CNY", "Chinese Yuan", "¥", 2),
            ("KRW", "South Korean Won", "₩", 0),
            ("THB", "Thai Baht", "฿", 2),
            ("MYR", "Malaysian Ringgit", "RM", 2),
            ("IDR", "Indonesian Rupiah", "Rp", 0),
            ("PHP", "Philippine Peso", "₱", 2),
            ("HKD", "Hong Kong Dollar", "HK$", 2),
            ("TWD", "New Taiwan Dollar", "NT$", 2),
            ("INR", "Indian Rupee", "₹", 2),
            ("AED", "UAE Dirham", "د.إ", 2),
            ("NZD", "New Zealand Dollar", "NZ$", 2),
            ("BRL", "Brazilian Real", "R$", 2),
            ("MXN", "Mexican Peso", "MX$", 2),
            ("SEK", "Swedish Krona", "kr", 2),
            ("NOK", "Norwegian Krone", "kr", 2),
            ("DKK", "Danish Krone", "kr", 2),
            ("PLN", "Polish Zloty", "zł", 2)
        ];

        var currenciesAdded = false;
        foreach (var cur in currencies)
        {
            if (existingCurrencies.Contains(cur.Code)) continue;
            var res = CurrencyDefinition.Create(cur.Code, cur.Name, cur.Symbol, cur.Precision, true);
            if (res.IsSuccess)
            {
                db.Currencies.Add(res.Value);
                currenciesAdded = true;
            }
        }

        if (itemsToAdd.Count > 0 || currenciesAdded)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<string>> ListCategoriesAsync(CancellationToken ct = default)
        => await db.ReferenceDataItems.AsNoTracking()
            .Select(r => r.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ReferenceDataItemDto>> ListByCategoryAsync(string category, string? search = null, bool includeInactive = false, CancellationToken ct = default)
    {
        var cat = category.Trim().ToLowerInvariant();
        var q = db.ReferenceDataItems.AsNoTracking().Where(r => r.Category == cat);
        if (!includeInactive)
        {
            q = q.Where(r => r.IsActive);
        }

        var items = await q.OrderBy(r => r.SortOrder).ThenBy(r => r.Label).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            items = items.Where(r =>
                r.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                r.Label.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return items.Select(MapRef).ToList();
    }

    public async Task<Result<ReferenceDataItemDto>> CreateItemAsync(CreateReferenceDataRequest request, CancellationToken ct = default)
    {
        var created = ReferenceDataItem.Create(
            request.Category, request.Code, request.Label, request.SortOrder,
            request.IsActive, request.LocalizedLabelsJson, request.MetadataJson);
        if (created.IsFailure) return created.Error;

        var item = created.Value;
        if (await db.ReferenceDataItems.AnyAsync(r => r.Category == item.Category && r.Code == item.Code, ct))
        {
            return Error.Conflict("REFDATA_DUPLICATE", $"Reference data item '{item.Category}:{item.Code}' already exists.");
        }

        db.ReferenceDataItems.Add(item);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "reference_data.create", "ReferenceDataItem", item.Id.ToString("D"), null, "Success",
            AfterState: new { item.Category, item.Code, item.Label, item.IsActive }), ct);

        return MapRef(item);
    }

    public async Task<Result<ReferenceDataItemDto>> UpdateItemAsync(Guid id, UpdateReferenceDataRequest request, CancellationToken ct = default)
    {
        var item = await db.ReferenceDataItems.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (item is null) return Error.NotFound("REFDATA_NOT_FOUND", "Reference data item not found.");

        item.Update(request.Label, request.IsActive, request.SortOrder, request.LocalizedLabelsJson, request.MetadataJson);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "reference_data.update", "ReferenceDataItem", item.Id.ToString("D"), null, "Success",
            AfterState: new { item.Category, item.Code, item.Label, item.IsActive }), ct);

        return MapRef(item);
    }

    public async Task<IReadOnlyList<CurrencyDto>> ListCurrenciesAsync(string? search = null, bool includeInactive = false, CancellationToken ct = default)
    {
        var q = db.Currencies.AsNoTracking();
        if (!includeInactive)
        {
            q = q.Where(c => c.IsActive);
        }

        var items = await q.OrderBy(c => c.Code).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            items = items.Where(c =>
                c.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                c.Symbol.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return items.Select(c => new CurrencyDto(c.Id, c.Code, c.Name, c.Symbol, c.MinorUnitPrecision, c.IsActive)).ToList();
    }

    public async Task<IReadOnlyList<FxRateDto>> GetLatestFxRatesAsync(string baseCurrency = "USD", CancellationToken ct = default)
    {
        var baseCode = baseCurrency.Trim().ToUpperInvariant();
        var now = clock.UtcNow;

        var rows = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.BaseCurrency == baseCode)
            .OrderByDescending(r => r.FetchedAt)
            .ToListAsync(ct);

        var latestPerQuote = rows
            .GroupBy(r => r.QuoteCurrency)
            .Select(g => g.First())
            .OrderBy(r => r.QuoteCurrency)
            .Select(r => MapFx(r, now))
            .ToList();

        return latestPerQuote;
    }

    public async Task<Result<FxRefreshResultDto>> RefreshFxRatesAsync(string baseCurrency = "USD", CancellationToken ct = default)
    {
        var baseCode = baseCurrency.Trim().ToUpperInvariant();
        var fetchRes = await fxProvider.FetchLatestAsync(baseCode, ct);
        if (fetchRes.IsFailure)
        {
            return fetchRes.Error;
        }

        var fetched = fetchRes.Value;
        var now = clock.UtcNow;
        var staleAfter = now.AddHours(24);
        var count = 0;

        foreach (var (quote, rate) in fetched.Rates)
        {
            var obs = ExchangeRateObservation.Create(
                fetched.BaseCurrency, quote, rate, fetched.Provider,
                fetched.ObservedAt, now, staleAfter, fetched.SourceReference);
            if (obs.IsSuccess)
            {
                db.ExchangeRates.Add(obs.Value);
                count++;
            }
        }

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "fx.rates.refresh", "ExchangeRateObservation", baseCode, null, "Success",
            AfterState: new { baseCode, fetched.Provider, RatesUpdated = count, fetched.ObservedAt }), ct);

        if (realtimePublisher is not null)
        {
            await realtimePublisher.PublishAsync(new PlatformRealtimeEvent(
                Guid.CreateVersion7().ToString("D"),
                "fx.rates.refreshed",
                now,
                null,
                "platform.configuration.read",
                "NEXUS_C",
                $"FX rates refreshed for base {baseCode} ({count} pairs from {fetched.Provider}).",
                new { baseCode, count, fetched.Provider }), ct);
        }

        return new FxRefreshResultDto(baseCode, fetched.Provider, count, fetched.ObservedAt, now, staleAfter, IndicativeDisclaimer);
    }

    public async Task<Result<FxRateDto>> RecordFxObservationAsync(RecordFxObservationRequest request, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var observedAt = request.ObservedAt ?? now;
        var ttl = Math.Clamp(request.TtlHours, 1, 720);
        var staleAfter = now.AddHours(ttl);

        var created = ExchangeRateObservation.Create(
            request.BaseCurrency, request.QuoteCurrency, request.Rate,
            request.Provider, observedAt, now, staleAfter, request.SourceReference);
        if (created.IsFailure) return created.Error;

        db.ExchangeRates.Add(created.Value);
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "fx.rate.recorded", "ExchangeRateObservation", created.Value.Id.ToString("D"), null, "Success",
            AfterState: new { created.Value.BaseCurrency, created.Value.QuoteCurrency, created.Value.Rate, created.Value.Provider }), ct);

        return MapFx(created.Value, now);
    }

    public async Task<Result<FxConversionResultDto>> ConvertCurrencyAsync(string fromCurrency, string toCurrency, decimal amount, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fromCurrency) || fromCurrency.Trim().Length != 3 ||
            string.IsNullOrWhiteSpace(toCurrency) || toCurrency.Trim().Length != 3)
        {
            return Error.Validation("FX_INVALID_CURRENCY", "FromCurrency and ToCurrency must be 3-letter ISO 4217 codes.");
        }

        var from = fromCurrency.Trim().ToUpperInvariant();
        var to = toCurrency.Trim().ToUpperInvariant();
        var now = clock.UtcNow;

        var targetDef = await db.Currencies.AsNoTracking().FirstOrDefaultAsync(c => c.Code == to, ct);
        var precision = targetDef?.MinorUnitPrecision ?? 2;

        if (from == to)
        {
            var sameRounded = decimal.Round(amount, precision, MidpointRounding.AwayFromZero);
            return new FxConversionResultDto(from, to, amount, sameRounded, precision, 1m, "Identity", now, now, false, IndicativeDisclaimer);
        }

        // Direct pair lookup
        var direct = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.BaseCurrency == from && r.QuoteCurrency == to)
            .OrderByDescending(r => r.FetchedAt)
            .FirstOrDefaultAsync(ct);

        if (direct is not null)
        {
            var converted = decimal.Round(amount * direct.Rate, precision, MidpointRounding.AwayFromZero);
            return new FxConversionResultDto(
                from, to, amount, converted, precision, direct.Rate,
                direct.Provider, direct.ObservedAt, direct.FetchedAt, now > direct.StaleAfter, IndicativeDisclaimer);
        }

        // Inverse pair lookup
        var inverse = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.BaseCurrency == to && r.QuoteCurrency == from)
            .OrderByDescending(r => r.FetchedAt)
            .FirstOrDefaultAsync(ct);

        if (inverse is not null)
        {
            var effectiveRate = decimal.Round(1m / inverse.Rate, 8, MidpointRounding.AwayFromZero);
            var converted = decimal.Round(amount / inverse.Rate, precision, MidpointRounding.AwayFromZero);
            return new FxConversionResultDto(
                from, to, amount, converted, precision, effectiveRate,
                inverse.Provider, inverse.ObservedAt, inverse.FetchedAt, now > inverse.StaleAfter, IndicativeDisclaimer);
        }

        // Cross-rate via USD pivot
        var usdToFrom = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.BaseCurrency == "USD" && r.QuoteCurrency == from)
            .OrderByDescending(r => r.FetchedAt)
            .FirstOrDefaultAsync(ct);
        var usdToTo = await db.ExchangeRates.AsNoTracking()
            .Where(r => r.BaseCurrency == "USD" && r.QuoteCurrency == to)
            .OrderByDescending(r => r.FetchedAt)
            .FirstOrDefaultAsync(ct);

        if (usdToFrom is not null && usdToTo is not null)
        {
            var crossRate = decimal.Round(usdToTo.Rate / usdToFrom.Rate, 8, MidpointRounding.AwayFromZero);
            var converted = decimal.Round(amount * (usdToTo.Rate / usdToFrom.Rate), precision, MidpointRounding.AwayFromZero);
            var oldestFetched = usdToFrom.FetchedAt < usdToTo.FetchedAt ? usdToFrom.FetchedAt : usdToTo.FetchedAt;
            var oldestObserved = usdToFrom.ObservedAt < usdToTo.ObservedAt ? usdToFrom.ObservedAt : usdToTo.ObservedAt;
            var isStale = now > usdToFrom.StaleAfter || now > usdToTo.StaleAfter;
            return new FxConversionResultDto(
                from, to, amount, converted, precision, crossRate,
                $"{usdToTo.Provider} (USD Cross)", oldestObserved, oldestFetched, isStale, IndicativeDisclaimer);
        }

        return Error.NotFound("FX_RATE_NOT_AVAILABLE", $"No FX reference rate observation found for pair {from}/{to}. Refresh FX rates first.");
    }

    private static ReferenceDataItemDto MapRef(ReferenceDataItem r)
        => new(r.Id, r.Category, r.Code, r.Label, r.IsActive, r.SortOrder, r.LocalizedLabelsJson, r.MetadataJson);

    private static FxRateDto MapFx(ExchangeRateObservation r, DateTimeOffset now)
        => new(r.Id, r.BaseCurrency, r.QuoteCurrency, r.Rate, r.Provider, r.ObservedAt, r.FetchedAt, r.StaleAfter, now > r.StaleAfter, r.SourceReference, IndicativeDisclaimer);
}
