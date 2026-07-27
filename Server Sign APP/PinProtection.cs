using System.Security.Cryptography;
using System.Text;

namespace KSKSigningManager;

internal static class PinProtection
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("KSK-SIGN-SERVER-PIN-v11");

    public static string Protect(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) return string.Empty;
        var plain = Encoding.UTF8.GetBytes(pin);
        var encrypted = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(plain);
        return Convert.ToBase64String(encrypted);
    }

    public static bool TryUnprotect(string? encryptedPin, out string pin)
    {
        pin = string.Empty;
        if (string.IsNullOrWhiteSpace(encryptedPin)) return false;
        try
        {
            var encrypted = Convert.FromBase64String(encryptedPin);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            pin = Encoding.UTF8.GetString(plain);
            CryptographicOperations.ZeroMemory(plain);
            return true;
        }
        catch { return false; }
    }
}
