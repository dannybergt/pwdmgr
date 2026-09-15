using Pwdmgr.Domain.Common;

namespace Pwdmgr.Domain.Secrets;

/// <summary>A secret inside a vault; the server sees only ciphertext for name and payload.</summary>
public sealed class Secret : TenantScopedEntity
{
    public const int NameCiphertextMaxLength = 1024;
    public const int TypeMaxLength = 32;

    public required Guid VaultId { get; init; }

    /// <summary>Client-defined template name (e.g. <c>password</c>, <c>note</c>); metadata, not secret.</summary>
    public required string Type { get; init; }

    public required byte[] NameCiphertext { get; set; }

    public int LatestVersionNo { get; set; } = 1;

    public SecretStatus Status { get; set; } = SecretStatus.Active;
}

public enum SecretStatus
{
    Active = 1,
    Deleted = 2
}
