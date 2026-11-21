using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nexus.BuildingBlocks.Infrastructure;
using Nexus.Modules.Identity.Domain;
using Nexus.Modules.Identity.Infrastructure;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;
using Nexus.SharedKernel.Events;
using Nexus.SharedKernel.Pagination;
using Nexus.SharedKernel.Results;

namespace Nexus.Modules.Identity.Application;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "https://nexus-c.local";
    public string Audience { get; set; } = "nexus-ecosystem";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
}

public sealed record LoginRequest(string Email, string Password, Guid? TenantId = null);
public sealed record RefreshRequest(string RefreshToken);
public sealed record SwitchTenantRequest(Guid TenantId);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record ResetPasswordRequest(string Email, string ResetToken, string NewPassword);
public sealed record VerifyEmailRequest(Guid UserId, string VerificationToken);

public sealed record CreateUserRequest(
    string Email,
    string Password,
    string FullName,
    Guid? TenantId = null,
    string? RoleName = null,
    bool IsPlatformUser = false);

public sealed record InviteUserRequest(string Email, string FullName, string? RoleName = null);

public sealed record AuthTokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    Guid UserId,
    string Email,
    Guid? TenantId,
    string Scope,
    Guid SessionId);

public sealed record UserDto(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive,
    bool IsPlatformUser,
    bool EmailConfirmed,
    bool IsLockedOut,
    DateTimeOffset CreatedAt);

public sealed record SessionDto(
    Guid Id,
    Guid UserId,
    Guid? TenantId,
    string? IpAddress,
    string? UserAgent,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    bool IsRevoked);

public interface ITokenGenerator
{
    (string Token, DateTimeOffset ExpiresAt) GenerateAccessToken(
        ApplicationUser user,
        Guid sessionId,
        Guid? tenantId,
        string scope,
        TimeSpan? overrideLifetime = null);
}

public sealed class JwtTokenGenerator(IOptions<JwtOptions> options, ISystemClock clock) : ITokenGenerator
{
    public (string Token, DateTimeOffset ExpiresAt) GenerateAccessToken(
        ApplicationUser user,
        Guid sessionId,
        Guid? tenantId,
        string scope,
        TimeSpan? overrideLifetime = null)
    {
        var cfg = options.Value;
        if (string.IsNullOrWhiteSpace(cfg.SigningKey) || cfg.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be configured with at least 32 characters.");
        }

        var now = clock.UtcNow;
        var expires = now.Add(overrideLifetime ?? TimeSpan.FromMinutes(cfg.AccessTokenMinutes));

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new("sid", sessionId.ToString("D")),
            new("scope", scope)
        };

        if (tenantId.HasValue)
        {
            claims.Add(new Claim("tid", tenantId.Value.ToString("D")));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: cfg.Issuer,
            audience: cfg.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(jwt), expires);
    }
}

public interface IAuthService
{
    Task<Result<AuthTokenResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<Result<AuthTokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct = default);
    Task<Result> LogoutAsync(CancellationToken ct = default);
    Task<Result<AuthTokenResponse>> SwitchTenantAsync(SwitchTenantRequest request, CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
    Task<Result<string>> GeneratePasswordResetTokenAsync(string email, CancellationToken ct = default);
    Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default);
    Task<Result<string>> GenerateEmailVerificationTokenAsync(Guid userId, CancellationToken ct = default);
    Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<SessionDto>>> ListMySessionsAsync(CancellationToken ct = default);
    Task<Result> RevokeSessionAsync(Guid userId, Guid sessionId, string reason, CancellationToken ct = default);
    Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<Result<UserDto>> InviteUserToCurrentTenantAsync(InviteUserRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<UserDto>>> ListUsersAsync(PagedRequest paging, CancellationToken ct = default);
    Task<Result> DisableUserAsync(Guid userId, string reason, CancellationToken ct = default);
    Task<Result> EnableUserAsync(Guid userId, CancellationToken ct = default);
}

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    IdentityModuleDbContext db,
    ITokenGenerator tokenGenerator,
    ITenantAccessValidator tenantValidator,
    IPermissionEvaluator permissionEvaluator,
    ITenantContext tenantContext,
    ICurrentUser currentUser,
    ICorrelationContext correlationContext,
    ISystemClock clock,
    IAuditWriter auditWriter,
    IOptions<JwtOptions> jwtOptions,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<Result<AuthTokenResponse>> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Error.Validation("AUTH_INVALID_INPUT", "Email and password are required.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            await RecordLoginAttemptAsync(null, email, false, "UserNotFound", ct);
            return Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid email or password.");
        }

        if (!user.IsActive)
        {
            await RecordLoginAttemptAsync(user.Id, email, false, "AccountDisabled", ct);
            return Error.Unauthorized("AUTH_ACCOUNT_DISABLED", "User account has been disabled.");
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await RecordLoginAttemptAsync(user.Id, email, false, "AccountLockedOut", ct);
            return Error.Unauthorized("AUTH_ACCOUNT_LOCKED", "Account is temporarily locked due to failed login attempts.");
        }

        var passwordValid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            await userManager.AccessFailedAsync(user);
            await RecordLoginAttemptAsync(user.Id, email, false, "InvalidPassword", ct);
            return Error.Unauthorized("AUTH_INVALID_CREDENTIALS", "Invalid email or password.");
        }

        await userManager.ResetAccessFailedCountAsync(user);

        Guid? targetTenantId = request.TenantId;
        string scope;

        if (user.IsPlatformUser && !targetTenantId.HasValue)
        {
            scope = "platform";
        }
        else
        {
            if (!targetTenantId.HasValue)
            {
                var accessible = await tenantValidator.GetAccessibleTenantIdsAsync(user.Id, ct);
                if (accessible.Count == 0)
                {
                    if (user.IsPlatformUser)
                    {
                        scope = "platform";
                    }
                    else
                    {
                        await RecordLoginAttemptAsync(user.Id, email, false, "NoTenantMembership", ct);
                        return Error.Forbidden("TENANT_MEMBERSHIP_REQUIRED", "User does not belong to any active tenant.");
                    }
                }
                else
                {
                    targetTenantId = accessible[0];
                    scope = "tenant";
                }
            }
            else
            {
                var access = await tenantValidator.ValidateMembershipAsync(user.Id, targetTenantId.Value, ct);
                if (access is null || !access.IsActiveMember)
                {
                    await RecordLoginAttemptAsync(user.Id, email, false, "TenantMembershipDenied", ct);
                    return Error.Forbidden("TENANT_MEMBERSHIP_REQUIRED", "User does not have an active membership in the requested tenant.");
                }

                scope = "tenant";
            }
        }

        var now = clock.UtcNow;
        var refreshLifetime = TimeSpan.FromDays(jwtOptions.Value.RefreshTokenDays);

        var session = UserSession.Create(user.Id, targetTenantId, currentUser.IpAddress, currentUser.UserAgent, now, refreshLifetime);
        var rawRefreshToken = CryptoHelper.GenerateSecureToken(64);
        var refreshHash = CryptoHelper.Sha256Hex(rawRefreshToken);
        var familyId = Guid.CreateVersion7();

        var refreshEntity = RefreshToken.Create(user.Id, targetTenantId, session.Id, familyId, refreshHash, now, refreshLifetime);

        db.UserSessions.Add(session);
        db.RefreshTokens.Add(refreshEntity);
        db.LoginAttempts.Add(LoginAttempt.Record(user.Id, email, true, null, currentUser.IpAddress, currentUser.UserAgent, now));
        await db.SaveChangesAsync(ct);

        var (accessToken, accessExp) = tokenGenerator.GenerateAccessToken(user, session.Id, targetTenantId, scope);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.login", "UserSession", session.Id.ToString("D"), targetTenantId, "Success"), ct);

        return new AuthTokenResponse(
            accessToken, accessExp, rawRefreshToken, refreshEntity.ExpiresAt,
            user.Id, user.Email!, targetTenantId, scope, session.Id);
    }

    public async Task<Result<AuthTokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Error.Validation("AUTH_REFRESH_REQUIRED", "Refresh token is required.");
        }

        var tokenHash = CryptoHelper.Sha256Hex(request.RefreshToken.Trim());
        var now = clock.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Lock the refresh token row with FOR UPDATE to prevent concurrent double-rotation race conditions!
        var existing = (await db.RefreshTokens
            .FromSqlInterpolated($"SELECT *, xmin FROM identity.refresh_tokens WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
            .ToListAsync(ct))
            .FirstOrDefault();

        if (existing is null)
        {
            await tx.RollbackAsync(ct);
            return Error.Unauthorized("AUTH_INVALID_REFRESH_TOKEN", "Refresh token is invalid.");
        }

        // Reuse Detection: rotated or revoked token presented again -> revoke ENTIRE token family & session!
        if (existing.IsRevoked || existing.IsRotated)
        {
            var familyTokens = await db.RefreshTokens
                .Where(t => t.TokenFamilyId == existing.TokenFamilyId && t.RevokedAt == null)
                .ToListAsync(ct);

            foreach (var token in familyTokens)
            {
                token.Revoke("TokenReuseDetected", now);
            }

            var compromisedSession = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == existing.SessionId, ct);
            compromisedSession?.Revoke("TokenReuseDetected", now);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            logger.LogWarning(
                "SECURITY ALERT: Refresh token reuse detected for user {UserId}, family {FamilyId}. Revoked entire token family and session.",
                existing.UserId, existing.TokenFamilyId);

            await auditWriter.WriteAsync(new AuditEntryRequest(
                "security.refresh_token.reuse_detected",
                "RefreshTokenFamily",
                existing.TokenFamilyId.ToString("D"),
                existing.TenantId,
                "Blocked",
                Reason: "Presented already rotated or revoked refresh token; revoked entire family and session."), ct);

            return Error.Unauthorized("AUTH_REFRESH_TOKEN_REUSE", "Refresh token reuse detected. Session has been revoked.");
        }

        if (existing.ExpiresAt <= now)
        {
            existing.Revoke("Expired", now);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Error.Unauthorized("AUTH_REFRESH_TOKEN_EXPIRED", "Refresh token has expired.");
        }

        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == existing.SessionId, ct);
        if (session is null || session.IsRevoked || session.ExpiresAt <= now)
        {
            existing.Revoke("SessionRevoked", now);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Error.Unauthorized("AUTH_SESSION_REVOKED", "User session has been revoked.");
        }

        var user = await userManager.FindByIdAsync(existing.UserId.ToString("D"));
        if (user is null || !user.IsActive || await userManager.IsLockedOutAsync(user))
        {
            existing.Revoke("AccountInactive", now);
            session.Revoke("AccountInactive", now);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Error.Unauthorized("AUTH_ACCOUNT_INACTIVE", "User account is disabled or locked.");
        }

        // Re-validate tenant membership if tenant-scoped
        if (existing.TenantId.HasValue)
        {
            var access = await tenantValidator.ValidateMembershipAsync(user.Id, existing.TenantId.Value, ct);
            if (access is null || !access.IsActiveMember)
            {
                existing.Revoke("MembershipRevoked", now);
                session.Revoke("MembershipRevoked", now);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Error.Forbidden("TENANT_MEMBERSHIP_REQUIRED", "Tenant membership is no longer active.");
            }
        }

        var refreshLifetime = TimeSpan.FromDays(jwtOptions.Value.RefreshTokenDays);
        var newRawToken = CryptoHelper.GenerateSecureToken(64);
        var newHash = CryptoHelper.Sha256Hex(newRawToken);

        var replacement = RefreshToken.Create(
            user.Id,
            existing.TenantId,
            session.Id,
            existing.TokenFamilyId,
            newHash,
            now,
            refreshLifetime);

        existing.MarkRotated(replacement.Id, now);
        session.Touch(now);

        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var scope = existing.TenantId.HasValue ? "tenant" : "platform";
        var (accessToken, accessExp) = tokenGenerator.GenerateAccessToken(user, session.Id, existing.TenantId, scope);

        return new AuthTokenResponse(
            accessToken, accessExp, newRawToken, replacement.ExpiresAt,
            user.Id, user.Email!, existing.TenantId, scope, session.Id);
    }

    public async Task<Result> LogoutAsync(CancellationToken ct = default)
    {
        if (!currentUser.UserId.HasValue || !currentUser.SessionId.HasValue)
        {
            return Result.Failure(Error.Unauthorized("AUTH_REQUIRED", "Authentication required."));
        }

        return await RevokeSessionAsync(currentUser.UserId.Value, currentUser.SessionId.Value, "UserLogout", ct);
    }

    public async Task<Result<AuthTokenResponse>> SwitchTenantAsync(SwitchTenantRequest request, CancellationToken ct = default)
    {
        if (!currentUser.UserId.HasValue || !currentUser.SessionId.HasValue)
        {
            return Error.Unauthorized("AUTH_REQUIRED", "Authentication required.");
        }

        var access = await tenantValidator.ValidateMembershipAsync(currentUser.UserId.Value, request.TenantId, ct);
        if (access is null || !access.IsActiveMember)
        {
            return Error.Forbidden("TENANT_MEMBERSHIP_REQUIRED", "You do not have an active membership in the target tenant.");
        }

        var user = await userManager.FindByIdAsync(currentUser.UserId.Value.ToString("D"));
        if (user is null || !user.IsActive)
        {
            return Error.Unauthorized("AUTH_ACCOUNT_INACTIVE", "User account is inactive.");
        }

        var now = clock.UtcNow;
        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == currentUser.SessionId.Value && s.UserId == user.Id, ct);
        if (session is null || session.IsRevoked)
        {
            return Error.Unauthorized("AUTH_SESSION_REVOKED", "Session is revoked.");
        }

        session.Touch(now, request.TenantId);
        var refreshLifetime = TimeSpan.FromDays(jwtOptions.Value.RefreshTokenDays);
        var newRawToken = CryptoHelper.GenerateSecureToken(64);
        var replacement = RefreshToken.Create(
            user.Id, request.TenantId, session.Id, Guid.CreateVersion7(), CryptoHelper.Sha256Hex(newRawToken), now, refreshLifetime);

        db.RefreshTokens.Add(replacement);
        await db.SaveChangesAsync(ct);

        var (accessToken, accessExp) = tokenGenerator.GenerateAccessToken(user, session.Id, request.TenantId, "tenant");
        return new AuthTokenResponse(
            accessToken, accessExp, newRawToken, replacement.ExpiresAt,
            user.Id, user.Email!, request.TenantId, "tenant", session.Id);
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        if (!currentUser.UserId.HasValue)
        {
            return Result.Failure(Error.Unauthorized("AUTH_REQUIRED", "Authentication required."));
        }

        var user = await userManager.FindByIdAsync(currentUser.UserId.Value.ToString("D"));
        if (user is null)
        {
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));
        }

        var res = await userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!res.Succeeded)
        {
            return Result.Failure(Error.Validation(
                "PASSWORD_CHANGE_FAILED",
                string.Join("; ", res.Errors.Select(e => e.Description))));
        }

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.password.change", "User", user.Id.ToString("D"), currentUser.TenantId, "Success"), ct);

        return Result.Success();
    }

    public async Task<Result<string>> GeneratePasswordResetTokenAsync(string email, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(email.Trim().ToLowerInvariant());
        if (user is null)
        {
            return Error.NotFound("USER_NOT_FOUND", "User not found.");
        }

        return await userManager.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim().ToLowerInvariant());
        if (user is null)
        {
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));
        }

        var res = await userManager.ResetPasswordAsync(user, request.ResetToken, request.NewPassword);
        if (!res.Succeeded)
        {
            return Result.Failure(Error.Validation(
                "PASSWORD_RESET_FAILED",
                string.Join("; ", res.Errors.Select(e => e.Description))));
        }

        await RevokeAllUserSessionsInternalAsync(user.Id, "PasswordReset", ct);
        return Result.Success();
    }

    public async Task<Result<string>> GenerateEmailVerificationTokenAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null)
        {
            return Error.NotFound("USER_NOT_FOUND", "User not found.");
        }

        return await userManager.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task<Result> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken ct = default)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString("D"));
        if (user is null)
        {
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));
        }

        var res = await userManager.ConfirmEmailAsync(user, request.VerificationToken);
        if (!res.Succeeded)
        {
            return Result.Failure(Error.Validation("EMAIL_VERIFICATION_FAILED", string.Join("; ", res.Errors.Select(e => e.Description))));
        }

        return Result.Success();
    }

    public async Task<Result<IReadOnlyList<SessionDto>>> ListMySessionsAsync(CancellationToken ct = default)
    {
        if (!currentUser.UserId.HasValue)
        {
            return Error.Unauthorized("AUTH_REQUIRED", "Authentication required.");
        }

        var sessions = await db.UserSessions.AsNoTracking()
            .Where(s => s.UserId == currentUser.UserId.Value)
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new SessionDto(s.Id, s.UserId, s.TenantId, s.IpAddress, s.UserAgent, s.CreatedAt, s.LastSeenAt, s.ExpiresAt, s.RevokedAt != null))
            .ToListAsync(ct);

        return sessions;
    }

    public async Task<Result> RevokeSessionAsync(Guid userId, Guid sessionId, string reason, CancellationToken ct = default)
    {
        // When a non-platform admin revokes another user's session, enforce same-tenant membership and protect platform accounts
        if (currentUser.IsAuthenticated && !currentUser.IsPlatformUser && currentUser.UserId != userId)
        {
            var scopeCheck = await EnsureTargetUserManageableByTenantAdminAsync(userId, ct);
            if (scopeCheck.IsFailure)
            {
                return scopeCheck;
            }
        }

        var session = await db.UserSessions.FirstOrDefaultAsync(s => s.Id == sessionId && s.UserId == userId, ct);
        if (session is null)
        {
            return Result.Failure(Error.NotFound("SESSION_NOT_FOUND", "Session was not found."));
        }

        if (currentUser.IsAuthenticated && !currentUser.IsPlatformUser && currentUser.UserId != userId &&
            session.TenantId != tenantContext.TenantId)
        {
            return Result.Failure(Error.Forbidden("CROSS_TENANT_SESSION_REVOKE_DENIED", "Cannot revoke a session belonging to another tenant."));
        }

        var now = clock.UtcNow;
        session.Revoke(reason, now);

        var tokens = await db.RefreshTokens.Where(t => t.SessionId == sessionId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var token in tokens)
        {
            token.Revoke(reason, now);
        }

        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.session.revoke", "UserSession", sessionId.ToString("D"), session.TenantId, "Success", Reason: reason), ct);

        return Result.Success();
    }

    public async Task<Result<UserDto>> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password) || string.IsNullOrWhiteSpace(request.FullName))
        {
            return Error.Validation("USER_INVALID_INPUT", "Email, password, and full name are required.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return Error.Conflict("USER_ALREADY_EXISTS", $"User with email '{email}' already exists.");
        }

        // Enforce strict tenant-scope boundaries when invoked by a non-platform authenticated actor
        if (currentUser.IsAuthenticated && !currentUser.IsPlatformUser)
        {
            if (request.IsPlatformUser)
            {
                return Error.Forbidden("USER_PLATFORM_ESCALATION_DENIED", "Tenant administrators cannot create platform-scoped users.");
            }

            if (!tenantContext.HasTenant)
            {
                return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "A valid tenant context is required.");
            }

            if (request.TenantId.HasValue && request.TenantId.Value != tenantContext.TenantId!.Value)
            {
                return Error.Forbidden("CROSS_TENANT_USER_CREATE_DENIED", "Tenant administrators cannot create users in another tenant.");
            }

            if (!string.IsNullOrWhiteSpace(request.RoleName) &&
                (string.Equals(request.RoleName.Trim(), Nexus.SharedKernel.Security.SystemRoles.PlatformSuperAdmin, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(request.RoleName.Trim(), Nexus.SharedKernel.Security.SystemRoles.PlatformSupport, StringComparison.OrdinalIgnoreCase)))
            {
                return Error.Forbidden("ROLE_SCOPE_VIOLATION", "Tenant administrators cannot assign platform-scoped roles.");
            }
        }

        var effectiveTenantId = currentUser.IsAuthenticated && !currentUser.IsPlatformUser
            ? tenantContext.TenantId
            : request.TenantId ?? tenantContext.TenantId;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
            IsPlatformUser = request.IsPlatformUser,
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow
        };

        var createResult = await userManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return Error.Validation("USER_CREATE_FAILED", string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }

        var profile = UserProfile.Create(user.Id, request.FullName);
        db.UserProfiles.Add(profile);
        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.UserCreatedV1,
            effectiveTenantId,
            correlationContext.CorrelationId,
            new { user.Id, user.Email, profile.FullName, user.IsPlatformUser },
            occurredAt: clock.UtcNow));

        await db.SaveChangesAsync(ct);

        if (effectiveTenantId.HasValue)
        {
            await tenantValidator.EnsureMembershipAsync(user.Id, effectiveTenantId.Value, ct);
        }

        if (!string.IsNullOrWhiteSpace(request.RoleName))
        {
            await permissionEvaluator.AssignRoleByNameAsync(
                user.Id,
                request.IsPlatformUser && !effectiveTenantId.HasValue ? null : effectiveTenantId,
                request.RoleName,
                ct);
        }

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.user.create", "User", user.Id.ToString("D"), effectiveTenantId, "Success",
            AfterState: new { user.Id, user.Email, profile.FullName, user.IsPlatformUser }), ct);

        return new UserDto(user.Id, user.Email!, profile.FullName, user.IsActive, user.IsPlatformUser, user.EmailConfirmed, false, user.CreatedAt);
    }

    public async Task<Result<UserDto>> InviteUserToCurrentTenantAsync(InviteUserRequest request, CancellationToken ct = default)
    {
        if (!tenantContext.HasTenant)
        {
            return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "A tenant context is required to invite users.");
        }

        var tenantId = tenantContext.TenantId!.Value;
        var email = request.Email.Trim().ToLowerInvariant();
        var existing = await userManager.FindByEmailAsync(email);

        if (existing is not null)
        {
            await tenantValidator.EnsureMembershipAsync(existing.Id, tenantId, ct);
            if (!string.IsNullOrWhiteSpace(request.RoleName))
            {
                await permissionEvaluator.AssignRoleByNameAsync(existing.Id, tenantId, request.RoleName, ct);
            }

            var prof = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == existing.Id, ct);
            db.StageOutboxEvent(EventEnvelope.Create(
                PlatformEventTypes.UserInvitedV1,
                tenantId,
                correlationContext.CorrelationId,
                new { existing.Id, existing.Email, TenantId = tenantId },
                occurredAt: clock.UtcNow));
            await db.SaveChangesAsync(ct);

            return new UserDto(existing.Id, existing.Email!, prof?.FullName ?? existing.Email!, existing.IsActive, existing.IsPlatformUser, existing.EmailConfirmed, await userManager.IsLockedOutAsync(existing), existing.CreatedAt);
        }

        var tempPassword = $"Nx!{CryptoHelper.GenerateSecureToken(18)}9aA";
        return await CreateUserAsync(new CreateUserRequest(email, tempPassword, request.FullName, tenantId, request.RoleName ?? "Employee", false), ct);
    }

    public async Task<Result<PagedResult<UserDto>>> ListUsersAsync(PagedRequest paging, CancellationToken ct = default)
    {
        IReadOnlyList<Guid>? allowedUserIds = null;
        if (currentUser.IsAuthenticated && !currentUser.IsPlatformUser)
        {
            if (!tenantContext.HasTenant)
            {
                return Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required to list tenant users.");
            }

            allowedUserIds = await tenantValidator.GetMemberUserIdsAsync(tenantContext.TenantId!.Value, ct);
        }

        var query = from u in db.Users.AsNoTracking()
                    join p in db.UserProfiles.AsNoTracking() on u.Id equals p.UserId into gp
                    from profile in gp.DefaultIfEmpty()
                    select new { User = u, FullName = profile != null ? profile.FullName : (u.Email ?? string.Empty) };

        if (allowedUserIds is not null)
        {
            query = query.Where(x => allowedUserIds.Contains(x.User.Id));
        }

        if (!string.IsNullOrWhiteSpace(paging.Search))
        {
            var term = paging.Search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                (x.User.Email != null && x.User.Email.ToLower().Contains(term)) ||
                x.FullName.ToLower().Contains(term));
        }

        var total = await query.LongCountAsync(ct);
        var now = clock.UtcNow;
        var items = await query
            .OrderByDescending(x => x.User.CreatedAt)
            .Skip(paging.Skip)
            .Take(paging.PageSize)
            .Select(x => new UserDto(
                x.User.Id,
                x.User.Email!,
                x.FullName,
                x.User.IsActive,
                x.User.IsPlatformUser,
                x.User.EmailConfirmed,
                x.User.LockoutEnd != null && x.User.LockoutEnd > now,
                x.User.CreatedAt))
            .ToListAsync(ct);

        return new PagedResult<UserDto>(items, paging.Page, paging.PageSize, total);
    }

    public async Task<Result> DisableUserAsync(Guid userId, string reason, CancellationToken ct = default)
    {
        if (currentUser.IsAuthenticated && !currentUser.IsPlatformUser)
        {
            var scopeCheck = await EnsureTargetUserManageableByTenantAdminAsync(userId, ct);
            if (scopeCheck.IsFailure)
            {
                return scopeCheck;
            }
        }

        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null) return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));

        user.IsActive = false;
        user.DisabledAt = clock.UtcNow;
        user.DisabledReason = reason;
        user.UpdatedAt = clock.UtcNow;
        await userManager.UpdateAsync(user);

        await RevokeAllUserSessionsInternalAsync(userId, $"AccountDisabled: {reason}", ct);

        db.StageOutboxEvent(EventEnvelope.Create(
            PlatformEventTypes.UserDisabledV1,
            tenantContext.TenantId,
            correlationContext.CorrelationId,
            new { UserId = userId, Reason = reason },
            occurredAt: clock.UtcNow));
        await db.SaveChangesAsync(ct);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.user.disable", "User", userId.ToString("D"), tenantContext.TenantId, "Success", Reason: reason), ct);

        return Result.Success();
    }

    public async Task<Result> EnableUserAsync(Guid userId, CancellationToken ct = default)
    {
        if (currentUser.IsAuthenticated && !currentUser.IsPlatformUser)
        {
            var scopeCheck = await EnsureTargetUserManageableByTenantAdminAsync(userId, ct);
            if (scopeCheck.IsFailure)
            {
                return scopeCheck;
            }
        }

        var user = await userManager.FindByIdAsync(userId.ToString("D"));
        if (user is null) return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));

        user.IsActive = true;
        user.DisabledAt = null;
        user.DisabledReason = null;
        user.UpdatedAt = clock.UtcNow;
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.UpdateAsync(user);

        await auditWriter.WriteAsync(new AuditEntryRequest(
            "identity.user.enable", "User", userId.ToString("D"), tenantContext.TenantId, "Success"), ct);

        return Result.Success();
    }

    private async Task<Result> EnsureTargetUserManageableByTenantAdminAsync(Guid targetUserId, CancellationToken ct)
    {
        if (!tenantContext.HasTenant)
        {
            return Result.Failure(Error.Forbidden("TENANT_CONTEXT_REQUIRED", "Tenant context is required."));
        }

        var target = await userManager.FindByIdAsync(targetUserId.ToString("D"));
        if (target is null)
        {
            return Result.Failure(Error.NotFound("USER_NOT_FOUND", "User not found."));
        }

        if (target.IsPlatformUser)
        {
            return Result.Failure(Error.Forbidden("CROSS_TENANT_USER_ACTION_DENIED", "Tenant administrators cannot manage platform accounts."));
        }

        var membership = await tenantValidator.ValidateMembershipAsync(targetUserId, tenantContext.TenantId!.Value, ct);
        if (membership is null || !membership.IsActiveMember)
        {
            return Result.Failure(Error.Forbidden("CROSS_TENANT_USER_ACTION_DENIED", "Target user is not an active member of your tenant."));
        }

        return Result.Success();
    }

    private async Task RevokeAllUserSessionsInternalAsync(Guid userId, string reason, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var sessions = await db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null).ToListAsync(ct);
        foreach (var s in sessions) s.Revoke(reason, now);

        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in tokens) t.Revoke(reason, now);

        await db.SaveChangesAsync(ct);
    }

    private async Task RecordLoginAttemptAsync(Guid? userId, string email, bool succeeded, string? reason, CancellationToken ct)
    {
        db.LoginAttempts.Add(LoginAttempt.Record(userId, email, succeeded, reason, currentUser.IpAddress, currentUser.UserAgent, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }
}

public sealed class UserDirectoryReader(IdentityModuleDbContext db, ISystemClock clock) : IUserDirectoryReader
{
    public async Task<UserSummary?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        var locked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > clock.UtcNow;
        return new UserSummary(user.Id, user.Email ?? string.Empty, profile?.FullName ?? user.Email ?? string.Empty, user.IsActive, locked);
    }

    public async Task<UserSummary?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToUpperInvariant();
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null) return null;

        var profile = await db.UserProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        var locked = user.LockoutEnd.HasValue && user.LockoutEnd.Value > clock.UtcNow;
        return new UserSummary(user.Id, user.Email ?? string.Empty, profile?.FullName ?? user.Email ?? string.Empty, user.IsActive, locked);
    }

    public async Task<bool> IsSessionActiveAsync(Guid userId, Guid sessionId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return await db.UserSessions.AsNoTracking()
            .AnyAsync(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null && s.ExpiresAt > now, ct);
    }
}
