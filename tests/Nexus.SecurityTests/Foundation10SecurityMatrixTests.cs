using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.AccessControl.Application;
using Nexus.Modules.AccessControl.Domain;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Organization.Application;
using Nexus.Modules.Tenancy.Application;
using Nexus.Modules.Workflow.Application;
using Nexus.Modules.Workflow.Domain;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Security;
using Xunit;

namespace Nexus.SecurityTests;

/// <summary>
/// NEXUS C 1.0 Final Security Matrix:
/// - Organization scope & ABAC policy evaluation (Branch A vs Branch B, Explicit Deny)
/// - Cross-tenant Org assignment rejection (F-002 fix)
/// - Cookie refresh + CSRF guard + SwitchTenant refresh-token revocation (F-004 fix)
/// - Complete Workflow Security Matrix (Maker-Checker, Role Eligibility, Sequential Order, Expired Delegation, Version Pinning)
/// - OAuth 2.0 Client Credentials & Scoped API Key Security Matrix
/// </summary>
public sealed class Foundation10SecurityMatrixTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task OrganizationScope_AbacPolicy_ExplicitDeny_AndCrossTenantOrgAssignmentBlock()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var tenantA = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("ABACA", "ABAC Tenant A", "abac-a", "UTC", "USD", "US")));
        var tenantB = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("ABACB", "ABAC Tenant B", "abac-b", "UTC", "USD", "US")));

        const string pw = "Abac!Pass2026#";
        await superAdmin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("owner.abaca@nexus.test", pw, "Owner A", tenantA.Id, SystemRoles.TenantOwner));
        var empA = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("emp.abaca@nexus.test", pw, "Emp A", tenantA.Id, SystemRoles.Employee)));
        var empB = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("emp.abacb@nexus.test", pw, "Emp B", tenantB.Id, SystemRoles.Employee)));

        var ownerAAuth = await fixture.LoginAsync("owner.abaca@nexus.test", pw, tenantA.Id);
        var empAAuth = await fixture.LoginAsync("emp.abaca@nexus.test", pw, tenantA.Id);

        using var ownerAClient = fixture.CreateAuthenticatedClient(ownerAAuth.AccessToken);
        using var empAClient = fixture.CreateAuthenticatedClient(empAAuth.AccessToken);

        var le = await ReadDataAsync<LegalEntityDto>(await ownerAClient.PostAsJsonAsync("/api/v1/platform/organizations/legal-entities",
            new CreateLegalEntityRequest("LE-A", "ABAC Legal Entity", null, null)));
        var branchA = await ReadDataAsync<BranchDto>(await ownerAClient.PostAsJsonAsync("/api/v1/platform/organizations/branches",
            new CreateBranchRequest(le.Id, "BR-A", "Branch Alpha", null)));
        var branchB = await ReadDataAsync<BranchDto>(await ownerAClient.PostAsJsonAsync("/api/v1/platform/organizations/branches",
            new CreateBranchRequest(le.Id, "BR-B", "Branch Beta", null)));

        // P0 F-002 Fix: Tenant A Owner CANNOT assign Tenant B's user into Tenant A's organization!
        var badOrgAssign = await ownerAClient.PostAsJsonAsync("/api/v1/platform/organizations/assignments",
            new AssignUserOrganizationRequest(empB.Id, branchA.Id, null, "Intruder", true));
        Assert.Equal(HttpStatusCode.Forbidden, badOrgAssign.StatusCode);

        // Assign Emp A strictly to Branch A
        var goodOrgAssign = await ownerAClient.PostAsJsonAsync("/api/v1/platform/organizations/assignments",
            new AssignUserOrganizationRequest(empA.Id, branchA.Id, null, "Branch Clerk", true));
        Assert.Equal(HttpStatusCode.Created, goodOrgAssign.StatusCode);

        // Verify GET /api/v1/auth/organization-scope returns Branch A + LegalEntity, NOT Branch B
        var scopeDto = await ReadDataAsync<EffectiveOrganizationScope>(await empAClient.GetAsync("/api/v1/auth/organization-scope"));
        Assert.Contains(branchA.Id, scopeDto.BranchIds);
        Assert.DoesNotContain(branchB.Id, scopeDto.BranchIds);
        Assert.Contains(le.Id, scopeDto.LegalEntityIds);

        // Policy evaluation: Emp A is ALLOWED on Branch A, DENIED on Branch B!
        var allowA = await ReadDataAsync<AuthorizationDecision>(await empAClient.PostAsJsonAsync("/api/v1/auth/authorize",
            new { PermissionCode = Permissions.OrganizationsRead, ScopeType = "Branch", ScopeId = branchA.Id.ToString("D") }));
        Assert.True(allowA.Allowed);

        var denyB = await ReadDataAsync<AuthorizationDecision>(await empAClient.PostAsJsonAsync("/api/v1/auth/authorize",
            new { PermissionCode = Permissions.OrganizationsRead, ScopeType = "Branch", ScopeId = branchB.Id.ToString("D") }));
        Assert.False(denyB.Allowed);
        Assert.Equal("ORGANIZATION_SCOPE_DENIED", denyB.Reason);

        // Explicit DENY overrides ALLOW!
        var roles = await ReadDataAsync<List<RoleDto>>(await ownerAClient.GetAsync("/api/v1/platform/roles"));
        var empRole = roles.First(r => r.Name == SystemRoles.Employee);
        var denyAssign = await ownerAClient.PostAsJsonAsync("/api/v1/platform/roles/assign",
            new AssignRoleRequest(empA.Id, empRole.Id, "Branch", branchA.Id.ToString("D"), AuthorizationEffect.Deny));
        Assert.Equal(HttpStatusCode.Created, denyAssign.StatusCode);

        var deniedA = await ReadDataAsync<AuthorizationDecision>(await empAClient.PostAsJsonAsync("/api/v1/auth/authorize",
            new { PermissionCode = Permissions.OrganizationsRead, ScopeType = "Branch", ScopeId = branchA.Id.ToString("D") }));
        Assert.False(deniedA.Allowed);
        Assert.True(deniedA.ExplicitDeny);
        Assert.Equal("EXPLICIT_DENY", deniedA.Reason);
    }

    [Fact]
    public async Task CookieRefreshCsrfProtection_AndSwitchTenantRevokesPreviousTenantRefreshToken()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var tenantA = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("CKA", "Cookie Tenant A", "cookie-a", "UTC", "USD", "US")));
        var tenantB = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("CKB", "Cookie Tenant B", "cookie-b", "UTC", "USD", "US")));

        const string pw = "Cookie!Pass2026#";
        var user = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("multi.cookie@nexus.test", pw, "Multi User", tenantA.Id, SystemRoles.TenantAdmin)));
        await superAdmin.PostAsync($"/api/v1/platform/tenants/{tenantB.Id:D}/memberships/{user.Id:D}", null);

        // 1. Login with X-Nexus-Auth-Mode: cookie -> receives Set-Cookie __nexus_rt (HttpOnly; SameSite=Strict)
        using var anon = fixture.CreateClient();
        using var loginMsg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("multi.cookie@nexus.test", pw, tenantA.Id))
        };
        loginMsg.Headers.Add("X-Nexus-Auth-Mode", "cookie");
        var loginResp = await anon.SendAsync(loginMsg);
        loginResp.EnsureSuccessStatusCode();

        var setCookie = loginResp.Headers.GetValues("Set-Cookie").First();
        Assert.Contains("__nexus_rt=", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        var cookiePair = setCookie.Split(';')[0];

        // 2. Cookie refresh WITHOUT X-Nexus-CSRF: 1 MUST be blocked (403 CSRF_HEADER_REQUIRED)!
        using var noCsrfRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        noCsrfRefresh.Headers.Add("Cookie", cookiePair);
        var noCsrfResp = await anon.SendAsync(noCsrfRefresh);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrfResp.StatusCode);

        // 3. Cookie refresh WITH X-Nexus-CSRF: 1 succeeds and rotates token!
        using var csrfRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
        {
            Content = JsonContent.Create(new { })
        };
        csrfRefresh.Headers.Add("Cookie", cookiePair);
        csrfRefresh.Headers.Add("X-Nexus-CSRF", "1");
        var okRefreshResp = await anon.SendAsync(csrfRefresh);
        Assert.Equal(HttpStatusCode.OK, okRefreshResp.StatusCode);
        var refreshed = await ReadDataAsync<AuthTokenResponse>(okRefreshResp);

        // 4. SwitchTenant from Tenant A to Tenant B revokes Tenant A's refresh token (F-004 fix)!
        using var userClient = fixture.CreateAuthenticatedClient(refreshed.AccessToken);
        var switched = await ReadDataAsync<AuthTokenResponse>(await userClient.PostAsJsonAsync(
            "/api/v1/auth/switch-tenant", new SwitchTenantRequest(tenantB.Id)));
        Assert.Equal(tenantB.Id, switched.TenantId);

        var oldTenantRefreshAttempt = await anon.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(refreshed.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, oldTenantRefreshAttempt.StatusCode);

        // Replaying the revoked old tenant refresh token triggered reuse detection and revoked that session immediately!
        using var revokedSessionClient = fixture.CreateAuthenticatedClient(switched.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await revokedSessionClient.GetAsync("/api/v1/auth/me")).StatusCode);

        // 5. Using a clean active session in Tenant B, suspending Tenant A prevents switching into Tenant A (403 TENANT_SUSPENDED)!
        await superAdmin.PostAsJsonAsync($"/api/v1/platform/tenants/{tenantA.Id:D}/suspend", new SuspendTenantRequest("Hold"));
        var cleanTenantBLogin = await fixture.LoginAsync("multi.cookie@nexus.test", pw, tenantB.Id);
        using var tenantBClient = fixture.CreateAuthenticatedClient(cleanTenantBLogin.AccessToken);
        var switchSuspended = await tenantBClient.PostAsJsonAsync("/api/v1/auth/switch-tenant", new SwitchTenantRequest(tenantA.Id));
        Assert.Equal(HttpStatusCode.Forbidden, switchSuspended.StatusCode);
    }

    [Fact]
    public async Task WorkflowSecurityMatrix_SequentialOrder_RoleEligibility_DelegationExpiry_AndVersionPinning()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var tenant = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("WFSEC", "Workflow Security Corp", "wf-sec", "UTC", "USD", "US")));

        const string pw = "WfSec!Pass2026#";
        await superAdmin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("owner.wfs@nexus.test", pw, "Owner", tenant.Id, SystemRoles.TenantOwner));
        await superAdmin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("cfo.wfs@nexus.test", pw, "CFO", tenant.Id, SystemRoles.CFO));
        var dirUser = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("dir.wfs@nexus.test", pw, "Logistics Dir", tenant.Id, SystemRoles.LogisticsDirector)));
        await superAdmin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("acc.wfs@nexus.test", pw, "Accountant", tenant.Id, SystemRoles.Accountant));

        var ownerToken = await fixture.LoginAsync("owner.wfs@nexus.test", pw, tenant.Id);
        var cfoToken = await fixture.LoginAsync("cfo.wfs@nexus.test", pw, tenant.Id);
        var dirToken = await fixture.LoginAsync("dir.wfs@nexus.test", pw, tenant.Id);
        var accToken = await fixture.LoginAsync("acc.wfs@nexus.test", pw, tenant.Id);

        using var owner = fixture.CreateAuthenticatedClient(ownerToken.AccessToken);
        using var cfo = fixture.CreateAuthenticatedClient(cfoToken.AccessToken);
        using var dir = fixture.CreateAuthenticatedClient(dirToken.AccessToken);
        using var acc = fixture.CreateAuthenticatedClient(accToken.AccessToken);

        // Create 2-step SEQUENTIAL workflow assigned to CFO role with Maker-Checker enabled
        var def = await ReadDataAsync<WorkflowDefinitionDto>(await owner.PostAsJsonAsync("/api/v1/platform/workflows/definitions",
            new CreateWorkflowDefinitionRequest("PO_SEQ", "PO Sequential", "PurchaseOrder",
                RequireMakerChecker: true, RequiredApprovals: 2, ApproverRoleName: SystemRoles.CFO,
                ExpirationHours: 48, ApprovalMode: ApprovalMode.Sequential, EscalationHours: 12, EscalationRoleName: SystemRoles.TenantOwner)));

        var instV1 = await ReadDataAsync<WorkflowInstanceDto>(await acc.PostAsJsonAsync("/api/v1/platform/workflows/instances",
            new CreateWorkflowInstanceRequest(def.Id, "PO-100")));
        Assert.Equal(2, instV1.Tasks.Count);
        var step1 = instV1.Tasks[0];
        var step2 = instV1.Tasks[1];

        // 1. Sequential ordering: CFO CANNOT approve Step 2 before Step 1!
        var outOfOrder = await cfo.PostAsJsonAsync($"/api/v1/platform/workflows/tasks/{step2.Id:D}/decide", new DecideApprovalTaskRequest(true, "Skip step 1"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, outOfOrder.StatusCode);

        // 2. Role eligibility: LogisticsDirector HAS platform.workflows.approve permission, but does NOT hold CFO role -> 403 WORKFLOW_APPROVER_NOT_ELIGIBLE!
        var wrongRole = await dir.PostAsJsonAsync($"/api/v1/platform/workflows/tasks/{step1.Id:D}/decide", new DecideApprovalTaskRequest(true, "Wrong role"));
        Assert.Equal(HttpStatusCode.Forbidden, wrongRole.StatusCode);

        // 3. Expired delegation CANNOT be used!
        var expiredDelegation = await cfo.PostAsJsonAsync("/api/v1/platform/workflows/delegations",
            new CreateApprovalDelegationRequest(dirUser.Id, def.Id, DateTimeOffset.UtcNow.AddHours(-5), DateTimeOffset.UtcNow.AddHours(-1), "Past leave"));
        Assert.Equal(HttpStatusCode.Created, expiredDelegation.StatusCode);

        var stillBlocked = await dir.PostAsJsonAsync($"/api/v1/platform/workflows/tasks/{step1.Id:D}/decide", new DecideApprovalTaskRequest(true, "Expired delegation"));
        Assert.Equal(HttpStatusCode.Forbidden, stillBlocked.StatusCode);

        // 4. Active valid delegation ALLOWS LogisticsDirector to approve Step 1 on CFO's behalf!
        var activeDelegation = await cfo.PostAsJsonAsync("/api/v1/platform/workflows/delegations",
            new CreateApprovalDelegationRequest(dirUser.Id, def.Id, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(2), "Active delegation"));
        Assert.Equal(HttpStatusCode.Created, activeDelegation.StatusCode);

        var step1Ok = await dir.PostAsJsonAsync($"/api/v1/platform/workflows/tasks/{step1.Id:D}/decide", new DecideApprovalTaskRequest(true, "Approved via valid delegation"));
        Assert.Equal(HttpStatusCode.OK, step1Ok.StatusCode);

        // 5. Publish v2 with RequireMakerChecker=false & 1 approval -> existing v1 instance MUST remain pinned to v1 (still requires step 2)!
        var defV2 = await ReadDataAsync<WorkflowDefinitionDto>(await owner.PostAsJsonAsync(
            $"/api/v1/platform/workflows/definitions/{def.Id:D}/versions",
            new PublishWorkflowVersionRequest(RequiredApprovals: 1, ApproverRoleName: SystemRoles.CFO, RequireMakerChecker: false, ApprovalMode: ApprovalMode.Parallel)));
        Assert.Equal(2, defV2.CurrentVersionNumber);

        var reloadedV1 = await ReadDataAsync<WorkflowInstanceDto>(await owner.GetAsync($"/api/v1/platform/workflows/instances/{instV1.Id:D}"));
        Assert.Equal("Pending", reloadedV1.Status);
        Assert.Equal(def.LatestVersionId, reloadedV1.VersionId);

        // 6. Escalate instance -> records escalation & history!
        var escalated = await ReadDataAsync<WorkflowInstanceDto>(await owner.PostAsync($"/api/v1/platform/workflows/instances/{instV1.Id:D}/escalate", null));
        Assert.NotNull(escalated.Tasks[1].EscalatedAt);

        // 7. Complete Step 2 as CFO -> Instance becomes Approved, full history recorded!
        var finalInst = await ReadDataAsync<WorkflowInstanceDto>(await cfo.PostAsJsonAsync(
            $"/api/v1/platform/workflows/tasks/{step2.Id:D}/decide", new DecideApprovalTaskRequest(true, "Final approval")));
        Assert.Equal("Approved", finalInst.Status);

        var history = await ReadDataAsync<List<WorkflowHistoryDto>>(await owner.GetAsync($"/api/v1/platform/workflows/instances/{instV1.Id:D}/history"));
        Assert.True(history.Count >= 4);
    }

    [Fact]
    public async Task ServiceToServiceOAuth2ClientCredentials_AndScopedApiKeys_EnforceLeastPrivilegeAndImmediateRevocation()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var tenantA = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("SVCA", "Service Tenant A", "svc-a", "UTC", "USD", "US")));
        var tenantB = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("SVCB", "Service Tenant B", "svc-b", "UTC", "USD", "US")));

        // 1. Wildcard scope MUST be rejected (403 WILDCARD_SCOPE_FORBIDDEN)
        var wildcardAttempt = await superAdmin.PostAsJsonAsync("/api/v1/platform/api-clients",
            new CreateServiceClientRequest("bad-client", "Wildcard", "NEXUS_F", tenantA.Id, ["*"]));
        Assert.Equal(HttpStatusCode.Forbidden, wildcardAttempt.StatusCode);

        // 2. Register tenant-bound NEXUS_F service client with strictly scoped read permissions
        var createdClient = await ReadDataAsync<ServiceClientWithSecretDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/api-clients",
            new CreateServiceClientRequest("nexus-f-tenant-a", "NEXUS F Service for Tenant A", "NEXUS_F", tenantA.Id,
                [Permissions.OrganizationsRead, Permissions.SubscriptionsRead])));
        Assert.StartsWith("nxs_", createdClient.ClientSecret);

        // 3. Exchange OAuth 2.0 client_credentials token
        using var anon = fixture.CreateClient();
        // Requesting ungranted scope -> 403 INVALID_SCOPE
        var overScope = await anon.PostAsJsonAsync("/api/v1/auth/token",
            new ClientCredentialsTokenRequest("client_credentials", "nexus-f-tenant-a", createdClient.ClientSecret, Permissions.TenantsCreate, tenantA.Id));
        Assert.Equal(HttpStatusCode.Forbidden, overScope.StatusCode);

        // Requesting foreign tenant -> 403 CLIENT_TENANT_MISMATCH
        var wrongTenant = await anon.PostAsJsonAsync("/api/v1/auth/token",
            new ClientCredentialsTokenRequest("client_credentials", "nexus-f-tenant-a", createdClient.ClientSecret, Permissions.OrganizationsRead, tenantB.Id));
        Assert.Equal(HttpStatusCode.Forbidden, wrongTenant.StatusCode);

        // Valid token exchange
        var svcToken = await ReadDataAsync<ServiceTokenResponse>(await anon.PostAsJsonAsync("/api/v1/auth/token",
            new ClientCredentialsTokenRequest("client_credentials", "nexus-f-tenant-a", createdClient.ClientSecret, Permissions.OrganizationsRead, tenantA.Id)));

        using var svcHttp = fixture.CreateAuthenticatedClient(svcToken.AccessToken);
        // Allowed scope succeeds (200)
        Assert.Equal(HttpStatusCode.OK, (await svcHttp.GetAsync("/api/v1/platform/organizations/legal-entities")).StatusCode);
        // Ungranted scope denied (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await svcHttp.PostAsJsonAsync("/api/v1/platform/organizations/legal-entities",
            new CreateLegalEntityRequest("NOPE", "No Write", null, null))).StatusCode);
        // Tenant spoofing header blocked (403)
        using var spoof = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/organizations/legal-entities");
        spoof.Headers.Add("X-Tenant-Id", tenantB.Id.ToString("D"));
        Assert.Equal(HttpStatusCode.Forbidden, (await svcHttp.SendAsync(spoof)).StatusCode);

        // 4. Revoke service client -> IN-FLIGHT token is immediately rejected (401)!
        Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.PostAsync($"/api/v1/platform/api-clients/{createdClient.Client.Id:D}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await svcHttp.GetAsync("/api/v1/platform/organizations/legal-entities")).StatusCode);

        // 5. Scoped API Key (X-Api-Key) lifecycle: create -> use allowed -> denied ungranted -> revoke -> 401!
        var createdKey = await ReadDataAsync<ApiKeyWithSecretDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/api-keys",
            new CreateApiKeyRequest("Tenant A Org Reader", [Permissions.OrganizationsRead], DateTimeOffset.UtcNow.AddDays(30), tenantA.Id)));
        Assert.StartsWith("nxk_", createdKey.RawApiKey);

        using var keyClient = fixture.CreateClient();
        keyClient.DefaultRequestHeaders.Add("X-Api-Key", createdKey.RawApiKey);
        Assert.Equal(HttpStatusCode.OK, (await keyClient.GetAsync("/api/v1/platform/organizations/legal-entities")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await keyClient.GetAsync("/api/v1/platform/tenants")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.DeleteAsync($"/api/v1/platform/api-keys/{createdKey.ApiKey.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await keyClient.GetAsync("/api/v1/platform/organizations/legal-entities")).StatusCode);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return JsonSerializer.Deserialize<T>(doc.GetProperty("data").GetRawText(), JsonOpts)!;
    }
}
