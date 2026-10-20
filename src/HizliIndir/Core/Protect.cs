using System.Security.Cryptography;
using System.Text;

namespace HizliIndir.Core;

/// <summary>Şifreleri Windows kullanıcı hesabına bağlı olarak saklar (DPAPI).</summary>
public static class Protect
{
    public static string Encrypt(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }
    public static string Decrypt(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser)); }
        catch { return ""; }
    }
}
