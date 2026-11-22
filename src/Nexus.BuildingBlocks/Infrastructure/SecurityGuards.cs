using System.Net;
using System.Net.Sockets;

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
                    || (bytes[0] == 192 && bytes[1] == 168);
            }

            if (ip.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
            {
                var isLinkLocal = bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80;
                var isUniqueLocal = (bytes[0] & 0xFE) == 0xFC;
                return isLinkLocal || isUniqueLocal;
            }
        }

        return false;
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
