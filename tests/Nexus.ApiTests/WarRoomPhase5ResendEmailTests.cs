using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.IntegrationTests.Infrastructure;
using Nexus.Modules.Notification.Application;
using Xunit;

namespace Nexus.ApiTests;

/// <summary>
/// War-Room Phase 5 Shared Resend Email Infrastructure Tests:
/// - Transactional email delivery + idempotency deduplication
/// - Resend Svix webhook signature validation, replay rejection, delivery status update & bounce suppression
/// - Suppressed recipients blocked on subsequent sends
/// - ResendEmailSender transient 503 retry + 400 permanent error classification (mocked HTTP handler)
/// </summary>
public sealed class WarRoomPhase5ResendEmailTests(NexusPostgresFixture fixture) : IClassFixture<NexusPostgresFixture>
{
    private static readonly string TestResendWebhookSecret = string.Concat("whsec", "_", Convert.ToBase64String(Encoding.UTF8.GetBytes("TestResendWebhookSecretKey32Byte")));
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public async Task SharedEmailService_EnforcesIdempotency_ResendSvixWebhook_AndBounceSuppression()
    {
        var superAuth = await fixture.LoginAsync(fixture.SuperAdminEmail, fixture.SuperAdminPassword);
        using var superAdmin = fixture.CreateAuthenticatedClient(superAuth.AccessToken);

        // 1. Send transactional email with idempotency key
        var idemKey = $"email-idem-{Guid.NewGuid():N}";
        var sendRes1 = await superAdmin.PostAsJsonAsync("/api/v1/platform/email/send",
            new SendTransactionalEmailRequest("alice@example.test", "Welcome to NEXUS", "Hello Alice!", false, idemKey, "WELCOME", 1));
        Assert.Equal(HttpStatusCode.Created, sendRes1.StatusCode);
        var delivery1 = await ReadDataAsync<EmailDeliveryDto>(sendRes1);
        Assert.Equal("Sent", delivery1.Status);
        Assert.Equal("a***@example.test", delivery1.RecipientMasked);
        Assert.False(string.IsNullOrWhiteSpace(delivery1.ProviderMessageId));

        // 2. Duplicate idempotency key returns identical delivery record without double sending!
        var sendRes2 = await superAdmin.PostAsJsonAsync("/api/v1/platform/email/send",
            new SendTransactionalEmailRequest("alice@example.test", "Welcome to NEXUS", "Hello Alice!", false, idemKey, "WELCOME", 1));
        var delivery2 = await ReadDataAsync<EmailDeliveryDto>(sendRes2);
        Assert.Equal(delivery1.Id, delivery2.Id);

        // 3. Resend Svix webhook: invalid signature rejected (401)
        using var anon = fixture.CreateClient();
        var now = DateTimeOffset.UtcNow;
        var ts = now.ToUnixTimeSeconds();
        var bounceBody = JsonSerializer.Serialize(new
        {
            type = "email.bounced",
            data = new { email_id = delivery1.ProviderMessageId, to = new[] { "alice@example.test" } }
        });

        using (var badReq = BuildSvixReq(bounceBody, "resend_bad", ts, "v1,aW52YWxpZA=="))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(badReq)).StatusCode);
        }

        // 4. Valid Resend Svix bounce webhook marks delivery Bounced and adds recipient to EmailSuppression!
        var bounceSvixId = $"resend_bounce_{Guid.NewGuid():N}";
        var bounceSig = SvixSignatureVerifier.ComputeSignature(bounceSvixId, ts, bounceBody, TestResendWebhookSecret);
        using (var okReq = BuildSvixReq(bounceBody, bounceSvixId, ts, bounceSig))
        {
            var res = await anon.SendAsync(okReq);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var whRes = await ReadDataAsync<ResendWebhookResultDto>(res);
            Assert.False(whRes.Deduplicated);
        }

        // Duplicate webhook replay -> Deduplicated = true
        using (var dupReq = BuildSvixReq(bounceBody, bounceSvixId, ts, bounceSig))
        {
            var res = await anon.SendAsync(dupReq);
            var whRes = await ReadDataAsync<ResendWebhookResultDto>(res);
            Assert.True(whRes.Deduplicated);
        }

        // 5. Subsequent email to bounced recipient is automatically Suppressed!
        var suppressedRes = await superAdmin.PostAsJsonAsync("/api/v1/platform/email/send",
            new SendTransactionalEmailRequest("alice@example.test", "Second Email", "Should be suppressed"));
        var suppressedDto = await ReadDataAsync<EmailDeliveryDto>(suppressedRes);
        Assert.Equal("Suppressed", suppressedDto.Status);

        // 6. Status & Suppressions APIs report accurate counts without exposing API key
        var status = await ReadDataAsync<EmailProviderStatusDto>(await superAdmin.GetAsync("/api/v1/platform/email/status"));
        Assert.True(status.SuppressedCount >= 1);
        var suppressions = await ReadDataAsync<List<EmailSuppressionDto>>(await superAdmin.GetAsync("/api/v1/platform/email/suppressions"));
        Assert.Contains(suppressions, s => s.RecipientMasked == "a***@example.test" && s.Reason == "Bounce");
    }

    [Fact]
    public async Task ResendEmailSender_RetriesTransient503_AndDoesNotRetryPermanent400()
    {
        var transientHandler = new SequenceHttpHandler(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"message\":\"temporary outage\"}") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"id\":\"re_msg_999\"}") });

        var opts = Options.Create(new EmailOptions
        {
            Provider = "Resend",
            DefaultFromAddress = "no-reply@duli.studio",
            Resend = new ResendEmailOptions { ApiKey = "re_unit_test_key", MaxRetries = 3, TimeoutSeconds = 5 }
        });

        var sender = new ResendEmailSender(new SingleClientFactory(transientHandler), opts, NullLogger<ResendEmailSender>.Instance);
        var okRes = await sender.SendWithResultAsync(new EmailMessage("bob@example.test", "Test", "Body", false, "idem-1"));
        Assert.True(okRes.Succeeded);
        Assert.Equal("re_msg_999", okRes.ProviderMessageId);
        Assert.Equal(2, transientHandler.CallCount);

        // Permanent 400 fails immediately on first attempt without retrying
        var permHandler = new SequenceHttpHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"message\":\"invalid domain\"}") });
        var permSender = new ResendEmailSender(new SingleClientFactory(permHandler), opts, NullLogger<ResendEmailSender>.Instance);
        var failRes = await permSender.SendWithResultAsync(new EmailMessage("bob@example.test", "Test", "Body"));
        Assert.False(failRes.Succeeded);
        Assert.False(failRes.IsTransientFailure);
        Assert.Equal(1, permHandler.CallCount);
    }

    private static HttpRequestMessage BuildSvixReq(string rawBody, string svixId, long ts, string sig)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/resend")
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

    private sealed class SequenceHttpHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _index;
        public int CallCount => _index;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var idx = Math.Min(_index++, responses.Length - 1);
            return Task.FromResult(responses[idx]);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
