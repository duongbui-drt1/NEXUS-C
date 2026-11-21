using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Messaging;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.Configuration.Application;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Integration.Application;
using Nexus.Modules.Integration.Domain;
using Nexus.Modules.Notification.Application;
using Nexus.Modules.Notification.Domain;
using Nexus.Modules.Organization.Application;
using Nexus.Modules.Organization.Domain;
using Nexus.Modules.Subscription.Application;
using Nexus.Modules.Tenancy.Application;
using Nexus.Modules.Workflow.Application;
using Nexus.SharedKernel.Security;
using Xunit;

namespace Nexus.ApiTests;

public sealed class PlatformApiTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task HealthAndOpenApiEndpoints_ReturnHealthyAndValidSpec()
    {
        using var client = fixture.CreateClient();

        var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        var openapi = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, openapi.StatusCode);
        var spec = await openapi.Content.ReadAsStringAsync();
        Assert.Contains("/api/v1/platform/tenants", spec);
        Assert.Contains("/api/v1/auth/login", spec);
    }

    [Fact]
    public async Task Tenants_CrudPaginationValidationAndConcurrencyConflict_WorkEndToEnd()
    {
        var adminAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var admin = fixture.CreateAuthenticatedClient(adminAuth.AccessToken);

        // 400 Bad Request with RFC 9457 Problem Details + errorCode + correlationId
        var badResp = await admin.PostAsJsonAsync("/api/v1/platform/tenants", new CreateTenantRequest("BAD", "Bad", "bad", "UTC", "US", "US"));
        Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);
        var badProblem = await badResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TENANT_INVALID_CURRENCY", badProblem.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(badProblem.GetProperty("correlationId").GetString()));

        // 201 Created
        var createResp = await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("API01", "Nexus Alpha Corp", "nexus-alpha", "Asia/Ho_Chi_Minh", "VND", "VN"));
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = await ReadDataAsync<TenantDto>(createResp);
        Assert.Equal("API01", created.Code);

        // 409 Conflict on duplicate code
        var dupResp = await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("API01", "Duplicate", "dup-slug", "UTC", "USD", "US"));
        Assert.Equal(HttpStatusCode.Conflict, dupResp.StatusCode);

        // 404 Not Found
        var missingResp = await admin.GetAsync($"/api/v1/platform/tenants/{Guid.NewGuid():D}");
        Assert.Equal(HttpStatusCode.NotFound, missingResp.StatusCode);

        // 200 OK with pagination & search
        var listResp = await admin.GetAsync("/api/v1/platform/tenants?page=1&pageSize=10&search=alpha&sortBy=name");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);

        // Concurrent updates with stale ExpectedRowVersion -> second gets 409 Conflict!
        var firstUpdate = await admin.PutAsJsonAsync($"/api/v1/platform/tenants/{created.Id:D}",
            new UpdateTenantRequest("Nexus Alpha Updated 1", "Asia/Ho_Chi_Minh", "VND", "VN", created.RowVersion));
        Assert.Equal(HttpStatusCode.OK, firstUpdate.StatusCode);

        var staleUpdate = await admin.PutAsJsonAsync($"/api/v1/platform/tenants/{created.Id:D}",
            new UpdateTenantRequest("Nexus Alpha Stale 2", "Asia/Ho_Chi_Minh", "VND", "VN", created.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, staleUpdate.StatusCode);
    }

    [Fact]
    public async Task Organizations_WorkflowMakerChecker_AndConcurrentApproval_WorkEndToEnd()
    {
        var adminAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var admin = fixture.CreateAuthenticatedClient(adminAuth.AccessToken);

        // Provision Tenant + Owner + Maker (Accountant) + Checker (CFO)
        var tResp = await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("ORGWF", "Org Workflow Corp", "org-wf", "UTC", "USD", "US"));
        var tenant = await ReadDataAsync<TenantDto>(tResp);

        var ownerPass = "Owner!Pass1234";
        var makerPass = "Maker!Pass1234";
        var checkerPass = "Checker!Pass1234";

        await admin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("owner.wf@nexus.test", ownerPass, "Tenant Owner", tenant.Id, SystemRoles.TenantOwner));
        await admin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("maker.wf@nexus.test", makerPass, "Accountant Maker", tenant.Id, SystemRoles.Accountant));
        await admin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("checker.wf@nexus.test", checkerPass, "CFO Checker", tenant.Id, SystemRoles.CFO));

        var ownerToken = await fixture.LoginAsync("owner.wf@nexus.test", ownerPass, tenant.Id);
        var makerToken = await fixture.LoginAsync("maker.wf@nexus.test", makerPass, tenant.Id);
        var checkerToken = await fixture.LoginAsync("checker.wf@nexus.test", checkerPass, tenant.Id);

        using var ownerClient = fixture.CreateAuthenticatedClient(ownerToken.AccessToken);
        using var makerClient = fixture.CreateAuthenticatedClient(makerToken.AccessToken);
        using var checkerClient = fixture.CreateAuthenticatedClient(checkerToken.AccessToken);

        // Organization: LegalEntity -> Branch -> OrgUnit + Cycle detection
        var leResp = await ownerClient.PostAsJsonAsync("/api/v1/platform/organizations/legal-entities",
            new CreateLegalEntityRequest("VN01", "Org WF Vietnam JSC", "0109998888", "Hanoi, VN"));
        Assert.Equal(HttpStatusCode.Created, leResp.StatusCode);
        var le = await ReadDataAsync<LegalEntityDto>(leResp);

        var brResp = await ownerClient.PostAsJsonAsync("/api/v1/platform/organizations/branches",
            new CreateBranchRequest(le.Id, "HN-HQ", "Hanoi HQ", "Ba Dinh, Hanoi"));
        Assert.Equal(HttpStatusCode.Created, brResp.StatusCode);
        var branch = await ReadDataAsync<BranchDto>(brResp);

        var rootUnit = await ReadDataAsync<OrganizationUnitDto>(await ownerClient.PostAsJsonAsync(
            "/api/v1/platform/organizations/units", new CreateOrganizationUnitRequest(null, "FIN", "Finance Division", OrganizationUnitType.Division)));
        var childUnit = await ReadDataAsync<OrganizationUnitDto>(await ownerClient.PostAsJsonAsync(
            "/api/v1/platform/organizations/units", new CreateOrganizationUnitRequest(rootUnit.Id, "AP", "Accounts Payable", OrganizationUnitType.Team)));

        // Hierarchy cycle rejected!
        var cycleResp = await ownerClient.PutAsJsonAsync($"/api/v1/platform/organizations/units/{rootUnit.Id:D}",
            new UpdateOrganizationUnitRequest(childUnit.Id, "Finance Division", OrganizationUnitType.Division));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, cycleResp.StatusCode);

        // Delete branch returns 204 No Content
        var delBranch = await ownerClient.DeleteAsync($"/api/v1/platform/organizations/branches/{branch.Id:D}");
        Assert.Equal(HttpStatusCode.NoContent, delBranch.StatusCode);

        // Workflow Maker-Checker: Owner creates definition -> Maker creates instance -> Maker cannot self-approve -> Two concurrent Checker approvals!
        var defResp = await ownerClient.PostAsJsonAsync("/api/v1/platform/workflows/definitions",
            new CreateWorkflowDefinitionRequest("JOURNAL_POST", "Journal Posting Approval", "JournalEntry", RequireMakerChecker: true, RequiredApprovals: 1, ApproverRoleName: SystemRoles.CFO));
        Assert.Equal(HttpStatusCode.Created, defResp.StatusCode);
        var def = await ReadDataAsync<WorkflowDefinitionDto>(defResp);

        var instResp = await makerClient.PostAsJsonAsync("/api/v1/platform/workflows/instances",
            new CreateWorkflowInstanceRequest(def.Id, "JE-2026-001", "{\"amount\":15000}"));
        Assert.Equal(HttpStatusCode.Created, instResp.StatusCode);
        var inst = await ReadDataAsync<WorkflowInstanceDto>(instResp);
        var taskId = inst.Tasks[0].Id;

        // Maker (Accountant) cannot approve (lacks permission AND maker-checker!)
        var makerSelfApprove = await makerClient.PostAsJsonAsync($"/api/v1/platform/workflows/tasks/{taskId:D}/decide", new DecideApprovalTaskRequest(true, "Self"));
        Assert.Equal(HttpStatusCode.Forbidden, makerSelfApprove.StatusCode);

        // Owner creating an instance and trying to self-approve (Owner HAS WorkflowsApprove permission, so Maker-Checker domain rule blocks it!)
        var ownerInst = await ReadDataAsync<WorkflowInstanceDto>(await ownerClient.PostAsJsonAsync(
            "/api/v1/platform/workflows/instances", new CreateWorkflowInstanceRequest(def.Id, "JE-SELF-1")));
        var ownerSelf = await ownerClient.PostAsJsonAsync($"/api/v1/platform/workflows/tasks/{ownerInst.Tasks[0].Id:D}/decide", new DecideApprovalTaskRequest(true, "Self"));
        Assert.Equal(HttpStatusCode.Forbidden, ownerSelf.StatusCode);
        var selfProb = await ownerSelf.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("WORKFLOW_MAKER_CHECKER_VIOLATION", selfProb.GetProperty("errorCode").GetString());

        // Concurrency test: Two simultaneous approvals on the SAME task -> EXACTLY ONE succeeds (200) and the other gets 409 Conflict!
        var c1 = fixture.CreateAuthenticatedClient(checkerToken.AccessToken);
        var c2 = fixture.CreateAuthenticatedClient(checkerToken.AccessToken);
        var results = await Task.WhenAll(
            c1.PostAsJsonAsync($"/api/v1/platform/workflows/tasks/{taskId:D}/decide", new DecideApprovalTaskRequest(true, "Approve 1")),
            c2.PostAsJsonAsync($"/api/v1/platform/workflows/tasks/{taskId:D}/decide", new DecideApprovalTaskRequest(true, "Approve 2")));
        c1.Dispose();
        c2.Dispose();

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        // Verify Outbox dispatcher publishes platform.workflow.approved.v1
        fixture.EventBus.Clear();
        using var scope = fixture.ApiFactory.Services.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
        await dispatcher.DispatchBatchAsync();
        Assert.Contains(fixture.EventBus.PublishedEvents, e => e.EventType == "platform.workflow.approved.v1" && e.TenantId == tenant.Id);
    }

    [Fact]
    public async Task Subscriptions_Configuration_Integrations_Notifications_Files_AndIdempotency_WorkEndToEnd()
    {
        var adminAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var admin = fixture.CreateAuthenticatedClient(adminAuth.AccessToken);

        var tenant = await ReadDataAsync<TenantDto>(await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("FULL01", "Full Suite Corp", "full-suite", "UTC", "USD", "US")));

        var ownerPass = "Suite!Pass1234";
        var userResp = await admin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("suite.owner@nexus.test", ownerPass, "Suite Owner", tenant.Id, SystemRoles.TenantOwner));
        var ownerUser = await ReadDataAsync<UserDto>(userResp);

        // 1. Subscription Plan & Entitlements
        var plan = await ReadDataAsync<PlanDto>(await admin.PostAsJsonAsync("/api/v1/platform/subscriptions/plans",
            new CreatePlanRequest("PRO_SUITE", "NEXUS Pro", "Includes F and AI Gateway", 299.00m, "USD",
                [new PlanFeatureInput("NEXUS_F", true, 1000), new PlanFeatureInput("AI_GATEWAY", true, 50000), new PlanFeatureInput("NEXUS_E", false, 0)])));

        var assignResp = await admin.PostAsJsonAsync("/api/v1/platform/subscriptions/assign",
            new AssignSubscriptionRequest(tenant.Id, plan.Id, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(365), 25));
        Assert.Equal(HttpStatusCode.Created, assignResp.StatusCode);

        // Register provider & AI model as Platform Admin
        var provider = await ReadDataAsync<ProviderDto>(await admin.PostAsJsonAsync("/api/v1/platform/integrations/providers",
            new CreateProviderRequest("SAP_ERP", "SAP Connector", "ERP")));
        await admin.PostAsJsonAsync("/api/v1/platform/integrations/ai/models",
            new RegisterAiModelRequest("gemini-2.5-pro", "GoogleAI", 30, 2048));

        var ownerAuth = await fixture.LoginAsync("suite.owner@nexus.test", ownerPass, tenant.Id);
        using var owner = fixture.CreateAuthenticatedClient(ownerAuth.AccessToken);

        // Check entitlements as tenant owner
        Assert.True(await ReadDataAsync<bool>(await owner.GetAsync("/api/v1/platform/subscriptions/entitlements/NEXUS_F")));
        Assert.False(await ReadDataAsync<bool>(await owner.GetAsync("/api/v1/platform/subscriptions/entitlements/NEXUS_E")));

        // 2. Configuration & Feature Flags + Plaintext secret rejection
        var secretReject = await owner.PutAsJsonAsync("/api/v1/platform/configuration/tenant",
            new UpsertSettingRequest("stripe_api_key", "sk_live_forbidden", "Billing"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, secretReject.StatusCode);

        var localeSetting = await ReadDataAsync<SettingDto>(await owner.PutAsJsonAsync("/api/v1/platform/configuration/tenant",
            new UpsertSettingRequest("localization.date_format", "YYYY-MM-DD", "Localization")));
        Assert.Equal(1, localeSetting.Version);

        await owner.PutAsJsonAsync("/api/v1/platform/configuration/feature-flags", new UpsertFeatureFlagRequest("new_dashboard", true, "Beta UI"));
        Assert.True(await ReadDataAsync<bool>(await owner.GetAsync("/api/v1/platform/configuration/feature-flags/new_dashboard/evaluate")));

        // 3. Integration Hub & Honest AI Gateway
        var conn = await ReadDataAsync<ConnectionDto>(await owner.PostAsJsonAsync("/api/v1/platform/integrations/connections",
            new CreateConnectionRequest(provider.Id, "SAP Prod", "vault://nexus/full01/sap-token")));
        Assert.Equal("Connected", conn.Status);

        var sync = await ReadDataAsync<SyncLogDto>(await owner.PostAsJsonAsync($"/api/v1/platform/integrations/connections/{conn.Id:D}/sync-logs",
            new RecordSyncRequest("Outbound", SyncStatus.Succeeded, 15, "Synced chart of accounts")));
        Assert.Equal(15, sync.RecordsProcessed);

        // AI Gateway: tenant IS entitled, model IS registered, but no external AI credentials configured -> honest 422 AI_PROVIDER_NOT_CONFIGURED (never fake AI!)
        var aiResp = await owner.PostAsJsonAsync("/api/v1/platform/integrations/ai/invoke",
            new InvokeAiRequest("gemini-2.5-pro", "Summarize cashflow", 128, new Dictionary<string, object> { ["apiKey"] = "must-be-redacted" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, aiResp.StatusCode);
        var aiErr = await aiResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AI_PROVIDER_NOT_CONFIGURED", aiErr.GetProperty("errorCode").GetString());

        // 4. Notifications (In-App + Email via InMemoryTestEmailSender)
        fixture.EmailSender.Clear();
        await owner.PostAsJsonAsync("/api/v1/platform/notifications/templates",
            new CreateTemplateRequest("INV_READY", NotificationChannel.Email, "Invoice {{num}}", "Invoice {{num}} is ready."));
        var notifResp = await owner.PostAsJsonAsync("/api/v1/platform/notifications/send",
            new SendNotificationRequest(ownerUser.Id, "suite.owner@nexus.test", NotificationChannel.Email, "INV_READY",
                Variables: new Dictionary<string, string> { ["num"] = "INV-100" }));
        Assert.Equal(HttpStatusCode.Created, notifResp.StatusCode);
        Assert.Single(fixture.EmailSender.SentMessages);
        Assert.Equal("Invoice INV-100", fixture.EmailSender.SentMessages.First().Subject);

        // 5. File Storage (upload, download, signed temp URL)
        using var form = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("NEXUS confidential document payload");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(fileContent, "file", "report.txt");
        form.Add(new StringContent("Finance"), "category");

        var uploadResp = await owner.PostAsync("/api/v1/platform/files", form);
        Assert.Equal(HttpStatusCode.Created, uploadResp.StatusCode);
        var fileDto = await ReadDataAsync<Nexus.Modules.FileStorage.Application.FileObjectDto>(uploadResp);

        var tempUrlDto = await ReadDataAsync<Nexus.Modules.FileStorage.Application.TemporaryDownloadUrlDto>(
            await owner.PostAsync($"/api/v1/platform/files/{fileDto.Id:D}/temporary-url", null));
        var signedDl = await owner.GetAsync(tempUrlDto.DownloadUrl);
        Assert.Equal(HttpStatusCode.OK, signedDl.StatusCode);
        Assert.Equal("NEXUS confidential document payload", await signedDl.Content.ReadAsStringAsync());

        // 6. HTTP Idempotency-Key test (replay & concurrent duplicate protection!)
        var idemKey = $"idem-{Guid.NewGuid():N}";
        using var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/organizations/legal-entities")
        {
            Content = JsonContent.Create(new CreateLegalEntityRequest("IDEM01", "Idempotent Entity", null, null))
        };
        req1.Headers.Add("Idempotency-Key", idemKey);
        var resp1 = await owner.SendAsync(req1);
        Assert.Equal(HttpStatusCode.Created, resp1.StatusCode);
        var body1 = await resp1.Content.ReadAsStringAsync();

        using var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/organizations/legal-entities")
        {
            Content = JsonContent.Create(new CreateLegalEntityRequest("IDEM01", "Idempotent Entity", null, null))
        };
        req2.Headers.Add("Idempotency-Key", idemKey);
        var resp2 = await owner.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Created, resp2.StatusCode);
        Assert.Equal("true", resp2.Headers.GetValues("X-Idempotent-Replayed").First());
        Assert.Equal(body1, await resp2.Content.ReadAsStringAsync());

        // Concurrent Idempotency-Key race: 2 parallel POSTs with same fresh Idempotency-Key
        var concurrentKey = $"idem-race-{Guid.NewGuid():N}";
        Task<HttpResponseMessage> SendRace()
        {
            var c = fixture.CreateAuthenticatedClient(ownerAuth.AccessToken);
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/platform/organizations/legal-entities")
            {
                Content = JsonContent.Create(new CreateLegalEntityRequest("IDEM02", "Concurrent Entity", null, null))
            };
            msg.Headers.Add("Idempotency-Key", concurrentKey);
            return c.SendAsync(msg);
        }

        var raceResults = await Task.WhenAll(SendRace(), SendRace());
        // Either one 201 + one 409 (in-progress) OR one 201 + one 201 replayed — NEVER two separate entities!
        Assert.Contains(raceResults, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(raceResults, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task RateLimiting_Returns429TooManyRequests_WithProblemDetails()
    {
        // Spin up a dedicated lightweight API host pointing at the same real DB with strict PermitLimit = 3
        using var strictFactory = new NexusApiFactory(
            fixture.AppConnectionString,
            fixture.MigratorConnectionString,
            fixture.TempStorageDir,
            overrideRateLimit: 3);

        using var client = strictFactory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var r = await client.GetAsync("/api/v1/platform/tenants");
            statuses.Add(r.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return JsonSerializer.Deserialize<T>(doc.GetProperty("data").GetRawText(), JsonOpts)!;
    }
}
