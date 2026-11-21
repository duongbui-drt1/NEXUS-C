using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.AccessControl.Application;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Security;
using Xunit;

namespace Nexus.SecurityTests;

/// <summary>
/// Phase 02 P0 Security Regression Suite:
/// Verifies cross-tenant membership privilege escalation fix, tenant-scoped user management isolation,
/// role assignment membership checks, and GET /api/v1/auth/permissions effective scope isolation.
/// </summary>
public sealed class Phase02AuthorizationRegressionTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task MembershipAndUserManagement_CrossTenantEscalation_IsStrictlyBlocked()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        // Provision Tenant A & Tenant B
        var tenantA = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("P0TENA", "Phase2 Tenant A", "p0-ten-a", "UTC", "USD", "US")));
        var tenantB = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("P0TENB", "Phase2 Tenant B", "p0-ten-b", "UTC", "USD", "US")));

        const string pw = "Phase2!Sec2026#";

        var adminAUser = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("admin.p0a@nexus.test", pw, "Admin A", tenantA.Id, SystemRoles.TenantAdmin)));
        var memberAUser = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("member.p0a@nexus.test", pw, "Member A", tenantA.Id, SystemRoles.Employee)));
        var memberBUser = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("member.p0b@nexus.test", pw, "Member B", tenantB.Id, SystemRoles.Employee)));
        var standaloneUser = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("standalone.p0@nexus.test", pw, "Standalone User", null, null, false)));
        var supportUser = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("support.p0@nexus.test", pw, "Platform Support", null, SystemRoles.PlatformSupport, true)));
        _ = supportUser;

        var adminAAuth = await fixture.LoginAsync("admin.p0a@nexus.test", pw, tenantA.Id);
        var supportAuth = await fixture.LoginAsync("support.p0@nexus.test", pw);

        using var adminAClient = fixture.CreateAuthenticatedClient(adminAAuth.AccessToken);
        using var supportClient = fixture.CreateAuthenticatedClient(supportAuth.AccessToken);

        // 1. TenantAdmin A CAN grant & revoke membership in Tenant A (own tenant)
        var grantOwn = await adminAClient.PostAsync($"/api/v1/platform/tenants/{tenantA.Id:D}/memberships/{standaloneUser.Id:D}", null);
        Assert.Equal(HttpStatusCode.Created, grantOwn.StatusCode);

        var revokeOwn = await adminAClient.DeleteAsync($"/api/v1/platform/tenants/{tenantA.Id:D}/memberships/{standaloneUser.Id:D}");
        Assert.Equal(HttpStatusCode.NoContent, revokeOwn.StatusCode);

        // 2. CRITICAL P0: TenantAdmin A CANNOT grant membership in Tenant B -> HTTP 403!
        var grantCross = await adminAClient.PostAsync($"/api/v1/platform/tenants/{tenantB.Id:D}/memberships/{adminAUser.Id:D}", null);
        Assert.Equal(HttpStatusCode.Forbidden, grantCross.StatusCode);
        var grantCrossErr = await grantCross.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TENANT_MEMBERSHIP_SCOPE_VIOLATION", grantCrossErr.GetProperty("errorCode").GetString());

        // 3. CRITICAL P0: TenantAdmin A CANNOT revoke membership in Tenant B -> HTTP 403!
        var revokeCross = await adminAClient.DeleteAsync($"/api/v1/platform/tenants/{tenantB.Id:D}/memberships/{memberBUser.Id:D}");
        Assert.Equal(HttpStatusCode.Forbidden, revokeCross.StatusCode);
        var revokeCrossErr = await revokeCross.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TENANT_MEMBERSHIP_SCOPE_VIOLATION", revokeCrossErr.GetProperty("errorCode").GetString());

        // 4. PlatformSupport CANNOT manage cross-tenant memberships -> HTTP 403!
        var supportGrant = await supportClient.PostAsync($"/api/v1/platform/tenants/{tenantB.Id:D}/memberships/{standaloneUser.Id:D}", null);
        Assert.Equal(HttpStatusCode.Forbidden, supportGrant.StatusCode);

        // 5. PlatformSuperAdmin (holding platform.memberships.manage) CAN grant & revoke in Tenant B
        var superGrant = await superAdmin.PostAsync($"/api/v1/platform/tenants/{tenantB.Id:D}/memberships/{standaloneUser.Id:D}", null);
        Assert.Equal(HttpStatusCode.Created, superGrant.StatusCode);
        var superRevoke = await superAdmin.DeleteAsync($"/api/v1/platform/tenants/{tenantB.Id:D}/memberships/{standaloneUser.Id:D}");
        Assert.Equal(HttpStatusCode.NoContent, superRevoke.StatusCode);

        // 6. Invalid Tenant ID -> 404; Nonexistent User ID -> 404
        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.PostAsync($"/api/v1/platform/tenants/{Guid.NewGuid():D}/memberships/{standaloneUser.Id:D}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await superAdmin.PostAsync($"/api/v1/platform/tenants/{tenantB.Id:D}/memberships/{Guid.NewGuid():D}", null)).StatusCode);

        // 7. TenantAdmin A CANNOT create a Platform user or a user in Tenant B -> HTTP 403
        Assert.Equal(HttpStatusCode.Forbidden, (await adminAClient.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("evil.plat@nexus.test", pw, "Evil", null, SystemRoles.PlatformSuperAdmin, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminAClient.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("evil.b@nexus.test", pw, "Evil B", tenantB.Id, SystemRoles.Employee, false))).StatusCode);

        // 8. TenantAdmin A CANNOT disable Tenant B's user or PlatformSuperAdmin -> HTTP 403
        Assert.Equal(HttpStatusCode.Forbidden, (await adminAClient.PostAsJsonAsync(
            $"/api/v1/platform/users/{memberBUser.Id:D}/disable", new { Reason = "Attack" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminAClient.PostAsJsonAsync(
            $"/api/v1/platform/users/{superAuth.UserId:D}/disable", new { Reason = "Attack" })).StatusCode);

        // 9. TenantAdmin A user list sees ONLY Tenant A members (isolated from Tenant B & Platform users)
        var listResp = await adminAClient.GetAsync("/api/v1/platform/users?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listedJson = await listResp.Content.ReadAsStringAsync();
        Assert.Contains("member.p0a@nexus.test", listedJson);
        Assert.DoesNotContain("member.p0b@nexus.test", listedJson);
        Assert.DoesNotContain(fixture.SuperAdminEmail, listedJson);

        // 10. TenantAdmin A CANNOT assign a Tenant A role to Tenant B's user (not a member of Tenant A!) -> HTTP 403
        var rolesA = await ReadDataAsync<List<RoleDto>>(await adminAClient.GetAsync("/api/v1/platform/roles"));
        var empRole = rolesA.First(r => r.Name == SystemRoles.Employee);
        var badAssign = await adminAClient.PostAsJsonAsync("/api/v1/platform/roles/assign",
            new AssignRoleRequest(memberBUser.Id, empRole.Id));
        Assert.Equal(HttpStatusCode.Forbidden, badAssign.StatusCode);
        var badAssignErr = await badAssign.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("USER_NOT_TENANT_MEMBER", badAssignErr.GetProperty("errorCode").GetString());

        // 11. TenantAdmin A querying role assignments for Tenant B's user returns empty (isolated!)
        var crossRoles = await ReadDataAsync<List<UserRoleDto>>(await adminAClient.GetAsync($"/api/v1/platform/roles/users/{memberBUser.Id:D}"));
        Assert.Empty(crossRoles);

        // 12. GET /api/v1/auth/permissions returns strictly scoped effective permissions
        var permDocA = await (await adminAClient.GetAsync("/api/v1/auth/permissions")).Content.ReadFromJsonAsync<JsonElement>();
        var permsA = permDocA.GetProperty("data").GetProperty("permissions").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains(Permissions.UsersManage, permsA);
        Assert.Contains(Permissions.ApiRead, permsA);
        Assert.DoesNotContain(Permissions.MembershipsManageCrossTenant, permsA);
        Assert.DoesNotContain(Permissions.TenantsCreate, permsA);
        Assert.DoesNotContain(Permissions.OperationsRead, permsA);

        var permDocSuper = await (await superAdmin.GetAsync("/api/v1/auth/permissions")).Content.ReadFromJsonAsync<JsonElement>();
        var permsSuper = permDocSuper.GetProperty("data").GetProperty("permissions").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Contains(Permissions.MembershipsManageCrossTenant, permsSuper);
        Assert.Contains(Permissions.OperationsRead, permsSuper);

        // 13. Service-layer check: even bypassing the controller, TenantService rejects unauthenticated/wrong-tenant call
        using var scope = fixture.ApiFactory.Services.CreateScope();
        var tenantCtx = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var tenantSvc = scope.ServiceProvider.GetRequiredService<ITenantService>();
        using (tenantCtx.BeginScope(tenantA.Id))
        {
            var unauthRes = await tenantSvc.AddMembershipAsync(tenantB.Id, memberAUser.Id);
            Assert.True(unauthRes.IsFailure);
            Assert.Equal("AUTH_REQUIRED", unauthRes.Error.Code);
        }
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return JsonSerializer.Deserialize<T>(doc.GetProperty("data").GetRawText(), JsonOpts)!;
    }
}
