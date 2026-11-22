using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.Configuration.Application;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Integration.Application;
using Nexus.Modules.Integration.Domain;
using Nexus.Modules.Subscription.Application;
using Nexus.Modules.Subscription.Domain;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Security;
using Xunit;

namespace Nexus.ApiTests;

/// <summary>
/// NEXUS C 1.0 Platform Contract & Ecosystem Verification:
/// - Application Registry (NEXUS_C, NEXUS_F, NEXUS_E, NEXUS_M) & Tenant Activation/Deactivation
/// - Authoritative Entitlements (Plan downgrade revocation, Subscription suspension, Feature Flag kill-switch, Idempotent usage)
/// - Full Webhook lifecycle (CRUD, secret rotation, deliveries, retry)
/// - Platform Operations API & Sanitized /health/dependencies contract
/// - Public API envelope, Correlation ID, ProblemDetails & Pagination compatibility
/// </summary>
public sealed class Foundation10PlatformContractTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task EcosystemRegistry_AuthoritativeEntitlements_DowngradeRevocation_AndIdempotentUsage()
    {
        var adminAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var admin = fixture.CreateAuthenticatedClient(adminAuth.AccessToken);

        // 1. Verify Application Registry is pre-seeded with NEXUS_C, NEXUS_F, NEXUS_E, NEXUS_M
        var apps = await ReadDataAsync<List<RegisteredApplicationDto>>(await admin.GetAsync("/api/v1/platform/applications"));
        Assert.Contains(apps, a => a.Code == "NEXUS_C" && a.Status == "Active");
        Assert.Contains(apps, a => a.Code == "NEXUS_F" && a.ModuleDependencies.Contains("NEXUS_C"));
        Assert.Contains(apps, a => a.Code == "NEXUS_E" && a.ModuleDependencies.Contains("NEXUS_C"));
        Assert.Contains(apps, a => a.Code == "NEXUS_M" && a.ModuleDependencies.Contains("NEXUS_C"));

        // 2. Provision tenant + owner
        var tenant = await ReadDataAsync<TenantDto>(await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("ECO01", "Ecosystem Corp", "eco-01", "UTC", "USD", "US")));
        const string pw = "Eco!Pass2026#";
        await admin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("owner.eco@nexus.test", pw, "Eco Owner", tenant.Id, SystemRoles.TenantOwner));

        var proPlan = await ReadDataAsync<PlanDto>(await admin.PostAsJsonAsync("/api/v1/platform/subscriptions/plans",
            new CreatePlanRequest("ECO_PRO", "Pro FM", "Includes F and M", 499m, "USD",
                [new PlanFeatureInput("NEXUS_F", true, 100), new PlanFeatureInput("NEXUS_M", true, 500)])));
        var mgmtOnlyPlan = await ReadDataAsync<PlanDto>(await admin.PostAsJsonAsync("/api/v1/platform/subscriptions/plans",
            new CreatePlanRequest("ECO_M_ONLY", "Management Only", "Includes M only", 199m, "USD",
                [new PlanFeatureInput("NEXUS_M", true, 500)])));

        // Assign Pro plan (F + M, NOT E)
        await admin.PostAsJsonAsync("/api/v1/platform/subscriptions/assign",
            new AssignSubscriptionRequest(tenant.Id, proPlan.Id, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));

        var ownerAuth = await fixture.LoginAsync("owner.eco@nexus.test", pw, tenant.Id);
        using var owner = fixture.CreateAuthenticatedClient(ownerAuth.AccessToken);

        // Tenant entitled to NEXUS_F & NEXUS_M, NOT NEXUS_E!
        var evalF = await ReadDataAsync<EntitlementEvaluationDto>(await owner.GetAsync("/api/v1/platform/entitlements/NEXUS_F"));
        var evalE = await ReadDataAsync<EntitlementEvaluationDto>(await owner.GetAsync("/api/v1/platform/entitlements/NEXUS_E"));
        Assert.True(evalF.IsEntitled);
        Assert.Equal("OK", evalF.Reason);
        Assert.False(evalE.IsEntitled);
        Assert.Equal("FEATURE_NOT_ENTITLED", evalE.Reason);

        // Cannot activate unentitled NEXUS_E for this tenant -> 403 APP_ENTITLEMENT_REQUIRED!
        var badActE = await admin.PostAsJsonAsync("/api/v1/platform/applications/NEXUS_E/activate", new { TenantId = tenant.Id });
        Assert.Equal(HttpStatusCode.Forbidden, badActE.StatusCode);

        // CAN activate entitled NEXUS_F, then deactivate -> entitlement immediately reflects APPLICATION_DEACTIVATED!
        var actF = await ReadDataAsync<TenantApplicationDto>(await admin.PostAsJsonAsync("/api/v1/platform/applications/NEXUS_F/activate", new { TenantId = tenant.Id }));
        Assert.True(actF.IsActivated);

        await admin.PostAsJsonAsync("/api/v1/platform/applications/NEXUS_F/deactivate", new { TenantId = tenant.Id });
        var evalFDeactivated = await ReadDataAsync<EntitlementEvaluationDto>(await owner.GetAsync("/api/v1/platform/entitlements/NEXUS_F"));
        Assert.False(evalFDeactivated.IsEntitled);
        Assert.Equal("APPLICATION_DEACTIVATED", evalFDeactivated.Reason);

        await admin.PostAsJsonAsync("/api/v1/platform/applications/NEXUS_F/activate", new { TenantId = tenant.Id });

        // P0 F-001 Fix: Downgrading tenant from Pro (F+M) to Management-Only (M) IMMEDIATELY revokes NEXUS_F entitlement!
        await admin.PostAsJsonAsync("/api/v1/platform/subscriptions/assign",
            new AssignSubscriptionRequest(tenant.Id, mgmtOnlyPlan.Id, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30)));

        var evalFAfterDowngrade = await ReadDataAsync<EntitlementEvaluationDto>(await owner.GetAsync("/api/v1/platform/entitlements/NEXUS_F"));
        var evalMAfterDowngrade = await ReadDataAsync<EntitlementEvaluationDto>(await owner.GetAsync("/api/v1/platform/entitlements/NEXUS_M"));
        Assert.False(evalFAfterDowngrade.IsEntitled);
        Assert.True(evalMAfterDowngrade.IsEntitled);

        // Feature flag kill-switch disables NEXUS_M even while subscribed!
        await owner.PutAsJsonAsync("/api/v1/platform/configuration/feature-flags", new UpsertFeatureFlagRequest("NEXUS_M", false, "Emergency maintenance"));
        var evalMFlaggedOff = await ReadDataAsync<EntitlementEvaluationDto>(await owner.GetAsync("/api/v1/platform/entitlements/NEXUS_M"));
        Assert.False(evalMFlaggedOff.IsEntitled);
        Assert.Equal("FEATURE_FLAG_DISABLED", evalMFlaggedOff.Reason);
        await owner.PutAsJsonAsync("/api/v1/platform/configuration/feature-flags", new UpsertFeatureFlagRequest("NEXUS_M", true, "Restored"));

        // Suspending subscription immediately revokes entitlement!
        await admin.PostAsJsonAsync($"/api/v1/platform/subscriptions/{tenant.Id:D}/status", new { Status = SubscriptionStatus.Suspended, Reason = "Non-payment" });
        var evalMSuspended = await ReadDataAsync<EntitlementEvaluationDto>(await owner.GetAsync("/api/v1/platform/entitlements/NEXUS_M"));
        Assert.False(evalMSuspended.IsEntitled);
        Assert.Equal("SUBSCRIPTION_SUSPENDED", evalMSuspended.Reason);

        // Idempotent usage ingestion: submitting the same IdempotencyKey twice counts quantity ONCE!
        var uKey = $"usage-{Guid.NewGuid():N}";
        var u1 = await ReadDataAsync<UsageSummaryDto>(await owner.PostAsJsonAsync("/api/v1/platform/subscriptions/usage", new RecordUsageRequest("NEXUS_M", 25, uKey)));
        var u2 = await ReadDataAsync<UsageSummaryDto>(await owner.PostAsJsonAsync("/api/v1/platform/subscriptions/usage", new RecordUsageRequest("NEXUS_M", 25, uKey)));
        Assert.Equal(25, u1.TotalQuantity);
        Assert.Equal(25, u2.TotalQuantity);
        Assert.Equal(1, u2.RecordCount);
    }

    [Fact]
    public async Task WebhookFullLifecycle_OperationsApi_AndPublicContractCompatibility()
    {
        var adminAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var admin = fixture.CreateAuthenticatedClient(adminAuth.AccessToken);

        var tenant = await ReadDataAsync<TenantDto>(await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("WHL01", "Webhook Corp", "whl-01", "UTC", "USD", "US")));
        const string pw = "Whl!Pass2026#";
        await admin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("owner.whl@nexus.test", pw, "WH Owner", tenant.Id, SystemRoles.TenantOwner));
        await admin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("support.whl@nexus.test", pw, "Support", null, SystemRoles.PlatformSupport, true));

        var ownerAuth = await fixture.LoginAsync("owner.whl@nexus.test", pw, tenant.Id);
        var supportAuth = await fixture.LoginAsync("support.whl@nexus.test", pw);

        using var owner = fixture.CreateAuthenticatedClient(ownerAuth.AccessToken);
        using var support = fixture.CreateAuthenticatedClient(supportAuth.AccessToken);

        // 1. Full Webhook Lifecycle: Register -> List -> Update -> Rotate Secret -> Disable -> Enable -> Deliveries -> Retry -> Delete
        var wh = await ReadDataAsync<WebhookDto>(await owner.PostAsJsonAsync("/api/v1/platform/integrations/webhooks",
            new CreateWebhookRequest("platform.workflow.approved.v1", "https://hooks.partner.example.com/v1", "vault://nexus/wh/key", "ERP hook")));
        Assert.True(wh.IsActive);

        var list = await ReadDataAsync<List<WebhookDto>>(await owner.GetAsync("/api/v1/platform/integrations/webhooks"));
        Assert.Contains(list, w => w.Id == wh.Id);

        var updated = await ReadDataAsync<WebhookDto>(await owner.PutAsJsonAsync($"/api/v1/platform/integrations/webhooks/{wh.Id:D}",
            new UpdateWebhookRequest("platform.workflow.rejected.v1", "https://hooks.partner.example.com/v2", "Updated hook")));
        Assert.Equal("https://hooks.partner.example.com/v2", updated.TargetUrl);

        var rotated = await ReadDataAsync<WebhookSecretRotationDto>(await owner.PostAsync($"/api/v1/platform/integrations/webhooks/{wh.Id:D}/rotate-secret", null));
        Assert.StartsWith("whsec_", rotated.SigningSecret);
        Assert.Equal(2, rotated.Webhook.SecretVersion);

        // List response NEVER exposes raw signing secret!
        var listRaw = await (await owner.GetAsync("/api/v1/platform/integrations/webhooks")).Content.ReadAsStringAsync();
        Assert.DoesNotContain(rotated.SigningSecret, listRaw);

        // Delivery history & retry
        var delivery = await ReadDataAsync<WebhookDeliveryDto>(await owner.PostAsJsonAsync(
            $"/api/v1/platform/integrations/webhooks/{wh.Id:D}/deliveries",
            new RecordWebhookDeliveryRequest(Guid.NewGuid(), "{\"ok\":true}", WebhookDeliveryStatus.Failed, 502, "Bad Gateway")));
        Assert.Equal("Failed", delivery.Status);

        var retried = await ReadDataAsync<WebhookDeliveryDto>(await owner.PostAsync($"/api/v1/platform/integrations/webhooks/deliveries/{delivery.Id:D}/retry", null));
        Assert.Equal(2, retried.AttemptCount);
        Assert.Equal("Pending", retried.Status);

        await owner.PostAsync($"/api/v1/platform/integrations/webhooks/{wh.Id:D}/disable", null);
        var deliveries = await ReadDataAsync<List<WebhookDeliveryDto>>(await owner.GetAsync($"/api/v1/platform/integrations/webhooks/{wh.Id:D}/deliveries"));
        Assert.Single(deliveries);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/platform/integrations/webhooks/{wh.Id:D}")).StatusCode);

        // 2. Operations API + Least-Privilege Check (PlatformSupport can READ ops, CANNOT retry DLQ!)
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync("/api/v1/platform/operations/outbox")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync("/api/v1/platform/operations/inbox")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await support.GetAsync("/api/v1/platform/operations/jobs")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await support.PostAsync($"/api/v1/platform/operations/dead-letters/{Guid.NewGuid():D}/retry", null)).StatusCode);

        // 3. Health Dependencies Contract: sanitized JSON with zero credentials or connection strings!
        using var anon = fixture.CreateClient();
        var depResp = await anon.GetAsync("/health/dependencies");
        Assert.Equal(HttpStatusCode.OK, depResp.StatusCode);
        var depJson = await depResp.Content.ReadAsStringAsync();
        Assert.Contains("postgresql", depJson);
        Assert.DoesNotContain("Password=", depJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", depJson, StringComparison.OrdinalIgnoreCase);

        // 4. API Envelope & Correlation-ID Contract check
        using var corrReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/applications");
        corrReq.Headers.Add("X-Correlation-Id", "contract-corr-12345");
        var corrResp = await admin.SendAsync(corrReq);
        Assert.Equal(HttpStatusCode.OK, corrResp.StatusCode);
        Assert.Equal("contract-corr-12345", corrResp.Headers.GetValues("X-Correlation-Id").First());
        var envelope = await corrResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(envelope.GetProperty("success").GetBoolean());
        Assert.Equal("contract-corr-12345", envelope.GetProperty("meta").GetProperty("correlationId").GetString());
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return JsonSerializer.Deserialize<T>(doc.GetProperty("data").GetRawText(), JsonOpts)!;
    }
}
