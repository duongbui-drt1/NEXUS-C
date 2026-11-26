using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.IntegrationTests.Infrastructure;
using Xunit;

namespace Nexus.IntegrationTests;

public sealed class ApplicationPermissionSyncTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task SyncApplicationPermissions_AllowsNexusMPermissions_RejectsPlatformPrefix_AndAuthorizesEndToEnd()
    {
        var client = fixture.ApiFactory.CreateClient();
        var adminToken = await LoginAsync(client, fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // 1. Reject tampering with reserved platform.* prefix
        var forbiddenResp = await client.PostAsJsonAsync("/api/v1/platform/applications/NEXUS_M/permissions/sync", new
        {
            permissions = new[]
            {
                new { code = "platform.tenants.read", name = "Hijack", category = "Management", scope = "Tenant" }
            }
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);

        // 2. Sync valid NEXUS_M permissions (idempotent upsert)
        var syncPayload = new
        {
            permissions = new[]
            {
                new { code = "m.workspace.read", name = "Read Management Workspace", category = "M00_Foundation", scope = "Tenant" },
                new { code = "m.dashboard.read", name = "Read Executive Dashboard", category = "M01_Executive", scope = "Tenant" },
                new { code = "m.report.export", name = "Export Management Reports", category = "M02_BI", scope = "Tenant" }
            }
        };

        var sync1 = await client.PostAsJsonAsync("/api/v1/platform/applications/NEXUS_M/permissions/sync", syncPayload);
        Assert.Equal(HttpStatusCode.OK, sync1.StatusCode);
        var sync1Json = await sync1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("NEXUS_M", sync1Json.GetProperty("data").GetProperty("applicationCode").GetString());
        Assert.Equal(3, sync1Json.GetProperty("data").GetProperty("addedCount").GetInt32());

        // Re-syncing same payload is idempotent (0 added, 0 updated)
        var sync2 = await client.PostAsJsonAsync("/api/v1/platform/applications/NEXUS_M/permissions/sync", syncPayload);
        Assert.Equal(HttpStatusCode.OK, sync2.StatusCode);
        var sync2Json = await sync2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, sync2Json.GetProperty("data").GetProperty("addedCount").GetInt32());
        Assert.Equal(0, sync2Json.GetProperty("data").GetProperty("updatedCount").GetInt32());

        // 3. Verify m.* permissions appear in /api/v1/platform/roles/permissions
        var permsResp = await client.GetAsync("/api/v1/platform/roles/permissions");
        Assert.Equal(HttpStatusCode.OK, permsResp.StatusCode);
        var permsJson = await permsResp.Content.ReadAsStringAsync();
        Assert.Contains("m.workspace.read", permsJson);
        Assert.Contains("m.dashboard.read", permsJson);
        Assert.Contains("m.report.export", permsJson);
    }
}
