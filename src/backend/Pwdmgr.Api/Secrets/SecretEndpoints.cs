using Microsoft.EntityFrameworkCore;
using Pwdmgr.Api.Vaults;
using Pwdmgr.Application.Auth;
using Pwdmgr.Domain.Crypto;
using Pwdmgr.Domain.Secrets;
using Pwdmgr.Domain.Vaults;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Api.Secrets;

/// <summary><paramref name="Id"/> is chosen by the client so the AAD can bind it before the server sees the secret (ADR-0009).</summary>
public sealed record CreateSecretRequest(Guid Id, string Type, string NameCiphertext, string PayloadCiphertext, string WrappedDek, string AadHash);

/// <summary><paramref name="VersionNo"/> must be the latest + 1: the client sealed the payload with that number in the AAD, so the server never renumbers.</summary>
public sealed record NewVersionRequest(int VersionNo, string PayloadCiphertext, string WrappedDek, string AadHash);

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
        VaultEndpoints.AccessibleVaults(db, user, (_, v) => v);

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

        if (request.Id == Guid.Empty)
        {
            errors["id"] = ["client-generated random UUID required"];
        }

        if (!Base64Field.TryDecode(request.NameCiphertext, MinPayloadLength, Secret.NameCiphertextMaxLength, out var name))
        {
            errors["nameCiphertext"] = [$"base64 of {MinPayloadLength}..{Secret.NameCiphertextMaxLength} bytes"];
        }

        if (IsPayloadTooLarge(request.PayloadCiphertext))
        {
            return PayloadTooLarge();
        }

        var payloadResult = ValidateVersion(request.PayloadCiphertext, request.WrappedDek, request.AadHash, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (!await AccessibleVaults(db, user).AnyAsync(v => v.Id == vaultId, cancellationToken))
        {
            return Results.NotFound();
        }

        if (await db.Secrets.IgnoreQueryFilters().AnyAsync(s => s.Id == request.Id, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Secret id already exists");
        }

        var secret = new Secret
        {
            Id = request.Id,
            TenantId = tenant.TenantId,
            VaultId = vaultId,
            Type = request.Type!,
            NameCiphertext = name
        };
        var version = NewVersion(secret, 1, payloadResult, tenant.TenantId, user.UserId);
        db.Secrets.Add(secret);
        db.SecretVersions.Add(version);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Secret id already exists");
        }

        return Results.Created($"/api/v1/secrets/{secret.Id}/versions/latest", ToDto(version, secret.VaultId));
    }

    private static async Task<IResult> LatestAsync(Guid secretId, ICurrentUser user, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var secret = await FindAccessibleAsync(db, user, secretId, cancellationToken);
        if (secret is null)
        {
            return Results.NotFound();
        }

        var version = await db.SecretVersions.SingleAsync(v => v.SecretId == secretId && v.VersionNo == secret.LatestVersionNo, cancellationToken);
        return Results.Ok(ToDto(version, secret.VaultId));
    }

    private static async Task<IResult> AddVersionAsync(Guid secretId, NewVersionRequest request, ICurrentUser user, ICurrentTenant tenant, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.VersionNo < 2)
        {
            errors["versionNo"] = ["must be the latest version + 1"];
        }

        if (IsPayloadTooLarge(request.PayloadCiphertext))
        {
            return PayloadTooLarge();
        }

        var payloadResult = ValidateVersion(request.PayloadCiphertext, request.WrappedDek, request.AadHash, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var secret = await FindAccessibleAsync(db, user, secretId, cancellationToken);
        if (secret is null)
        {
            return Results.NotFound();
        }

        // The client sealed the payload with this version number in the AAD; a stale client
        // (someone else added a version meanwhile) gets 409 and must re-read, never a renumber.
        if (request.VersionNo != secret.LatestVersionNo + 1)
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Version conflict", detail: $"latest is {secret.LatestVersionNo}");
        }

        secret.LatestVersionNo = request.VersionNo;
        secret.UpdatedAt = DateTimeOffset.UtcNow;
        var version = NewVersion(secret, request.VersionNo, payloadResult, tenant.TenantId, user.UserId);
        db.SecretVersions.Add(version);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Version conflict");
        }

        return Results.Created($"/api/v1/secrets/{secret.Id}/versions/latest", ToDto(version, secret.VaultId));
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

    // Base64 of exactly PayloadMaxLength bytes is 87 384 chars; anything longer cannot decode to
    // ≤ 64 KiB. Lengths 65 537..65 538 share that Base64 length, so those are caught after decoding.
    private static readonly int MaxPayloadBase64Length = ((SecretVersion.PayloadMaxLength + 2) / 3) * 4;

    private static bool IsPayloadTooLarge(string? payload) =>
        payload is not null
        && (payload.Length > MaxPayloadBase64Length
            || (Base64Field.TryDecode(payload, 0, SecretVersion.PayloadMaxLength + 3, out var bytes) && bytes.Length > SecretVersion.PayloadMaxLength));

    private static IResult PayloadTooLarge() =>
        Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Payload too large", detail: $"max {SecretVersion.PayloadMaxLength} bytes");

    private static VersionBlobs ValidateVersion(string? payload, string? wrappedDek, string? aadHash, Dictionary<string, string[]> errors)
    {
        if (!Base64Field.TryDecode(payload, MinPayloadLength, SecretVersion.PayloadMaxLength, out var payloadBytes))
        {
            errors["payloadCiphertext"] = [$"base64 of {MinPayloadLength}..{SecretVersion.PayloadMaxLength} bytes"];
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

    private static SecretVersionDto ToDto(SecretVersion v, Guid vaultId) => new(
        v.SecretId,
        vaultId,
        v.VersionNo,
        Convert.ToBase64String(v.PayloadCiphertext),
        Convert.ToBase64String(v.WrappedDek),
        Convert.ToBase64String(v.AadHash),
        v.CryptoVersion,
        v.CreatedAt);
}
