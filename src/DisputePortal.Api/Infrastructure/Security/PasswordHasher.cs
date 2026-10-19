using System.Security.Cryptography;

namespace DisputePortal.Api.Infrastructure.Security;

/// <summary>PBKDF2-SHA256 password hashing. Format: <c>v1.{iterations}.{salt}.{hash}</c> (base64).</summary>
public static class PasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000; // OWASP 2023+ guidance for PBKDF2-HMAC-SHA256

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string encoded)
    {
        var parts = encoded.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
