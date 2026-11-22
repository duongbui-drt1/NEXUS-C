using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Messaging;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Integration.Application;
using Nexus.Modules.Notification.Application;
using Nexus.Modules.Subscription.Application;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Security;
using Xunit;

namespace Nexus.SecurityTests;

/// <summary>
/// War-Room Phase 12 & Phase 13 Security Tests:
/// - Real-time SSE authentication, live event streaming, strict tenant isolation, permission filtering & Last-Event-ID replay
/// - AI Governance Policies: tenant RLS isolation, kill-switch (403), human approval gate (403), and monthly token budget (429)
/// </summary>
public sealed class WarRoomPhase12And13RealtimeAndAiTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task RealtimeSseStream_RequiresAuth_DeliversLiveEvents_AndEnforcesStrictTenantAndPermissionIsolation()
    {
        // 1. Unauthenticated SSE connection is rejected with 401
        using var anonClient = fixture.CreateClient();
        var anonRes = await anonClient.GetAsync("/api/v1/platform/realtime/stream?maxEvents=0");
        Assert.Equal(HttpStatusCode.Unauthorized, anonRes.StatusCode);

        // 2. Unit verification of PlatformRealtimeHub tenant isolation, permission filter, and Last-Event-ID replay
        var hub = new PlatformRealtimeHub();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var (subA, readerA, _) = hub.Subscribe(tenantA, isPlatformScope: false, [Permissions.IntegrationsRead]);
        var (subNoPerm, readerNoPerm, _) = hub.Subscribe(tenantA, isPlatformScope: false, [Permissions.UsersRead]);
        var (subPlatform, readerPlatform, _) = hub.Subscribe(null, isPlatformScope: true, [Permissions.IntegrationsRead]);

        var evt1 = new PlatformRealtimeEvent("evt-1", "webhook.delivery.completed", DateTimeOffset.UtcNow, tenantA, Permissions.IntegrationsRead, "NEXUS_C", "Tenant A webhook");
        var evt2 = new PlatformRealtimeEvent("evt-2", "webhook.delivery.completed", DateTimeOffset.UtcNow, tenantB, Permissions.IntegrationsRead, "NEXUS_C", "Tenant B secret webhook");
        var evt3 = new PlatformRealtimeEvent("evt-3", "webhook.delivery.completed", DateTimeOffset.UtcNow, tenantA, Permissions.IntegrationsRead, "NEXUS_C", "Tenant A second webhook");

        await hub.PublishAsync(evt1);
        await hub.PublishAsync(evt2);
        await hub.PublishAsync(evt3);

        // Tenant A receives evt-1 and evt-3, NEVER evt-2!
        Assert.True(readerA.TryRead(out var a1));
        Assert.Equal("evt-1", a1.EventId);
        Assert.True(readerA.TryRead(out var a2));
        Assert.Equal("evt-3", a2.EventId);
        Assert.False(readerA.TryRead(out _));

        // Subscriber without IntegrationsRead receives NOTHING
        Assert.False(readerNoPerm.TryRead(out _));

        // Platform admin with IntegrationsRead receives all 3
        Assert.True(readerPlatform.TryRead(out _));
        Assert.True(readerPlatform.TryRead(out _));
        Assert.True(readerPlatform.TryRead(out _));

        // Last-Event-ID replay for Tenant A starting after evt-1 replays ONLY evt-3 (never evt-2!)
        var (subReplay, _, replayed) = hub.Subscribe(tenantA, isPlatformScope: false, [Permissions.IntegrationsRead], lastEventId: "evt-1");
        var singleReplay = Assert.Single(replayed);
        Assert.Equal("evt-3", singleReplay.EventId);

        hub.Unsubscribe(subA);
        hub.Unsubscribe(subNoPerm);
        hub.Unsubscribe(subPlatform);
        hub.Unsubscribe(subReplay);

        // 3. End-to-end SSE stream over HTTP receives connected frame + live email.delivery.dispatched event
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var sseClient = fixture.CreateAuthenticatedClient(superAuth.AccessToken);
        using var actionClient = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var streamTask = sseClient.GetStringAsync("/api/v1/platform/realtime/stream?maxEvents=1");
        await Task.Delay(150);

        var sendRes = await actionClient.PostAsJsonAsync("/api/v1/platform/email/send",
            new SendTransactionalEmailRequest("sse-observer@example.test", "SSE Test", "Live event test", false, $"sse-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Created, sendRes.StatusCode);

        var sseBody = await streamTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("event: connected", sseBody);
        Assert.Contains("event: email.delivery.dispatched", sseBody);
    }

    [Fact]
    public async Task AiGovernancePolicies_EnforceKillSwitch_HumanApprovalGate_TokenBudget_AndTenantRlsIsolation()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        // Register AI model + plan with AI_GATEWAY entitlement
        var modelCode = $"gpt-gov-{Guid.NewGuid():N}"[..20];
        var modelRes = await superAdmin.PostAsJsonAsync("/api/v1/platform/integrations/ai/models",
            new RegisterAiModelRequest(modelCode, "TestProvider", 15, 1000));
        Assert.Equal(HttpStatusCode.Created, modelRes.StatusCode);

        var aiPlan = await ReadDataAsync<PlanDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/subscriptions/plans",
            new CreatePlanRequest($"AI_GOV_{Guid.NewGuid():N}"[..16], "AI Governed Tier", "Includes AI_GATEWAY", 199m, "USD",
                [new PlanFeatureInput("AI_GATEWAY", true, 500_000)])));

        // Provision Tenant A and Tenant B
        var tenantA = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("AIGOVA", "AI Gov Tenant A", "ai-gov-a", "UTC", "USD", "US")));
        var tenantB = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("AIGOVB", "AI Gov Tenant B", "ai-gov-b", "UTC", "USD", "US")));

        var now = DateTimeOffset.UtcNow;
        await superAdmin.PostAsJsonAsync("/api/v1/platform/subscriptions/assign",
            new AssignSubscriptionRequest(tenantA.Id, aiPlan.Id, now.AddMinutes(-5), now.AddDays(30)));

        const string pw = "StrongPass!2026#";
        await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("owner.aigova@demo.test", pw, "AI Gov A Owner", tenantA.Id, SystemRoles.TenantOwner));
        await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("owner.aigovb@demo.test", pw, "AI Gov B Owner", tenantB.Id, SystemRoles.TenantOwner));

        var authA = await fixture.LoginAsync("owner.aigova@demo.test", pw, tenantA.Id);
        var authB = await fixture.LoginAsync("owner.aigovb@demo.test", pw, tenantB.Id);
        using var clientA = fixture.CreateAuthenticatedClient(authA.AccessToken);
        using var clientB = fixture.CreateAuthenticatedClient(authB.AccessToken);

        // 1. Upsert policy with KillSwitchActive = true -> 403 AI_KILL_SWITCH_ACTIVE
        var killRes = await clientA.PutAsJsonAsync("/api/v1/platform/integrations/ai/policies",
            new UpsertAiPolicyRequest("INVOICE_AUTO", IsEnabled: true, KillSwitchActive: true, RequireHumanApproval: false, MonthlyTokenBudget: 10_000));
        Assert.Equal(HttpStatusCode.OK, killRes.StatusCode);

        var invokeKill = await clientA.PostAsJsonAsync("/api/v1/platform/integrations/ai/invoke",
            new InvokeAiRequest(modelCode, "Extract invoice totals", 100, CapabilityCode: "INVOICE_AUTO"));
        Assert.Equal(HttpStatusCode.Forbidden, invokeKill.StatusCode);
        Assert.Contains("AI_KILL_SWITCH_ACTIVE", await invokeKill.Content.ReadAsStringAsync());

        // 2. RLS Isolation: Tenant B listing AI policies sees ZERO of Tenant A's policies!
        var policiesB = await ReadDataAsync<List<AiCapabilityPolicyDto>>(await clientB.GetAsync("/api/v1/platform/integrations/ai/policies"));
        Assert.Empty(policiesB);

        // 3. Disable kill-switch, enable RequireHumanApproval -> 403 AI_HUMAN_APPROVAL_REQUIRED without token
        await clientA.PutAsJsonAsync("/api/v1/platform/integrations/ai/policies",
            new UpsertAiPolicyRequest("INVOICE_AUTO", IsEnabled: true, KillSwitchActive: false, RequireHumanApproval: true, MonthlyTokenBudget: 10_000));

        var invokeNoApproval = await clientA.PostAsJsonAsync("/api/v1/platform/integrations/ai/invoke",
            new InvokeAiRequest(modelCode, "Approve wire transfer", 100, CapabilityCode: "INVOICE_AUTO", IsConsequentialAction: true));
        Assert.Equal(HttpStatusCode.Forbidden, invokeNoApproval.StatusCode);
        Assert.Contains("AI_HUMAN_APPROVAL_REQUIRED", await invokeNoApproval.Content.ReadAsStringAsync());

        // 4. Low MonthlyTokenBudget (50) with MaxTokens=100 -> 429 AI_BUDGET_EXCEEDED
        await clientA.PutAsJsonAsync("/api/v1/platform/integrations/ai/policies",
            new UpsertAiPolicyRequest("INVOICE_AUTO", IsEnabled: true, KillSwitchActive: false, RequireHumanApproval: true, MonthlyTokenBudget: 50));

        var invokeOverBudget = await clientA.PostAsJsonAsync("/api/v1/platform/integrations/ai/invoke",
            new InvokeAiRequest(modelCode, "Summarize ledger", 100, CapabilityCode: "INVOICE_AUTO", HumanApprovalToken: "apr_verified_123"));
        Assert.Equal(HttpStatusCode.TooManyRequests, invokeOverBudget.StatusCode);
        Assert.Contains("AI_BUDGET_EXCEEDED", await invokeOverBudget.Content.ReadAsStringAsync());
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
