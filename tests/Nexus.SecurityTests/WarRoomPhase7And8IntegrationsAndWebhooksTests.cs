using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Integration.Application;
using Nexus.Modules.Tenancy.Application;
using Nexus.SharedKernel.Security;
using Xunit;

namespace Nexus.SecurityTests;

/// <summary>
/// War-Room Phase 7 & 8 Provider Catalog, Secure Tenant Connections & Outbound Webhook Security Tests:
/// - Canonical providers pre-seeded
/// - Connection secret-reference enforcement, non-secret config secret rejection, test, disable, reconnect, rotate-ref, delete
/// - SafeWebhookHttpDispatcher SSRF / metadata / private-IP / redirect blocking & signed delivery tracking
/// </summary>
public sealed class WarRoomPhase7And8IntegrationsAndWebhooksTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task CanonicalProviders_AndTenantConnectionLifecycle_EnforceSecretAndHostGuards()
    {
        using var client = await CreateTenantOwnerClientAsync("WR7", "WarRoom Phase 7 Corp", "admin.wr7@demo.test");

        // 1. Canonical providers seeded
        var provRes = await client.GetAsync("/api/v1/platform/integrations/providers");
        Assert.Equal(HttpStatusCode.OK, provRes.StatusCode);
        var providers = await ReadDataAsync<List<ProviderDto>>(provRes);
        Assert.Contains(providers, p => p.Code == "RESEND" && p.AllowedHosts == "api.resend.com");
        Assert.Contains(providers, p => p.Code == "CLERK");
        Assert.Contains(providers, p => p.Code == "FX_FRANKFURTER");

        var resendProvider = providers.First(p => p.Code == "RESEND");

        // 2. Reject secret-like key inside NonSecretConfig
        var badSecretCfg = await client.PostAsJsonAsync("/api/v1/platform/integrations/connections",
            new CreateConnectionRequest(
                resendProvider.Id,
                "Bad Secret Connection",
                "vault://nexus/wr7/resend",
                NonSecretConfig: new Dictionary<string, object> { ["apiKey"] = "raw_plaintext_api_key_forbidden" }));
        Assert.Equal(HttpStatusCode.BadRequest, badSecretCfg.StatusCode);
        Assert.Contains("CONNECTION_SECRET_IN_CONFIG_FORBIDDEN", await badSecretCfg.Content.ReadAsStringAsync());

        // 3. Create valid connection with allowed endpointUrl
        var createRes = await client.PostAsJsonAsync("/api/v1/platform/integrations/connections",
            new CreateConnectionRequest(
                resendProvider.Id,
                "WR7 Resend Production",
                "vault://nexus/wr7/resend-v1",
                ApplicationCode: "NEXUS_F",
                NonSecretConfig: new Dictionary<string, object> { ["endpointUrl"] = "https://api.resend.com/emails" }));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var conn = await ReadDataAsync<ConnectionDto>(createRes);
        Assert.Equal("NEXUS_F", conn.ApplicationCode);

        // 4. Test connection -> Healthy
        var testRes = await client.PostAsync($"/api/v1/platform/integrations/connections/{conn.Id}/test", null);
        Assert.Equal(HttpStatusCode.OK, testRes.StatusCode);
        var testOutcome = await ReadDataAsync<ConnectionTestResultDto>(testRes);
        Assert.True(testOutcome.Passed);
        Assert.Equal("Healthy", testOutcome.HealthStatus);

        // 5. Disable -> Test fails -> Reconnect -> Rotate ref -> Delete
        var disRes = await client.PostAsync($"/api/v1/platform/integrations/connections/{conn.Id}/disable", null);
        Assert.Equal(HttpStatusCode.OK, disRes.StatusCode);
        var disConn = await ReadDataAsync<ConnectionDto>(disRes);
        Assert.Equal("Disconnected", disConn.Status);

        var testDisabledRes = await client.PostAsync($"/api/v1/platform/integrations/connections/{conn.Id}/test", null);
        var testDisabled = await ReadDataAsync<ConnectionTestResultDto>(testDisabledRes);
        Assert.False(testDisabled.Passed);
        Assert.Equal("Disabled", testDisabled.HealthStatus);

        var reconnRes = await client.PostAsync($"/api/v1/platform/integrations/connections/{conn.Id}/reconnect", null);
        Assert.Equal(HttpStatusCode.OK, reconnRes.StatusCode);

        var rotRes = await client.PostAsJsonAsync($"/api/v1/platform/integrations/connections/{conn.Id}/rotate-credential-ref",
            new RotateConnectionCredentialRefRequest("kms://nexus/wr7/resend-v2"));
        Assert.Equal(HttpStatusCode.OK, rotRes.StatusCode);
        var rotated = await ReadDataAsync<ConnectionDto>(rotRes);
        Assert.Equal("kms://nexus/wr7/resend-v2", rotated.CredentialReference);

        var delRes = await client.DeleteAsync($"/api/v1/platform/integrations/connections/{conn.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);
    }

    [Fact]
    public async Task OutboundWebhookDispatcher_BlocksSsrfMetadataPrivateIpsAndRedirects()
    {
        var handler = new StubHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("http://169.254.169.254/latest/meta-data/") }
        });
        var factory = new SingleClientFactory(new HttpClient(handler));
        var dispatcher = new SafeWebhookHttpDispatcher(factory, NullLogger<SafeWebhookHttpDispatcher>.Instance);

        // 1. Metadata IP blocked before any HTTP call
        var metaOutcome = await dispatcher.DispatchAsync(new WebhookDispatchRequest(
            Guid.NewGuid(), "invoice.paid", "https://169.254.169.254/hook", "{\"id\":1}", "secret-key", 1700000000));
        Assert.False(metaOutcome.Succeeded);
        Assert.True(metaOutcome.BlockedBySsrf);
        Assert.Contains("WEBHOOK_SSRF_BLOCKED", metaOutcome.FailureReason);
        Assert.Equal(0, handler.CallCount);

        // 2. Private RFC1918 IP blocked before any HTTP call
        var privateOutcome = await dispatcher.DispatchAsync(new WebhookDispatchRequest(
            Guid.NewGuid(), "invoice.paid", "https://10.0.0.42/hook", "{\"id\":1}", "secret-key", 1700000000));
        Assert.False(privateOutcome.Succeeded);
        Assert.True(privateOutcome.BlockedBySsrf);
        Assert.Equal(0, handler.CallCount);

        // 3. Plain HTTP blocked
        var httpOutcome = await dispatcher.DispatchAsync(new WebhookDispatchRequest(
            Guid.NewGuid(), "invoice.paid", "http://93.184.216.34/hook", "{\"id\":1}", "secret-key", 1700000000));
        Assert.False(httpOutcome.Succeeded);
        Assert.True(httpOutcome.BlockedBySsrf);
        Assert.Equal(0, handler.CallCount);

        // 4. Public IP returning 302 redirect is rejected with WEBHOOK_REDIRECT_FORBIDDEN (no redirect followed!)
        var redirectOutcome = await dispatcher.DispatchAsync(new WebhookDispatchRequest(
            Guid.NewGuid(), "invoice.paid", "https://93.184.216.34/hook", "{\"id\":1}", "secret-key", 1700000000));
        Assert.False(redirectOutcome.Succeeded);
        Assert.True(redirectOutcome.BlockedBySsrf);
        Assert.Contains("WEBHOOK_REDIRECT_FORBIDDEN", redirectOutcome.FailureReason);
        Assert.Equal(1, handler.CallCount);
        Assert.True(handler.LastRequest!.Headers.Contains("X-Nexus-Signature"));
        Assert.True(handler.LastRequest!.Headers.Contains("X-Nexus-Timestamp"));
        Assert.True(handler.LastRequest!.Headers.Contains("X-Nexus-Event-Id"));

        // 5. End-to-end API dispatch endpoint records signed delivery attempt cleanly
        using var client = await CreateTenantOwnerClientAsync("WR8", "WarRoom Phase 8 Corp", "admin.wr8@demo.test");

        var whRes = await client.PostAsJsonAsync("/api/v1/platform/integrations/webhooks",
            new CreateWebhookRequest("order.dispatched", "https://nonexistent.invalid/nexus-hook", "vault://nexus/wr8/hook-secret", "WR8 hook", "NEXUS_E"));
        Assert.Equal(HttpStatusCode.Created, whRes.StatusCode);
        var wh = await ReadDataAsync<WebhookDto>(whRes);
        Assert.Equal("NEXUS_E", wh.ApplicationCode);

        var dispatchRes = await client.PostAsJsonAsync($"/api/v1/platform/integrations/webhooks/{wh.Id}/dispatch",
            new DispatchWebhookRequest(Guid.NewGuid(), "{\"orderId\":\"ORD-1\"}", MaxAttempts: 1));
        Assert.Equal(HttpStatusCode.Created, dispatchRes.StatusCode);
        var delivery = await ReadDataAsync<WebhookDeliveryDto>(dispatchRes);
        Assert.Equal("Failed", delivery.Status); // .invalid domain fails DNS resolution cleanly without leaking internal data
        Assert.Contains("WEBHOOK_SSRF_BLOCKED", delivery.FailureReason);
        Assert.False(string.IsNullOrWhiteSpace(delivery.SignatureDigestPrefix));
    }

    private sealed class StubHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private async Task<HttpClient> CreateTenantOwnerClientAsync(string code, string name, string email)
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        var tenant = await ReadDataAsync<TenantDto>(await superAdmin.PostAsJsonAsync("/api/v1/platform/tenants",
            new CreateTenantRequest(code, name, code.ToLowerInvariant(), "UTC", "USD", "US")));

        const string pw = "StrongPass!2026#";
        await superAdmin.PostAsJsonAsync("/api/v1/platform/users",
            new CreateUserRequest(email, pw, $"{code} Admin", tenant.Id, SystemRoles.TenantOwner));

        var auth = await fixture.LoginAsync(email, pw, tenant.Id);
        return fixture.CreateAuthenticatedClient(auth.AccessToken);
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
