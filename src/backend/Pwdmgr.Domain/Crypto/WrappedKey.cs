using Pwdmgr.Domain.Common;

namespace Pwdmgr.Domain.Crypto;

/// <summary>A resource key (today: a vault key) wrapped for one recipient's public key.</summary>
public sealed class WrappedKey : TenantScopedEntity
{
    public const int CiphertextMaxLength = 1024;

    public required WrappedKeyResourceType ResourceType { get; init; }

    public required Guid ResourceId { get; init; }

    public required WrappedKeyRecipientType RecipientType { get; init; }

    public required Guid RecipientId { get; init; }

    public required int KeyVersion { get; init; }

    /// <summary>Wrapping scheme version (ADR-0009); lets a future scheme change keep reading old blobs.</summary>
    public required int CryptoVersion { get; init; }

    public required byte[] Ciphertext { get; init; }
}

public enum WrappedKeyResourceType
{
    Vault = 1
}

public enum WrappedKeyRecipientType
{
    User = 1
}
