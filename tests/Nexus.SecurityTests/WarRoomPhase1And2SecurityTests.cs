using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.AccessControl.Application;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Identity.Domain;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Security;
using Xunit;

namespace Nexus.SecurityTests;

/// <summary>
/// War-Room Phase 1 & Phase 2 Adversarial Security Tests:
/// - Application-scoped role isolation (NEXUS_F role never authorizes NEXUS_M)
/// - Cross-application API key / OAuth2 credential escape prevention (403 APPLICATION_SCOPE_VIOLATION)
/// - Partner credential platform-scope prohibition & ApplicationCode requirement
/// - Secret/key rotation overlap grace window + immediate full revocation
/// - Cross-tenant membership add/revoke/reactivate regression
/// </summary>
public sealed class WarRoomPhase1And2SecurityTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task ApplicationScopedRoles_AndMachineCredentials_EnforceStrictAppIsolationAndRotationOverlap()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var tenant = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("APPISO", "App Isolation Tenant", "app-iso", "UTC", "USD", "US")));

        const string pw = "AppIso!2026#";
        await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("owner.appiso@nexus.test", pw, "Owner AppIso", tenant.Id, SystemRoles.TenantOwner));
        var finUser = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("fin.appiso@nexus.test", pw, "Finance Only User", tenant.Id, null)));

        var ownerAuth = await fixture.LoginAsync("owner.appiso@nexus.test", pw, tenant.Id);
        using var ownerClient = fixture.CreateAuthenticatedClient(ownerAuth.AccessToken);

        // 1. Create an application-scoped role strictly bound to NEXUS_F
        var finRoleRes = await ownerClient.PostAsJsonAsync("/api/v1/platform/roles",
            new CreateRoleRequest("FinanceLedgerReader", "NEXUS F scoped role", [Permissions.ConfigurationRead], ApplicationCode: "NEXUS_F"));
        Assert.Equal(HttpStatusCode.Created, finRoleRes.StatusCode);
        var finRole = await ReadDataAsync<RoleDto>(finRoleRes);
        Assert.Equal("NEXUS_F", finRole.ApplicationCode);

        var assignRes = await ownerClient.PostAsJsonAsync("/api/v1/platform/roles/assign",
            new AssignRoleRequest(finUser.Id, finRole.Id));
        Assert.Equal(HttpStatusCode.Created, assignRes.StatusCode);

        var finAuth = await fixture.LoginAsync("fin.appiso@nexus.test", pw, tenant.Id);
        using var finClient = fixture.CreateAuthenticatedClient(finAuth.AccessToken);

        // Allowed for NEXUS_F, DENIED for NEXUS_M!
        var allowF = await ReadDataAsync<AuthorizationDecision>(await finClient.PostAsJsonAsync("/api/v1/auth/authorize",
            new { PermissionCode = Permissions.ConfigurationRead, ApplicationCode = "NEXUS_F" }));
        Assert.True(allowF.Allowed);

        var denyM = await ReadDataAsync<AuthorizationDecision>(await finClient.PostAsJsonAsync("/api/v1/auth/authorize",
            new { PermissionCode = Permissions.ConfigurationRead, ApplicationCode = "NEXUS_M" }));
        Assert.False(denyM.Allowed);
        Assert.Equal("APPLICATION_SCOPE_DENIED", denyM.Reason);

        // 2. Partner credential MUST NOT hold platform-admin scopes and MUST bind ApplicationCode
        var badPartnerScope = await superAdmin.PostAsJsonAsync("/api/v1/platform/api-clients",
            new CreateServiceClientRequest("partner-evil", "Evil Partner", "PARTNER_X", null, [Permissions.TenantsCreate], 60, CredentialClass.Partner));
        Assert.Equal(HttpStatusCode.Forbidden, badPartnerScope.StatusCode);

        var badPartnerNoApp = await superAdmin.PostAsJsonAsync("/api/v1/platform/api-clients",
            new CreateServiceClientRequest("partner-noapp", "NoApp Partner", null, tenant.Id, [Permissions.OrganizationsRead], 60, CredentialClass.Partner));
        Assert.Equal(HttpStatusCode.BadRequest, badPartnerNoApp.StatusCode);

        // 3. Application-bound API key (NEXUS_F) cannot escape to NEXUS_M via X-Application-Code!
        var keyCreateRes = await superAdmin.PostAsJsonAsync("/api/v1/platform/api-keys",
            new CreateApiKeyRequest("NEXUS F Connector Key", [Permissions.OrganizationsRead], DateTimeOffset.UtcNow.AddDays(30), tenant.Id, CredentialClass.Application, "NEXUS_F"));
        Assert.Equal(HttpStatusCode.Created, keyCreateRes.StatusCode);
        var createdKey = await ReadDataAsync<ApiKeyWithSecretDto>(keyCreateRes);
        Assert.Equal("NEXUS_F", createdKey.ApiKey.ApplicationCode);

        using var keyClient = fixture.CreateClient();
        using var okReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/organizations/legal-entities");
        okReq.Headers.Add("X-Api-Key", createdKey.RawApiKey);
        okReq.Headers.Add("X-Application-Code", "NEXUS_F");
        Assert.Equal(HttpStatusCode.OK, (await keyClient.SendAsync(okReq)).StatusCode);

        using var escapeReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/organizations/legal-entities");
        escapeReq.Headers.Add("X-Api-Key", createdKey.RawApiKey);
        escapeReq.Headers.Add("X-Application-Code", "NEXUS_M");
        Assert.Equal(HttpStatusCode.Forbidden, (await keyClient.SendAsync(escapeReq)).StatusCode);

        // 4. API key rotation with 15-minute overlap grace window: BOTH old and new keys work, then revoke kills BOTH!
        var rotRes = await superAdmin.PostAsJsonAsync($"/api/v1/platform/api-keys/{createdKey.ApiKey.Id}/rotate",
            new { OverlapMinutes = 15 });
        Assert.Equal(HttpStatusCode.OK, rotRes.StatusCode);
        var rotatedKey = await ReadDataAsync<ApiKeyWithSecretDto>(rotRes);
        Assert.NotNull(rotatedKey.ApiKey.PreviousKeyValidUntil);

        using var oldOverlapReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/organizations/legal-entities");
        oldOverlapReq.Headers.Add("X-Api-Key", createdKey.RawApiKey);
        Assert.Equal(HttpStatusCode.OK, (await keyClient.SendAsync(oldOverlapReq)).StatusCode);

        using var newOverlapReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/organizations/legal-entities");
        newOverlapReq.Headers.Add("X-Api-Key", rotatedKey.RawApiKey);
        Assert.Equal(HttpStatusCode.OK, (await keyClient.SendAsync(newOverlapReq)).StatusCode);

        // Immediate revocation invalidates BOTH current and overlap keys
        Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.DeleteAsync($"/api/v1/platform/api-keys/{createdKey.ApiKey.Id}")).StatusCode);

        using var afterRevokeOld = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/organizations/legal-entities");
        afterRevokeOld.Headers.Add("X-Api-Key", createdKey.RawApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await keyClient.SendAsync(afterRevokeOld)).StatusCode);

        using var afterRevokeNew = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/organizations/legal-entities");
        afterRevokeNew.Headers.Add("X-Api-Key", rotatedKey.RawApiKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await keyClient.SendAsync(afterRevokeNew)).StatusCode);
    }

    [Fact]
    public async Task CrossTenantMembership_AddRevokeReactivate_IsStrictlyBlockedForTenantAdmin()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var tenantA = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("MEMA", "Membership Tenant A", "mema", "UTC", "USD", "US")));
        var tenantB = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("MEMB", "Membership Tenant B", "memb", "UTC", "USD", "US")));

        const string pw = "MemGuard!2026#";
        await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("ta.mema@nexus.test", pw, "TA A", tenantA.Id, SystemRoles.TenantAdmin));
        var userB = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("user.memb@nexus.test", pw, "User B", tenantB.Id, SystemRoles.Employee)));

        var taAuth = await fixture.LoginAsync("ta.mema@nexus.test", pw, tenantA.Id);
        using var taClient = fixture.CreateAuthenticatedClient(taAuth.AccessToken);

        // TenantAdmin A CANNOT grant or revoke memberships in Tenant B
        var crossAdd = await taClient.PostAsync($"/api/v1/platform/tenants/{tenantB.Id}/memberships/{userB.Id}", null);
        Assert.Equal(HttpStatusCode.Forbidden, crossAdd.StatusCode);

        var crossRevoke = await taClient.DeleteAsync($"/api/v1/platform/tenants/{tenantB.Id}/memberships/{userB.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, crossRevoke.StatusCode);

        // Verify User B's membership in Tenant B was NOT mutated (can still log into Tenant B)
        var userBAuth = await fixture.LoginAsync("user.memb@nexus.test", pw, tenantB.Id);
        Assert.False(string.IsNullOrWhiteSpace(userBAuth.AccessToken));

        // PlatformSuperAdmin CAN revoke and reactivate membership in Tenant B cleanly
        Assert.Equal(HttpStatusCode.NoContent, (await superAdmin.DeleteAsync($"/api/v1/platform/tenants/{tenantB.Id}/memberships/{userB.Id}")).StatusCode);
        var reactivate = await superAdmin.PostAsync($"/api/v1/platform/tenants/{tenantB.Id}/memberships/{userB.Id}", null);
        Assert.Equal(HttpStatusCode.Created, reactivate.StatusCode);
    }

    [Fact]
    public async Task PlatformAdmin_CanSwitchIntoTenantContext_RetainPlatformPermissions_AndSwitchBackToGlobal()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var tenant = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("SWCTX", "Switch Context Tenant", "sw-ctx", "UTC", "USD", "US")));

        // 1. PlatformSuperAdmin sees all non-archived tenants in /tenants/accessible even without membership rows
        var accessible = await ReadDataAsync<List<TenantDto>>(await superAdmin.GetAsync("/api/v1/platform/tenants/accessible"));
        Assert.Contains(accessible, t => t.Id == tenant.Id);

        // 2. Switch into tenant context
        var switched = await ReadDataAsync<AuthTokenResponse>(await superAdmin.PostAsJsonAsync("/api/v1/auth/switch-tenant",
            new SwitchTenantRequest(tenant.Id)));
        Assert.Equal(tenant.Id, switched.TenantId);

        using var scopedAdmin = fixture.CreateAuthenticatedClient(switched.AccessToken);

        // 3. Effective permissions retain platform.integrations.read & all PlatformSuperAdmin permissions!
        var permRaw = await (await scopedAdmin.GetAsync("/api/v1/auth/permissions")).Content.ReadAsStringAsync();
        using var permDoc = JsonDocument.Parse(permRaw);
        var permData = permDoc.RootElement.GetProperty("data");
        Assert.True(permData.GetProperty("isPlatformUser").GetBoolean());
        var perms = permData.GetProperty("permissions").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains(Permissions.IntegrationsRead, perms);
        Assert.Contains(Permissions.OrganizationsRead, perms);

        // 4. Tenant-scoped endpoints succeed with HTTP 200 OK
        Assert.Equal(HttpStatusCode.OK, (await scopedAdmin.GetAsync("/api/v1/platform/integrations/connections")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scopedAdmin.GetAsync("/api/v1/platform/integrations/ai/policies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await scopedAdmin.GetAsync("/api/v1/platform/organizations/legal-entities")).StatusCode);

        // 5. Switch back to Platform (Global)
        var backGlobal = await ReadDataAsync<AuthTokenResponse>(await scopedAdmin.PostAsJsonAsync("/api/v1/auth/switch-tenant",
            new SwitchTenantRequest(null)));
        Assert.Null(backGlobal.TenantId);
        Assert.Equal("platform", backGlobal.Scope);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {(int)response.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("data").Deserialize<T>(JsonOpts)!;
    }
}
