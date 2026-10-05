using System.Security.Cryptography;
using System.Text;

namespace restByAno.Infrastructure.Secrets;

/// <summary>
/// Windows DPAPI (CurrentUser) protection for secret values. The secret's id is mixed in as entropy, so a ciphertext
/// cannot be moved to another secret row and still decrypt. Only ciphertext is ever stored.
/// </summary>
public static class DpapiProtector
{
    private static byte[] Entropy(Guid secretId) => Encoding.UTF8.GetBytes($"restByAno.secret.v1:{secretId:N}");

    public static byte[] Protect(string plaintext, Guid secretId)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return ProtectedData.Protect(bytes, Entropy(secretId), DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Returns false when the data cannot be decrypted (other user/machine profile, damaged data).</summary>
    public static bool TryUnprotect(byte[] ciphertext, Guid secretId, out string? plaintext)
    {
        plaintext = null;
        byte[]? bytes = null;
        try
        {
            bytes = ProtectedData.Unprotect(ciphertext, Entropy(secretId), DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(bytes);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
