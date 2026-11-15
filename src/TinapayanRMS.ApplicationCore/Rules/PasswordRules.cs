using System.Security.Cryptography;
using System.Text;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// The app's password (AccessSettings): what one may be, how it's kept, and how a typed one is checked. It's kept as a
/// PBKDF2-HMAC-SHA256 hash with a random salt of its own, never as typed, and checked in constant time. A hash records the
/// iterations it was made with, so raising <see cref="Iterations"/> leaves the passwords made before still working.
/// </summary>
public static class PasswordRules
{
    public const int MinLength = 6;

    public const int MaxLength = 128;

    /// <summary>PBKDF2 iterations for a new hash: OWASP's figure for HMAC-SHA256, a third of a second on the shop's PC.</summary>
    public const int Iterations = 600_000;

    public const int SaltBytes = 16;

    public const int HashBytes = 32;

    /// <summary>The longest an auto-lock can be set to, in minutes (four hours); 0 is never.</summary>
    public const int MaxAutoLockMinutes = 240;

    /// <summary>Refuses a password too short or too long. Spaces count, wherever they are: it's kept exactly as typed.</summary>
    public static void EnsureValid(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            throw new BusinessRuleException($"The password must have at least {MinLength} characters.");
        }

        if (password.Length > MaxLength)
        {
            throw new BusinessRuleException($"The password can have at most {MaxLength} characters.");
        }
    }

    /// <summary>Refuses a new password typed differently the second time.</summary>
    public static void EnsureMatches(string? password, string? confirmation)
    {
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            throw new BusinessRuleException("The two passwords aren't the same: type the new one again in both boxes.");
        }
    }

    public static void EnsureValidAutoLock(int minutes)
    {
        if (minutes is < 0 or > MaxAutoLockMinutes)
        {
            throw new BusinessRuleException($"The auto-lock must be from 0 (never) to {MaxAutoLockMinutes} minutes.");
        }
    }

    /// <summary>A new salt and the hash of <paramref name="password"/> with it, at <see cref="Iterations"/>.</summary>
    public static (byte[] Hash, byte[] Salt, int Iterations) Hash(string password)
    {
        EnsureValid(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        return (Derive(password, salt, Iterations), salt, Iterations);
    }

    /// <summary>Whether <paramref name="password"/> is the one <paramref name="hash"/> was made from.</summary>
    public static bool Verify(string? password, byte[] hash, byte[] salt, int iterations)
    {
        if (string.IsNullOrEmpty(password) || password.Length > MaxLength || iterations <= 0 || hash.Length == 0)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(Derive(password, salt, iterations, hash.Length), hash);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, length);
}
