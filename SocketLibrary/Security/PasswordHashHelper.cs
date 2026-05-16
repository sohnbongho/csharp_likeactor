using System.Security.Cryptography;
using System.Text;

namespace Library.Security;

public static class PasswordHashHelper
{
    private const int Iterations = 100_000;
    private const int HashSize = 32;

    public static byte[] ComputeClientHash(string plainPassword)
        => SHA256.HashData(Encoding.UTF8.GetBytes(plainPassword));

    public static (string HashBase64, string SaltBase64) GenerateStoredHash(ReadOnlySpan<byte> clientHashBytes)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(clientHashBytes, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    public static bool Verify(ReadOnlySpan<byte> clientHashBytes, string storedHashBase64, string storedSaltBase64)
    {
        var salt = Convert.FromBase64String(storedSaltBase64);
        var expected = Rfc2898DeriveBytes.Pbkdf2(clientHashBytes, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(expected, Convert.FromBase64String(storedHashBase64));
    }
}
