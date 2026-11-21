using Microsoft.Extensions.Configuration;
using Nexus.Bootstrap;
using Nexus.Modules.Integration.Domain;
using Xunit;

namespace Nexus.UnitTests;

public sealed class HardeningRegressionUnitTests
{
    [Theory]
    [InlineData("https://localhost/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://10.0.0.5/internal")]
    [InlineData("https://172.16.0.10/internal")]
    [InlineData("https://192.168.1.50/internal")]
    [InlineData("https://[::1]/hook")]
    [InlineData("https://[fe80::1]/hook")]
    [InlineData("https://[fc00::1]/hook")]
    [InlineData("https://metadata.google.internal/computeMetadata")]
    [InlineData("https://api.service.local/webhook")]
    public void WebhookRegistration_RejectsUnsafeSsrfTargets(string unsafeUrl)
    {
        var result = WebhookRegistration.Create(Guid.NewGuid(), "platform.tenant.created.v1", unsafeUrl, "vault://nexus/wh");
        Assert.True(result.IsFailure);
        Assert.Equal("WEBHOOK_UNSAFE_TARGET", result.Error.Code);
    }

    [Fact]
    public void WebhookRegistration_AllowsPublicHttpsPartnerEndpoint()
    {
        var result = WebhookRegistration.Create(
            Guid.NewGuid(),
            "platform.tenant.created.v1",
            "https://hooks.partner-erp.example.com/nexus/events",
            "vault://nexus/wh");

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ProductionStartupValidation_RejectsWeakOrPlaceholderSecretsAndWildcardCors()
    {
        var weakJwtConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=pg;Database=nexus;Username=nexus_app;Password=StrongProdPassword99!",
                ["Jwt:SigningKey"] = "CHANGE_ME_PLACEHOLDER_KEY_12345678901234567890",
                ["ObjectStorage:SigningSecret"] = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            NexusBootstrapExtensions.ValidateRuntimeSecurityConfiguration(weakJwtConfig, "Production"));

        var superuserConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=pg;Database=nexus;Username=postgres;Password=StrongProdPassword99!",
                ["Jwt:SigningKey"] = "a7f3e2c9b4d18065e8a2f4c7d9b1e3f5a7c9e1b3d5f7a9c1e3b5d7f9a1c3e5b7",
                ["ObjectStorage:SigningSecret"] = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            NexusBootstrapExtensions.ValidateRuntimeSecurityConfiguration(superuserConfig, "Production"));

        var wildcardCorsConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=pg;Database=nexus;Username=nexus_app;Password=StrongProdPassword99!",
                ["Jwt:SigningKey"] = "a7f3e2c9b4d18065e8a2f4c7d9b1e3f5a7c9e1b3d5f7a9c1e3b5d7f9a1c3e5b7",
                ["ObjectStorage:SigningSecret"] = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                ["Cors:AllowedOrigins:0"] = "*"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            NexusBootstrapExtensions.ValidateRuntimeSecurityConfiguration(wildcardCorsConfig, "Production"));

        var validProdConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=pg;Database=nexus;Username=nexus_app;Password=StrongProdPassword99!",
                ["Jwt:SigningKey"] = "a7f3e2c9b4d18065e8a2f4c7d9b1e3f5a7c9e1b3d5f7a9c1e3b5d7f9a1c3e5b7",
                ["ObjectStorage:SigningSecret"] = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                ["Cors:AllowedOrigins:0"] = "https://console.nexus.company"
            })
            .Build();

        // Must NOT throw for valid hardened production config
        NexusBootstrapExtensions.ValidateRuntimeSecurityConfiguration(validProdConfig, "Production");
    }
}
