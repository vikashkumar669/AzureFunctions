using System.Security.Cryptography;
using System.Text;

namespace Harbourline.Connect.Core.Integrations;

/// <summary>
/// Verifies SignBridge callbacks. SignBridge (fictional) signs each
/// callback body with HMAC-SHA256 and a shared secret, and sends the
/// signature as hex in the X-SignBridge-Signature header.
/// </summary>
public static class CallbackSignature
{
    // <listing:hmac-verify>
    public static bool IsValid(
        string body, string? signatureHex, string secret)
    {
        if (string.IsNullOrEmpty(signatureHex))
        {
            return false;
        }

        byte[] expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(body));                 // <1>

        byte[] actual;
        try
        {
            actual = Convert.FromHexString(signatureHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations
            .FixedTimeEquals(expected, actual);            // <2>
    }
    // </listing:hmac-verify>

    public static string Sign(string body, string secret) =>
        Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(body)));
}
