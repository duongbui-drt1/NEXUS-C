using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Nexus.Modules.Identity.Application;
using Nexus.Modules.Identity.Domain;
using Nexus.Modules.Identity.Infrastructure;
using Nexus.SharedKernel.Contracts;

namespace Nexus.Modules.Identity;

public static class IdentityModuleExtensions
{
    public const string DualAuthScheme = "NexusDualAuth";
    public const string ClerkJwtScheme = "ClerkJwt";

    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<ClerkOptions>(configuration.GetSection(ClerkOptions.SectionName));
        services.AddHttpClient("ClerkBackendApi", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddDbContext<IdentityModuleDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__ef_migrations_history_identity", IdentityModuleDbContext.SchemaName);
            });
        });

        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 10;
            options.Password.RequireDigit = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireNonAlphanumeric = true;
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        })
        .AddEntityFrameworkStores<IdentityModuleDbContext>()
        .AddDefaultTokenProviders();

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var clerk = configuration.GetSection(ClerkOptions.SectionName).Get<ClerkOptions>() ?? new ClerkOptions();
        var signingKey = string.IsNullOrWhiteSpace(jwt.SigningKey)
            ? "NEXUS_BOOTSTRAP_PLACEHOLDER_KEY_MUST_BE_OVERRIDDEN_32CHARS"
            : jwt.SigningKey;

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = DualAuthScheme;
            options.DefaultChallengeScheme = DualAuthScheme;
        })
        .AddPolicyScheme(DualAuthScheme, "NEXUS Local JWT or Clerk JWKS JWT", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                if (clerk.Enabled && !string.IsNullOrWhiteSpace(clerk.Issuer))
                {
                    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(authHeader) &&
                        authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        var tokenStr = authHeader["Bearer ".Length..].Trim();
                        var handler = new JwtSecurityTokenHandler();
                        if (handler.CanReadToken(tokenStr))
                        {
                            var unverified = handler.ReadJwtToken(tokenStr);
                            if (string.Equals(unverified.Issuer?.TrimEnd('/'), clerk.Issuer.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                            {
                                return ClerkJwtScheme;
                            }
                        }
                    }
                }
                return JwtBearerDefaults.AuthenticationScheme;
            };
        })
        .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "sub"
            };
        });

        authBuilder.AddJwtBearer(ClerkJwtScheme, options =>
        {
            options.MapInboundClaims = false;
            if (!string.IsNullOrWhiteSpace(clerk.Issuer))
            {
                options.Authority = clerk.Issuer.TrimEnd('/');
                if (!string.IsNullOrWhiteSpace(clerk.JwksUri))
                {
                    options.MetadataAddress = clerk.JwksUri;
                }
            }
            options.RequireHttpsMetadata = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = clerk.Issuer.TrimEnd('/'),
                ValidateAudience = false, // Clerk session JWTs use azp (authorized party) rather than aud
                ValidateIssuerSigningKey = true,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "sub"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = ctx =>
                {
                    var sub = ctx.Principal?.FindFirst("sub")?.Value;
                    if (string.IsNullOrWhiteSpace(sub))
                    {
                        ctx.Fail("Clerk token missing required subject claim.");
                        return Task.CompletedTask;
                    }

                    if (clerk.AuthorizedParties is { Length: > 0 })
                    {
                        var azp = ctx.Principal?.FindFirst("azp")?.Value;
                        if (string.IsNullOrWhiteSpace(azp) || !clerk.AuthorizedParties.Contains(azp, StringComparer.OrdinalIgnoreCase))
                        {
                            ctx.Fail("Clerk token authorized party (azp) is not allowlisted.");
                            return Task.CompletedTask;
                        }
                    }

                    if (ctx.Principal?.Identity is ClaimsIdentity identity)
                    {
                        identity.AddClaim(new Claim("auth_provider", ClerkSyncService.ProviderName));
                    }

                    return Task.CompletedTask;
                }
            };
        });

        services.AddScoped<ITokenGenerator, JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IClerkSyncService, ClerkSyncService>();
        services.AddScoped<ServiceClientService>();
        services.AddScoped<IServiceClientService>(sp => sp.GetRequiredService<ServiceClientService>());
        services.AddScoped<IServicePrincipalValidator>(sp => sp.GetRequiredService<ServiceClientService>());
        services.AddScoped<IUserDirectoryReader, UserDirectoryReader>();

        return services;
    }
}
