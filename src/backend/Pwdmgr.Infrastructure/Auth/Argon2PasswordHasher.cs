using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Pwdmgr.Application.Auth;

namespace Pwdmgr.Infrastructure.Auth;

/// <summary>
/// Argon2id password verifier in PHC string format. Parameters follow ADR-0006 (same as the
/// browser KDF) so the frozen vectors in TESTING.md double as the cross-implementation anchor.
/// </summary>
public sealed class Argon2PasswordHasher : IPasswordHasher
{
    public const int MemoryKib = 65536;
    public const int Iterations = 3;
    public const int Parallelism = 4;
    public const int SaltLength = 16;
    public const int HashLength = 32;

    private const string Prefix = "$argon2id$v=19$";

    public Argon2PasswordHasher()
    {
        DecoyHash = Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
    }

    public string DecoyHash { get; }

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = Derive(password, salt, MemoryKib, Iterations, Parallelism, HashLength);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}m={MemoryKib},t={Iterations},p={Parallelism}${B64(salt)}${B64(hash)}");
    }

    public bool Verify(string phcHash, string password)
    {
        // Konscious rejects an empty password; the API boundary already requires one.
        if (password.Length == 0 || !TryParse(phcHash, out var m, out var t, out var p, out var salt, out var expected))
        {
            return false;
        }

        var actual = Derive(password, salt, m, t, p, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    internal static byte[] Derive(string password, byte[] salt, int memoryKib, int iterations, int parallelism, int length)
    {
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = memoryKib,
            Iterations = iterations,
            DegreeOfParallelism = parallelism
        };
        return argon2.GetBytes(length);
    }

    internal static bool TryParse(string phc, out int m, out int t, out int p, out byte[] salt, out byte[] hash)
    {
        m = t = p = 0;
        salt = hash = [];
        if (!phc.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = phc[Prefix.Length..].Split('$');
        if (parts.Length != 3)
        {
            return false;
        }

        foreach (var kv in parts[0].Split(','))
        {
            var eq = kv.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0 || !int.TryParse(kv.AsSpan(eq + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
            {
                return false;
            }

            switch (kv[..eq])
            {
                case "m": m = value; break;
                case "t": t = value; break;
                case "p": p = value; break;
                default: return false;
            }
        }

        if (m == 0 || t == 0 || p == 0)
        {
            return false;
        }

        try
        {
            salt = UnB64(parts[1]);
            hash = UnB64(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length >= 8 && hash.Length >= 16;
    }

    // PHC uses unpadded standard Base64.
    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');

    private static byte[] UnB64(string text) => Convert.FromBase64String(text.PadRight(text.Length + (4 - text.Length % 4) % 4, '='));
}
