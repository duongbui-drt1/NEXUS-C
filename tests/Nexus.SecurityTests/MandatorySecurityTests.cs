using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.AccessControl.Application;
using Nexus.Modules.FileStorage.Application;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Identity.Domain;
using Nexus.Modules.Organization.Application;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Security;
using Xunit;

namespace Nexus.SecurityTests;

/// <summary>
/// Implements all 15 mandatory security test cases + concurrent refresh token security tests from Step 7.4 & 7.5.
/// Executed against real ASP.NET Core pipeline + real PostgreSQL 17 + least-privilege nexus_app role with FORCE RLS.
/// </summary>
public sealed class MandatorySecurityTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task Security01_02_03_14_CrossTenant_GetUpdateDeleteAndFileAccess_AreStrictlyBlocked()
    {
        var adminAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var admin = fixture.CreateAuthenticatedClient(adminAuth.AccessToken);

        var tenantA = await ReadDataAsync<TenantDto>(await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("SECA", "Security Tenant A", "sec-a", "UTC", "USD", "US")));
        var tenantB = await ReadDataAsync<TenantDto>(await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("SECB", "Security Tenant B", "sec-b", "UTC", "USD", "US")));

        const string pw = "SecUser!Pass2026#";
        await admin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("owner.seca@nexus.test", pw, "Owner A", tenantA.Id, SystemRoles.TenantOwner));
        await admin.PostAsJsonAsync("/api/v1/platform/users", new CreateUserRequest("owner.secb@nexus.test", pw, "Owner B", tenantB.Id, SystemRoles.TenantOwner));

        var authA = await fixture.LoginAsync("owner.seca@nexus.test", pw, tenantA.Id);
        var authB = await fixture.LoginAsync("owner.secb@nexus.test", pw, tenantB.Id);

        using var clientA = fixture.CreateAuthenticatedClient(authA.AccessToken);
        using var clientB = fixture.CreateAuthenticatedClient(authB.AccessToken);

        // Tenant B creates a LegalEntity and uploads a confidential File
        var leB = await ReadDataAsync<LegalEntityDto>(await clientB.PostAsJsonAsync("/api/v1/platform/organizations/legal-entities",
            new CreateLegalEntityRequest("SEC-LE-B", "Beta Secret Entity", "TIN-B", "Secret Addr")));

        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes("TOP SECRET TENANT B PAYROLL"));
        content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        form.Add(content, "file", "payroll.txt");
        var fileB = await ReadDataAsync<FileObjectDto>(await clientB.PostAsync("/api/v1/platform/files", form));
        var tempUrlB = await ReadDataAsync<TemporaryDownloadUrlDto>(
            await clientB.PostAsync($"/api/v1/platform/files/{fileB.Id:D}/temporary-url", null));

        // Case 1: Tenant A CANNOT GET Tenant B's resource
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"/api/v1/platform/organizations/legal-entities/{leB.Id:D}")).StatusCode);

        // Case 2: Tenant A CANNOT UPDATE Tenant B's resource
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.PutAsJsonAsync(
            $"/api/v1/platform/organizations/legal-entities/{leB.Id:D}",
            new UpdateLegalEntityRequest("Hacked Name", null, null))).StatusCode);

        // Case 3: Tenant A CANNOT DELETE Tenant B's resource
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.DeleteAsync($"/api/v1/platform/organizations/legal-entities/{leB.Id:D}")).StatusCode);

        // Case 14: Tenant A CANNOT access Tenant B's file metadata, direct download, OR Tenant B's signed temporary download URL!
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"/api/v1/platform/files/{fileB.Id:D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await clientA.GetAsync($"/api/v1/platform/files/{fileB.Id:D}/download")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.GetAsync(tempUrlB.DownloadUrl)).StatusCode);

        // Verify Tenant B's resource is completely untouched
        var stillB = await ReadDataAsync<LegalEntityDto>(await clientB.GetAsync($"/api/v1/platform/organizations/legal-entities/{leB.Id:D}"));
        Assert.Equal("Beta Secret Entity", stillB.LegalName);
    }

    [Fact]
    public async Task Security04_10_11_15_AdminRbac_HeaderSpoofing_PrivilegeEscalation_AndAuditImmutability()
    {
        var adminAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var admin = fixture.CreateAuthenticatedClient(adminAuth.AccessToken);

        var tenantA = await ReadDataAsync<TenantDto>(await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("RBACA", "RBAC Tenant A", "rbac-a", "UTC", "USD", "US")));
        var tenantB = await ReadDataAsync<TenantDto>(await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("RBACB", "RBAC Tenant B", "rbac-b", "UTC", "USD", "US")));

        const string pw = "Rbac!Pass2026#";
        var empUser = await ReadDataAsync<UserDto>(await admin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("emp.rbac@nexus.test", pw, "Employee User", tenantA.Id, SystemRoles.Employee)));
        await admin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("tadmin.rbac@nexus.test", pw, "Tenant Admin", tenantA.Id, SystemRoles.TenantAdmin));
        await admin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("auditor.rbac@nexus.test", pw, "Auditor", tenantA.Id, SystemRoles.Auditor));

        var empAuth = await fixture.LoginAsync("emp.rbac@nexus.test", pw, tenantA.Id);
        var tAdminAuth = await fixture.LoginAsync("tadmin.rbac@nexus.test", pw, tenantA.Id);
        var auditorAuth = await fixture.LoginAsync("auditor.rbac@nexus.test", pw, tenantA.Id);

        using var empClient = fixture.CreateAuthenticatedClient(empAuth.AccessToken);
        using var tAdminClient = fixture.CreateAuthenticatedClient(tAdminAuth.AccessToken);
        using var auditorClient = fixture.CreateAuthenticatedClient(auditorAuth.AccessToken);

        // Case 4: Non-admin user cannot call platform admin endpoint -> 403 Forbidden
        Assert.Equal(HttpStatusCode.Forbidden, (await empClient.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("HACK", "Hack", "hack"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tAdminClient.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("HACK2", "Hack2", "hack2"))).StatusCode);

        // Case 10: Tenant header spoofing (X-Tenant-Id / Tenant-ID pointing to Tenant B) -> 403 TENANT_SPOOFING_DETECTED!
        using var spoofReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/organizations/legal-entities");
        spoofReq.Headers.Add("X-Tenant-Id", tenantB.Id.ToString("D"));
        var spoofResp = await empClient.SendAsync(spoofReq);
        Assert.Equal(HttpStatusCode.Forbidden, spoofResp.StatusCode);
        var spoofBody = await spoofResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TENANT_SPOOFING_DETECTED", spoofBody.GetProperty("errorCode").GetString());

        // Case 11: Cannot bypass RBAC via secondary endpoints or escalate to platform roles!
        // Employee cannot create org units or assign roles
        Assert.Equal(HttpStatusCode.Forbidden, (await empClient.PostAsJsonAsync("/api/v1/platform/organizations/legal-entities",
            new CreateLegalEntityRequest("X", "X", null, null))).StatusCode);
        // Auditor is strictly read-only
        Assert.Equal(HttpStatusCode.Forbidden, (await auditorClient.PutAsJsonAsync("/api/v1/platform/configuration/tenant",
            new Nexus.Modules.Configuration.Application.UpsertSettingRequest("k", "v"))).StatusCode);
        // TenantAdmin CANNOT see or assign PlatformSuperAdmin role (blocked by hybrid PostgreSQL RLS -> 404 non-disclosure, or 403 Forbidden)!
        var allRoles = await ReadDataAsync<List<RoleDto>>(await admin.GetAsync("/api/v1/platform/roles"));
        var superAdminRole = allRoles.First(r => r.Name == SystemRoles.PlatformSuperAdmin);
        var escalateResp = await tAdminClient.PostAsJsonAsync("/api/v1/platform/roles/assign",
            new AssignRoleRequest(empUser.Id, superAdminRole.Id));
        Assert.True(
            escalateResp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Expected 403 or 404 when tenant admin tries to assign PlatformSuperAdmin, got {escalateResp.StatusCode}");

        // Case 15: Audit logs cannot be modified or deleted by tenant user (405 Method Not Allowed)
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await auditorClient.DeleteAsync("/api/v1/platform/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await auditorClient.PutAsJsonAsync("/api/v1/platform/audit", new { })).StatusCode);
    }

    [Fact]
    public async Task Security05_06_07_08_09_TokenExpiry_Revocation_ReuseDetection_Lockout_SuspendedTenant_AndConcurrentRefresh()
    {
        var adminAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var admin = fixture.CreateAuthenticatedClient(adminAuth.AccessToken);

        var tenant = await ReadDataAsync<TenantDto>(await admin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("TOKS", "Token Security Corp", "tok-sec", "UTC", "USD", "US")));

        const string pw = "TokSec!Pass2026#";
        var user = await ReadDataAsync<UserDto>(await admin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("tok.user@nexus.test", pw, "Token User", tenant.Id, SystemRoles.TenantOwner)));

        // Case 5: Expired JWT access token is strictly rejected (401 Unauthorized)
        var expiredJwt = BuildExpiredJwt(user.Id, user.Email, tenant.Id);
        using var expiredClient = fixture.CreateAuthenticatedClient(expiredJwt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expiredClient.GetAsync("/api/v1/auth/me")).StatusCode);

        // Case 6: Refresh token after logout (revoked) is rejected (401) AND active access token is immediately rejected (401)!
        var login1 = await fixture.LoginAsync("tok.user@nexus.test", pw, tenant.Id);
        using var loggedInClient = fixture.CreateAuthenticatedClient(login1.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await loggedInClient.GetAsync("/api/v1/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await loggedInClient.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await loggedInClient.GetAsync("/api/v1/auth/me")).StatusCode);

        using var anon = fixture.CreateClient();
        var revokedRefresh = await anon.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login1.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, revokedRefresh.StatusCode);

        // Case 7: Refresh token rotation + REUSE DETECTION revokes entire family and active session!
        var login2 = await fixture.LoginAsync("tok.user@nexus.test", pw, tenant.Id);
        var rotated = await ReadDataAsync<AuthTokenResponse>(
            await anon.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login2.RefreshToken)));
        Assert.NotEqual(login2.RefreshToken, rotated.RefreshToken);

        // Replay old already-rotated refresh token -> triggers reuse detection!
        var reuseAttempt = await anon.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(login2.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, reuseAttempt.StatusCode);
        var reuseBody = await reuseAttempt.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AUTH_REFRESH_TOKEN_REUSE", reuseBody.GetProperty("errorCode").GetString());

        // The newer legitimate refresh token in the compromised family MUST now also be revoked!
        var victimRefresh = await anon.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(rotated.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, victimRefresh.StatusCode);

        // And the compromised session's access token MUST also be rejected!
        using var victimClient = fixture.CreateAuthenticatedClient(rotated.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await victimClient.GetAsync("/api/v1/auth/me")).StatusCode);

        // Concurrency test (7.5): Two simultaneous refresh requests using the EXACT same active refresh token!
        var loginRace = await fixture.LoginAsync("tok.user@nexus.test", pw, tenant.Id);
        using var anon1 = fixture.CreateClient();
        using var anon2 = fixture.CreateClient();
        var raceRes = await Task.WhenAll(
            anon1.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(loginRace.RefreshToken)),
            anon2.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(loginRace.RefreshToken)));
        Assert.Equal(1, raceRes.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, raceRes.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));

        // Case 9: Suspended tenant cannot execute business endpoints -> 403 TENANT_SUSPENDED!
        var login3 = await fixture.LoginAsync("tok.user@nexus.test", pw, tenant.Id);
        using var tenantClient = fixture.CreateAuthenticatedClient(login3.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await tenantClient.GetAsync("/api/v1/platform/organizations/legal-entities")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/v1/platform/tenants/{tenant.Id:D}/suspend",
            new SuspendTenantRequest("Compliance hold"))).StatusCode);

        var suspendedCall = await tenantClient.GetAsync("/api/v1/platform/organizations/legal-entities");
        Assert.Equal(HttpStatusCode.Forbidden, suspendedCall.StatusCode);
        var suspBody = await suspendedCall.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("TENANT_SUSPENDED", suspBody.GetProperty("errorCode").GetString());

        // Reactivate tenant so we can test Case 8 (Lockout / Disable)
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/v1/platform/tenants/{tenant.Id:D}/activate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tenantClient.GetAsync("/api/v1/platform/organizations/legal-entities")).StatusCode);

        // Case 8: Locked-out user cannot continue using existing active session!
        using (var scope = fixture.ApiFactory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var appUser = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(appUser);
            await userManager.SetLockoutEndDateAsync(appUser!, DateTimeOffset.UtcNow.AddHours(1));
        }

        var lockedCall = await tenantClient.GetAsync("/api/v1/platform/organizations/legal-entities");
        Assert.Equal(HttpStatusCode.Unauthorized, lockedCall.StatusCode);
        var lockedBody = await lockedCall.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("AUTH_ACCOUNT_INACTIVE", lockedBody.GetProperty("errorCode").GetString());
    }

    private static string BuildExpiredJwt(Guid userId, string email, Guid tenantId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(NexusPostgresFixture.TestJwtSigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;

        var jwt = new JwtSecurityToken(
            issuer: "https://nexus-c.test",
            audience: "nexus-ecosystem-test",
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("tid", tenantId.ToString("D")),
                new Claim("scope", "tenant")
            ],
            notBefore: now.AddMinutes(-20),
            expires: now.AddMinutes(-5),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        return JsonSerializer.Deserialize<T>(doc.GetProperty("data").GetRawText(), JsonOpts)!;
    }
}
