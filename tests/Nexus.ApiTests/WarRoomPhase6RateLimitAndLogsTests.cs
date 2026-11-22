using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Nexus.BuildingBlocks.Messaging;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.Configuration.Application;
using Xunit;

namespace Nexus.ApiTests;

/// <summary>
/// War-Room Phase 6 Distributed Rate Limiting & Operational API Request Log Tests:
/// - Policy CRUD & live cache invalidation
/// - Real HTTP 429 enforcement with Retry-After + X-RateLimit-* headers
/// - Health check exemptions
/// - Bounded operational API request logging with zero secret/query leakage
/// </summary>
public sealed class WarRoomPhase6RateLimitAndLogsTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task DistributedRateLimitPolicy_Enforces429WithHeaders_AndApiRequestLogsRecordSafely()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        // 1. Default seeded policies exist
        var listRes = await superAdmin.GetAsync("/api/v1/platform/rate-limits");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var initialPolicies = await listRes.Content.ReadFromJsonAsync<List<RateLimitPolicyDto>>(JsonOpts);
        Assert.NotNull(initialPolicies);
        Assert.True(initialPolicies.Count >= 5);

        // 2. Create a tight policy (PermitLimit = 2, Priority = 1) targeting /api/v1/platform/email/status
        var policyName = $"tight-email-status-{Guid.NewGuid():N}"[..32];
        var createRes = await superAdmin.PostAsJsonAsync("/api/v1/platform/rate-limits", new CreateRateLimitPolicyCommand(
            policyName,
            "/api/v1/platform/email/status",
            "HumanSession",
            PermitLimit: 2,
            WindowSeconds: 60,
            BurstLimit: 0,
            IsEnabled: true,
            Priority: 1,
            Description: "Test tight rate limit"));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var created = await ReadDataAsync<RateLimitPolicyDto>(createRes);

        try
        {
            var corr1 = $"corr-rl-{Guid.NewGuid():N}";
            using var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/email/status?secretToken=TOP_SECRET_VALUE_999");
            req1.Headers.Add("X-Correlation-Id", corr1);
            var r1 = await superAdmin.SendAsync(req1);
            Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
            Assert.Equal("2", r1.Headers.GetValues("X-RateLimit-Limit").First());
            Assert.Equal("1", r1.Headers.GetValues("X-RateLimit-Remaining").First());
            Assert.Equal(policyName, r1.Headers.GetValues("X-RateLimit-Policy").First());

            var r2 = await superAdmin.GetAsync("/api/v1/platform/email/status");
            Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
            Assert.Equal("0", r2.Headers.GetValues("X-RateLimit-Remaining").First());

            // 3rd call in the same window MUST return 429 Too Many Requests + Retry-After header
            var corr3 = $"corr-rl-429-{Guid.NewGuid():N}";
            using var req3 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/platform/email/status");
            req3.Headers.Add("X-Correlation-Id", corr3);
            var r3 = await superAdmin.SendAsync(req3);
            Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
            Assert.True(r3.Headers.Contains("Retry-After"));
            var body429 = await r3.Content.ReadAsStringAsync();
            Assert.Contains("RATE_LIMIT_EXCEEDED", body429);

            // Health checks remain exempt even when throttled
            var healthRes = await superAdmin.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);

            // 3. Query API request logs for the throttled correlation ID & secret redaction check
            var logsRes = await superAdmin.GetAsync($"/api/v1/platform/api-logs?correlationId={corr3}");
            Assert.Equal(HttpStatusCode.OK, logsRes.StatusCode);
            var throttledPage = await logsRes.Content.ReadFromJsonAsync<ApiRequestLogPage>(JsonOpts);
            Assert.NotNull(throttledPage);
            var throttledLog = Assert.Single(throttledPage.Items);
            Assert.Equal(429, throttledLog.StatusCode);
            Assert.Equal("Throttled", throttledLog.RateLimitDecision);
            Assert.Equal(policyName, throttledLog.RateLimitPolicy);
            Assert.Equal("RATE_LIMIT_EXCEEDED", throttledLog.ErrorCode);

            var secretCheckRes = await superAdmin.GetAsync($"/api/v1/platform/api-logs?correlationId={corr1}");
            var secretCheckJson = await secretCheckRes.Content.ReadAsStringAsync();
            Assert.DoesNotContain("TOP_SECRET_VALUE_999", secretCheckJson);
            Assert.DoesNotContain(superAuth.AccessToken, secretCheckJson);
        }
        finally
        {
            // Clean up tight policy so subsequent tests aren't affected
            var delRes = await superAdmin.DeleteAsync($"/api/v1/platform/rate-limits/{created.Id}");
            Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);
        }
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
