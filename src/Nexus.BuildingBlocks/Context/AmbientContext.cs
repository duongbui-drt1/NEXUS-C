using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Nexus.SharedKernel.Context;

namespace Nexus.BuildingBlocks.Context;

public sealed class AmbientTenantContext : ITenantContext
{
    private sealed record TenantState(Guid? TenantId, bool IsPlatformBypass, string? BypassReason);

    private readonly Stack<TenantState> _scopes = new();
    private TenantState _current = new(null, false, null);

    public Guid? TenantId => _current.TenantId;
    public bool HasTenant => _current.TenantId.HasValue && _current.TenantId.Value != Guid.Empty;
    public bool IsPlatformBypass => _current.IsPlatformBypass;
    public string? BypassReason => _current.BypassReason;

    public void SetTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("TenantId cannot be empty.", nameof(tenantId));
        }

        _current = new TenantState(tenantId, false, null);
    }

    public IDisposable BeginScope(Guid? tenantId, bool platformBypass = false, string? bypassReason = null)
    {
        _scopes.Push(_current);
        _current = new TenantState(tenantId, platformBypass, bypassReason);
        return new ScopeHandle(this);
    }

    private void PopScope()
    {
        if (_scopes.Count > 0)
        {
            _current = _scopes.Pop();
        }
    }

    private sealed class ScopeHandle(AmbientTenantContext owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.PopScope();
        }
    }
}

public sealed class AmbientCorrelationContext : ICorrelationContext
{
    private string _correlationId = Guid.NewGuid().ToString("D");

    public string CorrelationId => _correlationId;

    public void SetCorrelationId(string correlationId)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            _correlationId = correlationId.Trim();
        }
    }
}

public sealed class HttpCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId
    {
        get
        {
            var raw = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? Principal?.FindFirstValue("sub");
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public Guid? SessionId
    {
        get
        {
            var raw = Principal?.FindFirstValue("sid");
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public Guid? TenantId
    {
        get
        {
            var raw = Principal?.FindFirstValue("tid");
            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email) ?? Principal?.FindFirstValue("email");

    public string? Scope => Principal?.FindFirstValue("scope");

    public bool IsPlatformUser => string.Equals(Scope, "platform", StringComparison.OrdinalIgnoreCase);

    public bool IsServicePrincipal => string.Equals(Scope, "service", StringComparison.OrdinalIgnoreCase);

    public string? ServiceClientId => Principal?.FindFirstValue("client_id");

    public string? ApplicationCode => Principal?.FindFirstValue("app_code") ?? Principal?.FindFirstValue("app");

    public string? CredentialClass => Principal?.FindFirstValue("cred_class");

    public IReadOnlyList<string> GrantedScopes
    {
        get
        {
            if (Principal is null) return [];
            return Principal.FindAll("scp")
                .Select(c => c.Value)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    public string? IpAddress => httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString();
}
