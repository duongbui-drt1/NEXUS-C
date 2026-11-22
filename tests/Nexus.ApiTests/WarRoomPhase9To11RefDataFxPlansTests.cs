using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.Configuration.Application;
using Nexus.Modules.Subscription.Application;
using Nexus.Modules.Tenancy.Application;
using Xunit;

namespace Nexus.ApiTests;

/// <summary>
/// War-Room Phases 9, 10 & 11 Tests:
/// - Shared reference data catalogs (countries, timezones, locales, languages, statuses) + search + CRUD
/// - ISO 4217 currencies + decimal-precision FX conversion + cross-rates + indicative disclaimer
/// - Subscription Plan safe delete (hard-delete when unused, safe archive preserving history when referenced)
/// </summary>
public sealed class WarRoomPhase9To11RefDataFxPlansTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task ReferenceData_AndCurrencyFxCatalogs_SupportSearchDecimalConversionAndIndicativeMetadata()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        // 1. Seeded categories & searchable country catalog
        var catsRes = await superAdmin.GetAsync("/api/v1/platform/reference-data/categories");
        Assert.Equal(HttpStatusCode.OK, catsRes.StatusCode);
        var categories = await catsRes.Content.ReadFromJsonAsync<List<string>>(JsonOpts);
        Assert.NotNull(categories);
        Assert.Contains("country", categories);
        Assert.Contains("timezone", categories);
        Assert.Contains("locale", categories);

        var vnRes = await superAdmin.GetAsync("/api/v1/platform/reference-data/country?search=Vietnam");
        Assert.Equal(HttpStatusCode.OK, vnRes.StatusCode);
        var vnList = await vnRes.Content.ReadFromJsonAsync<List<ReferenceDataItemDto>>(JsonOpts);
        Assert.NotNull(vnList);
        Assert.Contains(vnList, x => x.Code == "VN" && x.Label == "Vietnam");

        // 2. Seeded ISO 4217 currencies with exact minor unit precision
        var curRes = await superAdmin.GetAsync("/api/v1/platform/currencies?search=VND");
        Assert.Equal(HttpStatusCode.OK, curRes.StatusCode);
        var currencies = await curRes.Content.ReadFromJsonAsync<List<CurrencyDto>>(JsonOpts);
        Assert.NotNull(currencies);
        var vnd = Assert.Single(currencies, c => c.Code == "VND");
        Assert.Equal(0, vnd.MinorUnitPrecision);

        // 3. Record deterministic FX observations & verify exact decimal conversion (direct + cross-rate)
        var obsVnd = await superAdmin.PostAsJsonAsync("/api/v1/platform/fx/observations",
            new RecordFxObservationRequest("USD", "VND", 25450.50m, "TreasuryFeed", TtlHours: 24));
        Assert.Equal(HttpStatusCode.Created, obsVnd.StatusCode);

        var obsEur = await superAdmin.PostAsJsonAsync("/api/v1/platform/fx/observations",
            new RecordFxObservationRequest("USD", "EUR", 0.92m, "TreasuryFeed", TtlHours: 24));
        Assert.Equal(HttpStatusCode.Created, obsEur.StatusCode);

        var convDirectRes = await superAdmin.GetAsync("/api/v1/platform/fx/convert?from=USD&to=VND&amount=100");
        Assert.Equal(HttpStatusCode.OK, convDirectRes.StatusCode);
        var convDirect = await ReadDataAsync<FxConversionResultDto>(convDirectRes);
        Assert.Equal(2545050m, convDirect.ConvertedAmount);
        Assert.Equal(0, convDirect.TargetMinorUnitPrecision);
        Assert.False(convDirect.IsStale);
        Assert.Contains("Indicative FX reference rate", convDirect.Disclaimer);

        // Cross-rate EUR -> VND via USD pivot
        var convCrossRes = await superAdmin.GetAsync("/api/v1/platform/fx/convert?from=EUR&to=VND&amount=92");
        Assert.Equal(HttpStatusCode.OK, convCrossRes.StatusCode);
        var convCross = await ReadDataAsync<FxConversionResultDto>(convCrossRes);
        Assert.Equal(2545050m, convCross.ConvertedAmount);
        Assert.Contains("USD Cross", convCross.Provider);
    }

    [Fact]
    public async Task SubscriptionPlanDelete_HardDeletesUnusedPlan_AndSafelyArchivesReferencedPlan()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        // 1. Create an unused plan and delete it -> HardDeleted!
        var unusedPlanRes = await superAdmin.PostAsJsonAsync("/api/v1/platform/subscriptions/plans",
            new CreatePlanRequest("UNUSED_TMP", "Temporary Unused Plan", "Will be deleted", 19m, "USD",
                [new PlanFeatureInput("NEXUS_C", true, 5)]));
        Assert.Equal(HttpStatusCode.Created, unusedPlanRes.StatusCode);
        var unusedPlan = await ReadDataAsync<PlanDto>(unusedPlanRes);

        var delUnusedRes = await superAdmin.DeleteAsync($"/api/v1/platform/subscriptions/plans/{unusedPlan.Id}?reason=Cleanup");
        Assert.Equal(HttpStatusCode.OK, delUnusedRes.StatusCode);
        var delUnused = await ReadDataAsync<PlanDeletionResultDto>(delUnusedRes);
        Assert.Equal("HardDeleted", delUnused.Outcome);
        Assert.Equal(0, delUnused.ReferencedSubscriptionCount);

        // Verify unused plan no longer appears in catalog
        var plansAfterHardDelete = await ReadDataAsync<List<PlanDto>>(await superAdmin.GetAsync("/api/v1/platform/subscriptions/plans"));
        Assert.DoesNotContain(plansAfterHardDelete, p => p.Id == unusedPlan.Id);

        // 2. Create a plan, assign it to a tenant, then delete -> Safely Archived!
        var activePlanRes = await superAdmin.PostAsJsonAsync("/api/v1/platform/subscriptions/plans",
            new CreatePlanRequest("REF_ARCHIVE", "Referenced Plan", "Has active tenant", 99m, "USD",
                [new PlanFeatureInput("NEXUS_F", true, 50)]));
        Assert.Equal(HttpStatusCode.Created, activePlanRes.StatusCode);
        var activePlan = await ReadDataAsync<PlanDto>(activePlanRes);

        var tenant1 = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("WR11A", "WR11 Tenant A", "wr11-a", "UTC", "USD", "US")));
        var tenant2 = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("WR11B", "WR11 Tenant B", "wr11-b", "UTC", "USD", "US")));

        var now = DateTimeOffset.UtcNow;
        var assign1 = await superAdmin.PostAsJsonAsync("/api/v1/platform/subscriptions/assign",
            new AssignSubscriptionRequest(tenant1.Id, activePlan.Id, now.AddMinutes(-5), now.AddDays(30)));
        Assert.Equal(HttpStatusCode.Created, assign1.StatusCode);

        var archiveRes = await superAdmin.DeleteAsync($"/api/v1/platform/subscriptions/plans/{activePlan.Id}?reason=Retiring%20legacy%20tier");
        Assert.Equal(HttpStatusCode.OK, archiveRes.StatusCode);
        var archived = await ReadDataAsync<PlanDeletionResultDto>(archiveRes);
        Assert.Equal("Archived", archived.Outcome);
        Assert.Equal(1, archived.ReferencedSubscriptionCount);
        Assert.NotNull(archived.ArchivedAt);

        // Archived plan remains in catalog with IsActive=false (history intact)
        var plansAfterArchive = await ReadDataAsync<List<PlanDto>>(await superAdmin.GetAsync("/api/v1/platform/subscriptions/plans"));
        var archivedPlanDto = Assert.Single(plansAfterArchive, p => p.Id == activePlan.Id);
        Assert.False(archivedPlanDto.IsActive);
        Assert.NotNull(archivedPlanDto.ArchivedAt);

        // Archived plan CANNOT be assigned to a new tenant!
        var assignArchivedRes = await superAdmin.PostAsJsonAsync("/api/v1/platform/subscriptions/assign",
            new AssignSubscriptionRequest(tenant2.Id, activePlan.Id, now, now.AddDays(30)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, assignArchivedRes.StatusCode);
        Assert.Contains("PLAN_ARCHIVED", await assignArchivedRes.Content.ReadAsStringAsync());
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            return data.Deserialize<T>(JsonOpts)!;
        }
        return doc.RootElement.Deserialize<T>(JsonOpts)!;
    }
}
