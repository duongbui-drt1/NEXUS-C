using Nexus.BuildingBlocks.Infrastructure;
using Nexus.BuildingBlocks.Web;
using Nexus.Modules.Configuration.Domain;
using Nexus.Modules.FileStorage.Domain;
using Nexus.Modules.Integration.Domain;
using Nexus.Modules.Notification.Domain;
using Nexus.Modules.Organization.Domain;
using Nexus.Modules.Subscription.Domain;
using Nexus.Modules.Tenancy.Domain;
using Nexus.Modules.Workflow.Domain;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Pagination;
using Xunit;

namespace Nexus.UnitTests;

public sealed class DomainAndLogicUnitTests
{
    [Fact]
    public void Tenant_Create_ValidatesCodeCurrencyCountryAndNormalizes()
    {
        var invalidCurrency = Tenant.Create("ACME", "Acme Corp", "acme", "UTC", "US", "US");
        Assert.True(invalidCurrency.IsFailure);
        Assert.Equal("TENANT_INVALID_CURRENCY", invalidCurrency.Error.Code);

        var invalidCountry = Tenant.Create("ACME", "Acme Corp", "acme", "UTC", "USD", "USA");
        Assert.True(invalidCountry.IsFailure);
        Assert.Equal("TENANT_INVALID_COUNTRY", invalidCountry.Error.Code);

        var valid = Tenant.Create(" acme ", "Acme Corp", "ACME-Slug", "Asia/Ho_Chi_Minh", "vnd", "vn");
        Assert.True(valid.IsSuccess);
        Assert.Equal("ACME", valid.Value.Code);
        Assert.Equal("acme-slug", valid.Value.Slug);
        Assert.Equal("VND", valid.Value.DefaultCurrency);
        Assert.Equal("VN", valid.Value.CountryCode);
        Assert.Equal(TenantLifecycleStatus.Active, valid.Value.Status);
    }

    [Fact]
    public void Tenant_SuspendAndActivate_EnforcesLifecycleRules()
    {
        var tenant = Tenant.Create("NX01", "Nexus One", "nx01", "UTC", "USD", "US").Value;
        var now = DateTimeOffset.UtcNow;

        Assert.True(tenant.Suspend("", now).IsFailure);
        Assert.True(tenant.Suspend("Non-payment", now).IsSuccess);
        Assert.Equal(TenantLifecycleStatus.Suspended, tenant.Status);
        Assert.True(tenant.Suspend("Again", now).IsFailure);

        Assert.True(tenant.Activate().IsSuccess);
        Assert.Equal(TenantLifecycleStatus.Active, tenant.Status);
        Assert.True(tenant.Activate().IsFailure);
    }

    [Fact]
    public void OrganizationUnit_CannotSetSelfAsParent()
    {
        var unit = OrganizationUnit.Create(Guid.NewGuid(), null, "HQ", "Headquarters", OrganizationUnitType.Division).Value;
        var res = unit.Update(unit.Id, "Headquarters", OrganizationUnitType.Division);
        Assert.True(res.IsFailure);
        Assert.Equal("ORG_UNIT_SELF_PARENT", res.Error.Code);
    }

    [Fact]
    public void WorkflowInstance_EnforcesValidStateMachineTransitions()
    {
        var tid = Guid.NewGuid();
        var inst = WorkflowInstance.Create(tid, Guid.NewGuid(), Guid.NewGuid(), "Invoice", "INV-1", Guid.NewGuid(), null);
        var now = DateTimeOffset.UtcNow;

        // Cannot approve while still Draft
        Assert.True(inst.MarkApproved(now).IsFailure);

        // Submit -> Pending -> Approved
        Assert.True(inst.Submit(now, 24).IsSuccess);
        Assert.Equal(WorkflowStatus.Pending, inst.Status);
        Assert.True(inst.Submit(now, 24).IsFailure);

        Assert.True(inst.MarkApproved(now).IsSuccess);
        Assert.Equal(WorkflowStatus.Approved, inst.Status);

        // Cannot reject or cancel after Approved
        Assert.True(inst.MarkRejected(now).IsFailure);
        Assert.True(inst.Cancel(now).IsFailure);
    }

    [Fact]
    public void ApprovalTask_CannotBeDecidedTwice()
    {
        var task = ApprovalTask.Create(Guid.NewGuid(), Guid.NewGuid(), 1, "CFO");
        var now = DateTimeOffset.UtcNow;

        Assert.True(task.Decide(true, Guid.NewGuid(), now).IsSuccess);
        var second = task.Decide(false, Guid.NewGuid(), now);
        Assert.True(second.IsFailure);
        Assert.Equal("WORKFLOW_TASK_ALREADY_DECIDED", second.Error.Code);
    }

    [Fact]
    public void Subscription_PlanUsesDecimalPrecisionAndValidatesPeriod()
    {
        var badPrice = Plan.Create("ENT", "Enterprise", "Desc", -10m, "USD");
        Assert.True(badPrice.IsFailure);

        var plan = Plan.Create("ENT", "Enterprise", "Full Suite", 199.995m, "usd").Value;
        Assert.Equal(200.00m, plan.MonthlyPrice);
        Assert.Equal("USD", plan.Currency);

        var now = DateTimeOffset.UtcNow;
        Assert.True(TenantSubscription.Create(Guid.NewGuid(), plan.Id, now, now.AddMinutes(-1)).IsFailure);

        var sub = TenantSubscription.Create(Guid.NewGuid(), plan.Id, now.AddDays(-1), now.AddDays(30)).Value;
        Assert.True(sub.IsCurrentlyValid(now));
        sub.Expire();
        Assert.False(sub.IsCurrentlyValid(now));
    }

    [Theory]
    [InlineData("db_password")]
    [InlineData("StripeApiKey")]
    [InlineData("jwt.signing.secret")]
    [InlineData("oauth_refresh_token")]
    [InlineData("private_key_pem")]
    public void Configuration_RejectsPlaintextSecretKeys(string secretKey)
    {
        var platformRes = PlatformSetting.Create(secretKey, "super-secret", "Security");
        Assert.True(platformRes.IsFailure);
        Assert.Equal("CONFIG_SECRET_FORBIDDEN", platformRes.Error.Code);

        var tenantRes = TenantSetting.Create(Guid.NewGuid(), secretKey, "super-secret", "Security");
        Assert.True(tenantRes.IsFailure);
        Assert.Equal("CONFIG_SECRET_FORBIDDEN", tenantRes.Error.Code);
    }

    [Fact]
    public void SensitiveDataRedactor_ScrubsNestedSecretsInAuditMetadata()
    {
        var payload = new
        {
            Email = "user@nexus.local",
            Password = "PlainTextPassword123!",
            Nested = new
            {
                AccessToken = "eyJhbGciOiJIUzI1NiJ9.secret",
                RefreshToken = "opaque_refresh_secret",
                SafeField = "visible"
            }
        };

        var sanitized = SensitiveDataRedactor.SanitizeToJson(payload)!;
        Assert.DoesNotContain("PlainTextPassword123!", sanitized);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiJ9.secret", sanitized);
        Assert.DoesNotContain("opaque_refresh_secret", sanitized);
        Assert.Contains("[REDACTED]", sanitized);
        Assert.Contains("user@nexus.local", sanitized);
        Assert.Contains("visible", sanitized);
    }

    [Fact]
    public void FileStorage_EnforcesMimeAllowlistSizeLimitAndTenantPrefix()
    {
        var tenantId = Guid.NewGuid();
        var badMime = StoredFileObject.Create(tenantId, "exploit.html", "text/html", 128, 1024, "abc", "General");
        Assert.True(badMime.IsFailure);
        Assert.Equal("FILE_UNSUPPORTED_CONTENT_TYPE", badMime.Error.Code);

        var tooBig = StoredFileObject.Create(tenantId, "big.pdf", "application/pdf", 2048, 1024, "abc", "General");
        Assert.True(tooBig.IsFailure);
        Assert.Equal("FILE_INVALID_SIZE", tooBig.Error.Code);

        var ok = StoredFileObject.Create(tenantId, "../../invoice.pdf", "application/pdf", 512, 1024, "deadbeef", "Finance");
        Assert.True(ok.IsSuccess);
        Assert.StartsWith($"tenants/{tenantId:D}/", ok.Value.ObjectKey);
        Assert.Equal("invoice.pdf", ok.Value.OriginalFileName);
    }

    [Fact]
    public void IntegrationConnection_RejectsRawCredentialsRequiresSecretRefUri()
    {
        var raw = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(), "Bank", "sk_live_raw_secret_123");
        Assert.True(raw.IsFailure);
        Assert.Equal("INTEGRATION_INVALID_CREDENTIAL_REF", raw.Error.Code);

        var ok = IntegrationConnection.Create(Guid.NewGuid(), Guid.NewGuid(), "Bank", "vault://nexus/tenant-a/bank-cred");
        Assert.True(ok.IsSuccess);
    }

    [Fact]
    public void NotificationTemplate_RendersVariablesCleanly()
    {
        var tmpl = NotificationTemplate.Create(Guid.NewGuid(), "WELCOME", NotificationChannel.Email, "Hello {{name}}", "Welcome to {{tenant}}!").Value;
        var (subject, body) = tmpl.Render(new Dictionary<string, string> { ["name"] = "Linh", ["tenant"] = "NEXUS" });
        Assert.Equal("Hello Linh", subject);
        Assert.Equal("Welcome to NEXUS!", body);
    }

    [Fact]
    public void PagedRequest_ClampsOutOfRangeValues()
    {
        var req = new PagedRequest { Page = -5, PageSize = 500 };
        Assert.Equal(1, req.Page);
        Assert.Equal(100, req.PageSize);
        Assert.Equal(0, req.Skip);
    }

    [Fact]
    public void CryptoHelper_ProducesDeterministicHashesAndValidatesHmac()
    {
        var h1 = CryptoHelper.Sha256Hex("token-1");
        var h2 = CryptoHelper.Sha256Hex("token-1");
        var h3 = CryptoHelper.Sha256Hex("token-2");
        Assert.Equal(h1, h2);
        Assert.NotEqual(h1, h3);

        var sig = CryptoHelper.ComputeHmacSha256("data", "secret");
        Assert.True(CryptoHelper.FixedTimeEquals(sig, CryptoHelper.ComputeHmacSha256("data", "secret")));
        Assert.False(CryptoHelper.FixedTimeEquals(sig, CryptoHelper.ComputeHmacSha256("tampered", "secret")));
    }
}
