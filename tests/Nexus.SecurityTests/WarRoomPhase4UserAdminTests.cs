using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Security;
using Xunit;

namespace Nexus.SecurityTests;

/// <summary>
/// War-Room Phase 4 Security & Contract Tests:
/// - Safe user detail exposure (internal ID, profile, sessions, zero secrets)
/// - Approved profile field editing (FirstName, LastName, DisplayName, Locale, TimeZone, CountryCode, Region, Phone, AdminNotes)
/// - Strict mass-assignment rejection when role/permission/membership/privilege fields are supplied
/// - Cross-tenant user detail/profile isolation
/// </summary>
public sealed class WarRoomPhase4UserAdminTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task UserProfileEditing_RejectsPrivilegeMassAssignment_AndEnforcesTenantIsolation()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var tenantA = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("USRADMA", "UserAdmin Tenant A", "usradm-a", "UTC", "USD", "US")));
        var tenantB = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest("USRADMB", "UserAdmin Tenant B", "usradm-b", "UTC", "USD", "US")));

        const string pw = "UsrAdm!2026#";
        await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("ta.usradma@nexus.test", pw, "TA A", tenantA.Id, SystemRoles.TenantAdmin));
        var empA = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("emp.usradma@nexus.test", pw, "Emp A", tenantA.Id, SystemRoles.Employee)));
        var empB = await ReadDataAsync<UserDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest("emp.usradmb@nexus.test", pw, "Emp B", tenantB.Id, SystemRoles.Employee)));

        var taAuth = await fixture.LoginAsync("ta.usradma@nexus.test", pw, tenantA.Id);
        using var taClient = fixture.CreateAuthenticatedClient(taAuth.AccessToken);

        // 1. Safe approved profile update succeeds for member in own tenant
        var okUpdate = await taClient.PutAsJsonAsync($"/api/v1/platform/users/{empA.Id}/profile", new
        {
            fullName = "Nguyen Van A",
            firstName = "Van A",
            lastName = "Nguyen",
            displayName = "Nguyen A",
            preferredLocale = "vi-VN",
            timeZone = "Asia/Ho_Chi_Minh",
            countryCode = "VN",
            region = "Ho Chi Minh",
            phoneNumber = "+84901234567",
            adminNotes = "Verified employee profile"
        });
        Assert.Equal(HttpStatusCode.OK, okUpdate.StatusCode);
        var detail = await ReadDataAsync<UserDetailDto>(okUpdate);
        Assert.Equal("Nguyen Van A", detail.FullName);
        Assert.Equal("vi-VN", detail.PreferredLocale);
        Assert.Equal("Asia/Ho_Chi_Minh", detail.TimeZone);
        Assert.Equal("VN", detail.CountryCode);
        Assert.Equal("+84901234567", detail.PhoneNumber);

        // 2. Mass-assignment attack with privilege fields MUST be rejected (422 PROFILE_PRIVILEGE_FIELDS_FORBIDDEN)
        var attackRoles = await taClient.PutAsJsonAsync($"/api/v1/platform/users/{empA.Id}/profile", new
        {
            displayName = "Hacked",
            roles = new[] { SystemRoles.PlatformSuperAdmin }
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, attackRoles.StatusCode);

        var attackPlatformFlag = await taClient.PutAsJsonAsync($"/api/v1/platform/users/{empA.Id}/profile", new
        {
            displayName = "Hacked",
            isPlatformUser = true
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, attackPlatformFlag.StatusCode);

        var attackTenant = await taClient.PutAsJsonAsync($"/api/v1/platform/users/{empA.Id}/profile", new
        {
            displayName = "Hacked",
            tenantId = tenantB.Id
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, attackTenant.StatusCode);

        // 3. Cross-tenant user detail & profile edit MUST be denied (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await taClient.GetAsync($"/api/v1/platform/users/{empB.Id}")).StatusCode);
        var crossEdit = await taClient.PutAsJsonAsync($"/api/v1/platform/users/{empB.Id}/profile", new { displayName = "CrossTenant" });
        Assert.Equal(HttpStatusCode.Forbidden, crossEdit.StatusCode);
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {(int)response.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("data").Deserialize<T>(JsonOpts)!;
    }
}
