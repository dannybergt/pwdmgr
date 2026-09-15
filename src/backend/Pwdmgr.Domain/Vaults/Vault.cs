using Pwdmgr.Domain.Common;

namespace Pwdmgr.Domain.Vaults;

/// <summary>A container of secrets. The server sees only the ciphertext of its name.</summary>
public sealed class Vault : TenantScopedEntity
{
    public const int NameCiphertextMaxLength = 1024;

    public required VaultType Type { get; init; }

    public required byte[] NameCiphertext { get; set; }

    public required int CryptoVersion { get; init; }

    /// <summary>Incremented when the vault key is rotated; wrapped keys carry the version they wrap.</summary>
    public int KeyVersion { get; set; } = 1;

    public VaultStatus Status { get; set; } = VaultStatus.Active;
}

public enum VaultType
{
    Personal = 1,
    Shared = 2,
    Recovery = 3
}

public enum VaultStatus
{
    Active = 1,
    Disabled = 2,
    OrphanedProtected = 3,
    RecoveryPending = 4,
    PendingDeletion = 5
}
