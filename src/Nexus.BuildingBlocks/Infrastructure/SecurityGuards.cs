using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.BuildingBlocks.Infrastructure;

public static class SsrfGuard
{
    public static bool IsUnsafeTarget(Uri uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            return true;
        }

        var host = uri.DnsSafeHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host) ||
            host is "localhost" or "ip6-localhost" or "metadata.google.internal" ||
            host.EndsWith(".localhost", StringComparison.Ordinal) ||
            host.EndsWith(".local", StringComparison.Ordinal) ||
            host.EndsWith(".internal", StringComparison.Ordinal))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            return IsUnsafeIpAddress(ip);
        }

        return false;
    }

    public static bool IsUnsafeIpAddress(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            return bytes[0] == 0
                || bytes[0] == 10
                || bytes[0] == 127
                || (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127)
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] >= 224);
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            var isLinkLocal = bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80;
            var isUniqueLocal = (bytes[0] & 0xFE) == 0xFC;
            var isMulticast = bytes[0] == 0xFF;
            return isLinkLocal || isUniqueLocal || isMulticast;
        }

        return true;
    }

    /// <summary>
    /// DNS-rebinding defense: resolves host DNS records at connection time and verifies EVERY returned A/AAAA IP is public.
    /// </summary>
    public static async Task<(bool IsSafe, string? FailureReason, IPAddress[] ResolvedAddresses)> ValidateUriAndResolveDnsAsync(
        Uri uri,
        IReadOnlyCollection<string>? allowedHosts = null,
        CancellationToken ct = default)
    {
        if (IsUnsafeTarget(uri))
        {
            return (false, "Target URI is not a valid public HTTPS endpoint.", []);
        }

        var host = uri.DnsSafeHost.Trim().TrimEnd('.').ToLowerInvariant();
        if (allowedHosts is { Count: > 0 } && !allowedHosts.Any(h =>
            string.Equals(h.Trim(), host, StringComparison.OrdinalIgnoreCase) ||
            (h.StartsWith("*.", StringComparison.Ordinal) && host.EndsWith(h[1..], StringComparison.OrdinalIgnoreCase))))
        {
            return (false, $"Host '{host}' is not permitted by the provider outbound host allowlist.", []);
        }

        if (IPAddress.TryParse(host, out var literalIp))
        {
            return IsUnsafeIpAddress(literalIp)
                ? (false, "Target IP address falls within a private, loopback, link-local, or metadata range.", [])
                : (true, null, [literalIp]);
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, ct);
            if (addresses.Length == 0)
            {
                return (false, $"DNS resolution returned no addresses for '{host}'.", []);
            }

            foreach (var addr in addresses)
            {
                if (IsUnsafeIpAddress(addr))
                {
                    return (false, $"DNS rebinding / private IP protection blocked host '{host}' resolving to restricted network address.", []);
                }
            }

            return (true, null, addresses);
        }
        catch (SocketException)
        {
            return (false, $"Failed to resolve DNS for target host '{host}'.", []);
        }
    }
}

/// <summary>
/// Svix webhook signature verifier (used by both Clerk and Resend inbound provider webhooks).
/// Verifies HMAC-SHA256 over "{svix-id}.{svix-timestamp}.{rawBody}" with timestamp replay protection.
/// </summary>
public static class SvixSignatureVerifier
{
    public const string IdHeader = "svix-id";
    public const string TimestampHeader = "svix-timestamp";
    public const string SignatureHeader = "svix-signature";

    public static string ComputeSignature(string svixId, long unixTimestampSeconds, string rawBody, string signingSecret)
    {
        var secretBytes = DecodeSecret(signingSecret);
        var toSign = Encoding.UTF8.GetBytes($"{svixId}.{unixTimestampSeconds}.{rawBody}");
        using var hmac = new HMACSHA256(secretBytes);
        var digest = hmac.ComputeHash(toSign);
        return $"v1,{Convert.ToBase64String(digest)}";
    }

    public static bool Verify(
        string? svixId,
        string? svixTimestamp,
        string? svixSignatureHeader,
        string rawBody,
        string signingSecret,
        DateTimeOffset utcNow,
        int maxToleranceSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(svixId) ||
            string.IsNullOrWhiteSpace(svixTimestamp) ||
            string.IsNullOrWhiteSpace(svixSignatureHeader) ||
            string.IsNullOrWhiteSpace(signingSecret))
        {
            return false;
        }

        if (!long.TryParse(svixTimestamp.Trim(), out var ts))
        {
            return false;
        }

        var ageSeconds = Math.Abs(utcNow.ToUnixTimeSeconds() - ts);
        if (ageSeconds > Math.Max(1, maxToleranceSeconds))
        {
            return false;
        }

        byte[] secretBytes;
        try
        {
            secretBytes = DecodeSecret(signingSecret);
        }
        catch (FormatException)
        {
            return false;
        }

        var toSign = Encoding.UTF8.GetBytes($"{svixId.Trim()}.{ts}.{rawBody}");
        using var hmac = new HMACSHA256(secretBytes);
        var expectedDigest = hmac.ComputeHash(toSign);

        var candidates = svixSignatureHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var candidate in candidates)
        {
            var commaIdx = candidate.IndexOf(',');
            if (commaIdx <= 0) continue;
            var version = candidate[..commaIdx];
            if (!string.Equals(version, "v1", StringComparison.Ordinal)) continue;

            var sigB64 = candidate[(commaIdx + 1)..];
            try
            {
                var sigBytes = Convert.FromBase64String(sigB64);
                if (sigBytes.Length == expectedDigest.Length &&
                    CryptographicOperations.FixedTimeEquals(sigBytes, expectedDigest))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // Continue checking remaining signatures in header
            }
        }

        return false;
    }

    private static byte[] DecodeSecret(string signingSecret)
    {
        var trimmed = signingSecret.Trim();
        if (trimmed.StartsWith("whsec_", StringComparison.Ordinal))
        {
            return Convert.FromBase64String(trimmed["whsec_".Length..]);
        }

        return Encoding.UTF8.GetBytes(trimmed);
    }
}

/// <summary>
/// HMAC-SHA256 webhook signing and replay-protected verification helper.
/// Signature payload format: "{unixTimestamp}.{rawPayload}" -> "v1={hexHmacSha256}".
/// </summary>
public static class WebhookSignatureHelper
{
    public const string SignatureHeader = "X-Nexus-Signature";
    public const string TimestampHeader = "X-Nexus-Timestamp";

    public static string ComputeSignature(string rawPayload, long unixTimestampSeconds, string signingSecret)
    {
        var signedContent = $"{unixTimestampSeconds}.{rawPayload}";
        var digest = CryptoHelper.ComputeHmacSha256(signedContent, signingSecret);
        return $"v1={digest}";
    }

    public static bool VerifySignature(
        string rawPayload,
        long unixTimestampSeconds,
        string providedSignature,
        string signingSecret,
        DateTimeOffset utcNow,
        int maxToleranceSeconds = 300)
    {
        if (string.IsNullOrWhiteSpace(providedSignature) || string.IsNullOrWhiteSpace(signingSecret))
        {
            return false;
        }

        var ageSeconds = Math.Abs(utcNow.ToUnixTimeSeconds() - unixTimestampSeconds);
        if (ageSeconds > Math.Max(1, maxToleranceSeconds))
        {
            return false;
        }

        var expected = ComputeSignature(rawPayload, unixTimestampSeconds, signingSecret);
        return CryptoHelper.FixedTimeEquals(expected, providedSignature.Trim());
    }
}
