using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.Identity.Application;
using Xunit;

namespace Nexus.SecurityTests;

/// <summary>
/// War-Room Phase 3 Security & Sync Tests (Clerk Identity Provider):
/// - Svix HMAC-SHA256 signature verification on raw body
/// - Invalid signature & timestamp replay (> 300s) rejection (401)
/// - Idempotent duplicate webhook handling (same svix-id)
/// - Out-of-order stale webhook non-overwrite protection
/// - Soft deactivation on user.deleted preserving internal user ID & audit
/// - Zero roles/memberships/platform privileges derived from Clerk events
/// - Forged Clerk JWT rejection (401)
/// </summary>
public sealed class WarRoomPhase3ClerkSecurityTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly string TestWebhookSecret = string.Concat("whsec", "_", Convert.ToBase64String(Encoding.UTF8.GetBytes("TestClerkWebhookSecretKey32Bytes")));
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task ClerkWebhook_EnforcesSvixSignature_Idempotency_OutOfOrderProtection_AndSoftDeactivation()
    {
        using var anon = fixture.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var clerkSub = $"user_clerk_{Guid.NewGuid():N}";

        var createdPayload = JsonSerializer.Serialize(new
        {
            type = "user.created",
            timestamp = now.ToUnixTimeMilliseconds(),
            data = new
            {
                id = clerkSub,
                first_name = "Alice",
                last_name = "Initial",
                primary_email_address_id = "idn_1",
                email_addresses = new[]
                {
                    new { id = "idn_1", email_address = $"{clerkSub}@example.test", verification = new { status = "verified" } }
                },
                banned = false
            }
        });

        // 1. Invalid signature -> 401
        using (var badSigReq = BuildSvixRequest(createdPayload, "msg_bad_sig", ts, "v1,aW52YWxpZHNpZw=="))
        {
            var badRes = await anon.SendAsync(badSigReq);
            Assert.Equal(HttpStatusCode.Unauthorized, badRes.StatusCode);
        }

        // 2. Expired timestamp (> 300s replay) -> 401
        var oldTs = now.AddMinutes(-10).ToUnixTimeSeconds();
        var oldSig = SvixSignatureVerifier.ComputeSignature("msg_replay", oldTs, createdPayload, TestWebhookSecret);
        using (var replayReq = BuildSvixRequest(createdPayload, "msg_replay", oldTs, oldSig))
        {
            var replayRes = await anon.SendAsync(replayReq);
            Assert.Equal(HttpStatusCode.Unauthorized, replayRes.StatusCode);
        }

        // 3. Valid user.created webhook -> 200 Created (local user provisioned with ZERO platform privileges/roles)
        var createSvixId = $"msg_create_{Guid.NewGuid():N}";
        var createSig = SvixSignatureVerifier.ComputeSignature(createSvixId, ts, createdPayload, TestWebhookSecret);
        ClerkWebhookResultDto createdResult;
        using (var okReq = BuildSvixRequest(createdPayload, createSvixId, ts, createSig))
        {
            var okRes = await anon.SendAsync(okReq);
            Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
            createdResult = await ReadDataAsync<ClerkWebhookResultDto>(okRes);
            Assert.Equal("Created", createdResult.Status);
            Assert.False(createdResult.Deduplicated);
            Assert.NotNull(createdResult.InternalUserId);
        }

        // 4. Duplicate delivery of same svix-id -> 200 Deduplicated = true
        using (var dupReq = BuildSvixRequest(createdPayload, createSvixId, ts, createSig))
        {
            var dupRes = await anon.SendAsync(dupReq);
            Assert.Equal(HttpStatusCode.OK, dupRes.StatusCode);
            var dupResult = await ReadDataAsync<ClerkWebhookResultDto>(dupRes);
            Assert.True(dupResult.Deduplicated);
        }

        // 5. Newer user.updated (t + 10s) succeeds, then STALE out-of-order user.updated (t + 5s) MUST NOT overwrite!
        var newerTime = now.AddSeconds(10);
        var newerPayload = JsonSerializer.Serialize(new
        {
            type = "user.updated",
            timestamp = newerTime.ToUnixTimeMilliseconds(),
            data = new
            {
                id = clerkSub,
                first_name = "Alice",
                last_name = "Newest",
                primary_email_address_id = "idn_1",
                email_addresses = new[]
                {
                    new { id = "idn_1", email_address = $"{clerkSub}@example.test", verification = new { status = "verified" } }
                },
                banned = false
            }
        });
        var newerId = $"msg_upd_new_{Guid.NewGuid():N}";
        using (var req = BuildSvixRequest(newerPayload, newerId, ts, SvixSignatureVerifier.ComputeSignature(newerId, ts, newerPayload, TestWebhookSecret)))
        {
            var res = await anon.SendAsync(req);
            var dto = await ReadDataAsync<ClerkWebhookResultDto>(res);
            Assert.Equal("Updated", dto.Status);
        }

        var staleTime = now.AddSeconds(5);
        var stalePayload = JsonSerializer.Serialize(new
        {
            type = "user.updated",
            timestamp = staleTime.ToUnixTimeMilliseconds(),
            data = new
            {
                id = clerkSub,
                first_name = "Alice",
                last_name = "StaleOverwrite",
                primary_email_address_id = "idn_1",
                email_addresses = new[]
                {
                    new { id = "idn_1", email_address = $"{clerkSub}@example.test", verification = new { status = "verified" } }
                },
                banned = false
            }
        });
        var staleId = $"msg_upd_stale_{Guid.NewGuid():N}";
        using (var req = BuildSvixRequest(stalePayload, staleId, ts, SvixSignatureVerifier.ComputeSignature(staleId, ts, stalePayload, TestWebhookSecret)))
        {
            var res = await anon.SendAsync(req);
            var dto = await ReadDataAsync<ClerkWebhookResultDto>(res);
            Assert.Equal("IgnoredStale", dto.Status);
        }

        // Verify via PlatformSuperAdmin UserDetail that profile kept "Alice Newest" and has ZERO platform flag or tenant memberships!
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var userDetail = await ReadDataAsync<UserDetailDto>(await superAdmin.GetAsync($"/api/v1/platform/users/{createdResult.InternalUserId}"));
        Assert.Equal("Alice Newest", userDetail.FullName);
        Assert.False(userDetail.IsPlatformUser);
        Assert.Empty(userDetail.TenantIds);
        Assert.Equal(clerkSub, userDetail.ClerkSubjectId);

        // 6. user.deleted soft-deactivates user while preserving record & audit trail
        var delTime = now.AddSeconds(20);
        var delPayload = JsonSerializer.Serialize(new
        {
            type = "user.deleted",
            timestamp = delTime.ToUnixTimeMilliseconds(),
            data = new { id = clerkSub }
        });
        var delId = $"msg_del_{Guid.NewGuid():N}";
        using (var req = BuildSvixRequest(delPayload, delId, ts, SvixSignatureVerifier.ComputeSignature(delId, ts, delPayload, TestWebhookSecret)))
        {
            var res = await anon.SendAsync(req);
            var dto = await ReadDataAsync<ClerkWebhookResultDto>(res);
            Assert.Equal("Deactivated", dto.Status);
        }

        var afterDel = await ReadDataAsync<UserDetailDto>(await superAdmin.GetAsync($"/api/v1/platform/users/{createdResult.InternalUserId}"));
        Assert.False(afterDel.IsActive);
        Assert.Equal("Deactivated", afterDel.ExternalSyncStatus);

        // 7. Forged Clerk JWT (invalid signature) -> 401 Unauthorized
        var forgedHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}")).TrimEnd('=');
        var forgedBody = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{{\"iss\":\"https://clerk.nexus.test\",\"sub\":\"{clerkSub}\",\"exp\":{now.AddHours(1).ToUnixTimeSeconds()}}}")).TrimEnd('=');
        using var forgedReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        forgedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", $"{forgedHeader}.{forgedBody}.forgedsignature");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(forgedReq)).StatusCode);

        // 8. Sync status endpoint returns live telemetry; reconcile without SecretKey fails honestly (422)
        var syncStatus = await ReadDataAsync<ClerkSyncStatusDto>(await superAdmin.GetAsync("/api/v1/platform/users/clerk/sync-status"));
        Assert.True(syncStatus.WebhookSecretConfigured);
        Assert.True(syncStatus.LinkedUserCount >= 1);

        var recRes = await superAdmin.PostAsync("/api/v1/platform/users/clerk/reconcile", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, recRes.StatusCode);
    }

    private static HttpRequestMessage BuildSvixRequest(string rawBody, string svixId, long ts, string sig)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/clerk")
        {
            Content = new StringContent(rawBody, Encoding.UTF8, "application/json")
        };
        req.Headers.Add(SvixSignatureVerifier.IdHeader, svixId);
        req.Headers.Add(SvixSignatureVerifier.TimestampHeader, ts.ToString());
        req.Headers.Add(SvixSignatureVerifier.SignatureHeader, sig);
        return req;
    }

    private static async Task<T> ReadDataAsync<T>(HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Expected success but got {(int)response.StatusCode}: {raw}");
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.GetProperty("data").Deserialize<T>(JsonOpts)!;
    }
}
