using Microsoft.EntityFrameworkCore;
using Pwdmgr.Api.Vaults;
using Pwdmgr.Application.Auth;
using Pwdmgr.Domain.Crypto;
using Pwdmgr.Domain.Secrets;
using Pwdmgr.Domain.Vaults;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Api.Secrets;

public sealed record CreateSecretRequest(string Type, string NameCiphertext, string PayloadCiphertext, string WrappedDek, string AadHash);

public sealed record NewVersionRequest(string PayloadCiphertext, string WrappedDek, string AadHash, string? NameCiphertext);

public sealed record SecretSummaryDto(Guid Id, Guid VaultId, string Type, string NameCiphertext, int LatestVersionNo, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt);

public sealed record SecretVersionDto(Guid SecretId, Guid VaultId, int VersionNo, string PayloadCiphertext, string WrappedDek, string AadHash, int CryptoVersion, DateTimeOffset CreatedAt);

/// <summary>
/// Ciphertext-only secret API. Every route first resolves the vault through the caller's
/// wrapped keys, so a vault the user holds no key for is indistinguishable from a vault that
/// does not exist (404). Versions are immutable; there is deliberately no PUT.
/// </summary>
public static class SecretEndpoints
{
    private const int MinPayloadLength = 12 + 16;
    private const int MinWrappedDekLength = 12 + 32 + 16;
    private static readonly HashSet<string> Types = ["password", "note"];

    public static RouteGroupBuilder MapSecrets(this RouteGroupBuilder api)
    {
        var vaultSecrets = api.MapGroup("/vaults/{vaultId:guid}/secrets").RequireAuthorization();
        vaultSecrets.MapGet("/", ListAsync);
        vaultSecrets.MapPost("/", CreateAsync);

        var secrets = api.MapGroup("/secrets/{secretId:guid}").RequireAuthorization();
        secrets.MapGet("/versions/latest", LatestAsync);
        secrets.MapPost("/versions", AddVersionAsync);
        secrets.MapDelete("/", DeleteAsync);
        return api;
    }

    private static IQueryable<Vault> AccessibleVaults(PwdmgrDbContext db, ICurrentUser user) =>
        db.WrappedKeys
            .Where(w => w.ResourceType == WrappedKeyResourceType.Vault && w.RecipientType == WrappedKeyRecipientType.User && w.RecipientId == user.UserId)
            .Join(db.Vaults.Where(v => v.Status == VaultStatus.Active), w => w.ResourceId, v => v.Id, (w, v) => v)
            .Distinct();

    private static async Task<IResult> ListAsync(Guid vaultId, ICurrentUser user, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        if (!await AccessibleVaults(db, user).AnyAsync(v => v.Id == vaultId, cancellationToken))
        {
            return Results.NotFound();
        }

        var rows = await db.Secrets
            .Where(s => s.VaultId == vaultId && s.Status == SecretStatus.Active)
            .OrderBy(s => s.CreatedAt)
            .Select(s => new SecretSummaryDto(s.Id, s.VaultId, s.Type, Convert.ToBase64String(s.NameCiphertext), s.LatestVersionNo, s.CreatedAt, s.UpdatedAt))
            .ToListAsync(cancellationToken);
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateAsync(Guid vaultId, CreateSecretRequest request, ICurrentUser user, ICurrentTenant tenant, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Type is null || !Types.Contains(request.Type))
        {
            errors["type"] = [$"one of {string.Join(", ", Types)}"];
        }

        if (!Base64Field.TryDecode(request.NameCiphertext, MinPayloadLength, Secret.NameCiphertextMaxLength, out var name))
        {
            errors["nameCiphertext"] = [$"base64 of {MinPayloadLength}..{Secret.NameCiphertextMaxLength} bytes"];
        }

        var payloadResult = ValidateVersion(request.PayloadCiphertext, request.WrappedDek, request.AadHash, errors);
        if (errors.Count > 0)
        {
            return errors.ContainsKey("payloadCiphertext") && errors["payloadCiphertext"][0].StartsWith("too large", StringComparison.Ordinal)
                ? Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Payload too large", detail: $"max {SecretVersion.PayloadMaxLength} bytes")
                : Results.ValidationProblem(errors);
        }

        if (!await AccessibleVaults(db, user).AnyAsync(v => v.Id == vaultId, cancellationToken))
        {
            return Results.NotFound();
        }

        var secret = new Secret
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            VaultId = vaultId,
            Type = request.Type!,
            NameCiphertext = name
        };
        var version = NewVersion(secret, 1, payloadResult, tenant.TenantId, user.UserId);
        db.Secrets.Add(secret);
        db.SecretVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/secrets/{secret.Id}/versions/latest", ToDto(version));
    }

    private static async Task<IResult> LatestAsync(Guid secretId, ICurrentUser user, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var secret = await FindAccessibleAsync(db, user, secretId, cancellationToken);
        if (secret is null)
        {
            return Results.NotFound();
        }

        var version = await db.SecretVersions.SingleAsync(v => v.SecretId == secretId && v.VersionNo == secret.LatestVersionNo, cancellationToken);
        return Results.Ok(ToDto(version));
    }

    private static async Task<IResult> AddVersionAsync(Guid secretId, NewVersionRequest request, ICurrentUser user, ICurrentTenant tenant, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        byte[]? name = null;
        if (request.NameCiphertext is not null && !Base64Field.TryDecode(request.NameCiphertext, MinPayloadLength, Secret.NameCiphertextMaxLength, out name))
        {
            errors["nameCiphertext"] = [$"base64 of {MinPayloadLength}..{Secret.NameCiphertextMaxLength} bytes"];
        }

        var payloadResult = ValidateVersion(request.PayloadCiphertext, request.WrappedDek, request.AadHash, errors);
        if (errors.Count > 0)
        {
            return errors.ContainsKey("payloadCiphertext") && errors["payloadCiphertext"][0].StartsWith("too large", StringComparison.Ordinal)
                ? Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Payload too large", detail: $"max {SecretVersion.PayloadMaxLength} bytes")
                : Results.ValidationProblem(errors);
        }

        var secret = await FindAccessibleAsync(db, user, secretId, cancellationToken);
        if (secret is null)
        {
            return Results.NotFound();
        }

        secret.LatestVersionNo += 1;
        secret.UpdatedAt = DateTimeOffset.UtcNow;
        if (name is not null)
        {
            secret.NameCiphertext = name;
        }

        var version = NewVersion(secret, secret.LatestVersionNo, payloadResult, tenant.TenantId, user.UserId);
        db.SecretVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/secrets/{secret.Id}/versions/latest", ToDto(version));
    }

    private static async Task<IResult> DeleteAsync(Guid secretId, ICurrentUser user, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var secret = await FindAccessibleAsync(db, user, secretId, cancellationToken);
        if (secret is null)
        {
            return Results.NotFound();
        }

        // Soft delete: versions stay for audit/recovery; a hard purge is an operator action.
        secret.Status = SecretStatus.Deleted;
        secret.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<Secret?> FindAccessibleAsync(PwdmgrDbContext db, ICurrentUser user, Guid secretId, CancellationToken cancellationToken) =>
        await db.Secrets
            .Where(s => s.Id == secretId && s.Status == SecretStatus.Active)
            .Join(AccessibleVaults(db, user), s => s.VaultId, v => v.Id, (s, _) => s)
            .SingleOrDefaultAsync(cancellationToken);

    private sealed record VersionBlobs(byte[] Payload, byte[] WrappedDek, byte[] AadHash);

    private static VersionBlobs ValidateVersion(string? payload, string? wrappedDek, string? aadHash, Dictionary<string, string[]> errors)
    {
        // Decode with headroom so "valid but too large" can be answered with 413 rather than 400;
        // Kestrel's request-body limit bounds the worst case long before this point.
        if (!Base64Field.TryDecode(payload, MinPayloadLength, SecretVersion.PayloadMaxLength * 2, out var payloadBytes))
        {
            errors["payloadCiphertext"] = [$"base64 of {MinPayloadLength}..{SecretVersion.PayloadMaxLength} bytes"];
        }
        else if (payloadBytes.Length > SecretVersion.PayloadMaxLength)
        {
            errors["payloadCiphertext"] = [$"too large: max {SecretVersion.PayloadMaxLength} bytes"];
        }

        if (!Base64Field.TryDecode(wrappedDek, MinWrappedDekLength, SecretVersion.WrappedDekMaxLength, out var dekBytes))
        {
            errors["wrappedDek"] = [$"base64 of {MinWrappedDekLength}..{SecretVersion.WrappedDekMaxLength} bytes"];
        }

        if (!Base64Field.TryDecode(aadHash, SecretVersion.AadHashLength, SecretVersion.AadHashLength, out var aadBytes))
        {
            errors["aadHash"] = [$"base64 of exactly {SecretVersion.AadHashLength} bytes"];
        }

        return new VersionBlobs(payloadBytes, dekBytes, aadBytes);
    }

    private static SecretVersion NewVersion(Secret secret, int versionNo, VersionBlobs blobs, Guid tenantId, Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        SecretId = secret.Id,
        VersionNo = versionNo,
        PayloadCiphertext = blobs.Payload,
        WrappedDek = blobs.WrappedDek,
        AadHash = blobs.AadHash,
        CryptoVersion = KeyringEndpoints.SupportedCryptoVersion,
        CreatedBy = userId
    };

    private static SecretVersionDto ToDto(SecretVersion v) => new(
        v.SecretId,
        Guid.Empty,
        v.VersionNo,
        Convert.ToBase64String(v.PayloadCiphertext),
        Convert.ToBase64String(v.WrappedDek),
        Convert.ToBase64String(v.AadHash),
        v.CryptoVersion,
        v.CreatedAt);
}
