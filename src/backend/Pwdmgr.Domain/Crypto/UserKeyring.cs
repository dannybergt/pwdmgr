using Pwdmgr.Domain.Common;

namespace Pwdmgr.Domain.Crypto;

/// <summary>
/// A user's asymmetric identity for key wrapping (ADR-0009). The private key is stored only
/// as ciphertext under the passphrase-derived KEK; the server can neither read nor recover it.
/// </summary>
public sealed class UserKeyring : TenantScopedEntity
{
    public const string Argon2id = "argon2id";
    public const int PublicKeyLength = 32;
    public const int KdfSaltMinLength = 16;
    public const int KdfSaltMaxLength = 64;
    public const int EncryptedPrivateKeyMaxLength = 4096;

    public required Guid UserId { get; init; }

    public required int CryptoVersion { get; init; }

    public required string KdfAlgorithm { get; init; }

    public required int KdfMemoryKib { get; init; }

    public required int KdfIterations { get; init; }

    public required int KdfParallelism { get; init; }

    public required byte[] KdfSalt { get; init; }

    public required byte[] PublicKey { get; init; }

    public required byte[] EncryptedPrivateKey { get; init; }
}
