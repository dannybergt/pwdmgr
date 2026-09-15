using Pwdmgr.Domain.Common;

namespace Pwdmgr.Domain.Secrets;

/// <summary>Immutable: an update creates a new version, nothing is ever rewritten in place.</summary>
public sealed class SecretVersion : TenantScopedEntity
{
    public const int PayloadMaxLength = 64 * 1024;
    public const int WrappedDekMaxLength = 256;
    public const int AadHashLength = 32;

    public required Guid SecretId { get; init; }

    public required int VersionNo { get; init; }

    public required byte[] PayloadCiphertext { get; init; }

    public required byte[] WrappedDek { get; init; }

    /// <summary>SHA-256 of the AAD the client used, stored as delivered so the client can detect a mismatch before decrypting.</summary>
    public required byte[] AadHash { get; init; }

    public required int CryptoVersion { get; init; }

    public required Guid CreatedBy { get; init; }
}
