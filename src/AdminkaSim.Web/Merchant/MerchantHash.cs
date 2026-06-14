using System.Security.Cryptography;
using System.Text;

namespace AdminkaSim.Web.Merchant;

/// <summary>
/// FASTPAY v1 MD5 hashing — the exact scheme adminka's MerchantApi /
/// WebhookDispatcher use. We reproduce it bit-for-bit so the sim can both
/// SIGN outbound calls and VERIFY inbound callbacks.
/// </summary>
/// <remarks>
/// MD5 is cryptographically broken; it is mandated by the v1 wire contract
/// (business-logic.md §5) for FASTPAY compatibility — not a security choice.
/// Adminka pairs it with a per-MID IP allowlist (memory 97134a72).
/// </remarks>
public static class MerchantHash
{
    /// <summary>
    /// <c>ToHexStringLower(MD5(UTF8(concat(parts))))</c> — matches adminka's
    /// <c>MerchantHasher.Md5Hex</c> and <c>WebhookDeliveryService.ComputeV1Hash</c>.
    /// </summary>
    public static string Md5Hex(params string[] parts)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(parts));
#pragma warning disable CA5351 // Broken crypto — mandated by the v1 wire contract.
        var hash = MD5.HashData(bytes);
#pragma warning restore CA5351
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Constant-time compare of two hex hashes (length-mismatch-safe). Used to
    /// verify an inbound callback's <c>hash</c> without leaking timing.
    /// </summary>
    public static bool ConstantTimeEquals(string a, string b)
    {
        var ab = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ab, bb);
    }
}
