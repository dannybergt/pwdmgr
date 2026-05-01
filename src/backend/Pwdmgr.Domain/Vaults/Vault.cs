using Pwdmgr.Domain.Common;

namespace Pwdmgr.Domain.Vaults;

public sealed class Vault : TenantScopedEntity
{
    public required string NameCiphertext { get; set; }

    public required VaultType Type { get; init; }

    public required string CryptoVersion { get; init; }

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

