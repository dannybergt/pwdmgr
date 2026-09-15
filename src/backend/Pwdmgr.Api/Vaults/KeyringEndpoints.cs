using Microsoft.EntityFrameworkCore;
using Pwdmgr.Application.Auth;
using Pwdmgr.Domain.Crypto;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Api.Vaults;

public sealed record KdfParamsDto(string Algorithm, int MemoryKib, int Iterations, int Parallelism);

public sealed record KeyringDto(int CryptoVersion, KdfParamsDto Kdf, string KdfSalt, string PublicKey, string EncryptedPrivateKey);

public static class KeyringEndpoints
{
    public const int SupportedCryptoVersion = 1;

    public static RouteGroupBuilder MapKeyring(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").RequireAuthorization();
        me.MapGet("/keyring", GetAsync);
        me.MapPut("/keyring", EnrolAsync);
        return api;
    }

    private static async Task<IResult> GetAsync(ICurrentUser user, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var keyring = await db.UserKeyrings.SingleOrDefaultAsync(k => k.UserId == user.UserId, cancellationToken);
        return keyring is null ? Results.NotFound() : Results.Ok(ToDto(keyring));
    }

    /// <summary>Enrolment only: a keyring is written once. Re-enrolment (passphrase change, recovery) is a later slice with its own rules.</summary>
    private static async Task<IResult> EnrolAsync(KeyringDto request, ICurrentUser user, ICurrentTenant tenant, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.CryptoVersion != SupportedCryptoVersion)
        {
            errors["cryptoVersion"] = [$"only version {SupportedCryptoVersion} is supported"];
        }

        if (request.Kdf is null || !string.Equals(request.Kdf.Algorithm, UserKeyring.Argon2id, StringComparison.Ordinal))
        {
            errors["kdf.algorithm"] = [$"must be {UserKeyring.Argon2id}"];
        }
        else if (!KdfLimits.IsAllowed(request.Kdf.MemoryKib, request.Kdf.Iterations, request.Kdf.Parallelism))
        {
            errors["kdf"] = [$"parameters must be within m={KdfLimits.MinMemoryKib}..{KdfLimits.MaxMemoryKib} KiB, t={KdfLimits.MinIterations}..{KdfLimits.MaxIterations}, p={KdfLimits.MinParallelism}..{KdfLimits.MaxParallelism}"];
        }

        if (!Base64Field.TryDecode(request.KdfSalt, UserKeyring.KdfSaltMinLength, UserKeyring.KdfSaltMaxLength, out var salt))
        {
            errors["kdfSalt"] = [$"base64 of {UserKeyring.KdfSaltMinLength}..{UserKeyring.KdfSaltMaxLength} bytes"];
        }

        if (!Base64Field.TryDecode(request.PublicKey, UserKeyring.PublicKeyLength, UserKeyring.PublicKeyLength, out var publicKey))
        {
            errors["publicKey"] = [$"base64 of exactly {UserKeyring.PublicKeyLength} bytes"];
        }

        if (!Base64Field.TryDecode(request.EncryptedPrivateKey, 28, UserKeyring.EncryptedPrivateKeyMaxLength, out var encryptedPrivateKey))
        {
            errors["encryptedPrivateKey"] = [$"base64 of 28..{UserKeyring.EncryptedPrivateKeyMaxLength} bytes"];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (await db.UserKeyrings.AnyAsync(k => k.UserId == user.UserId, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Keyring already enrolled");
        }

        db.UserKeyrings.Add(new UserKeyring
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            UserId = user.UserId,
            CryptoVersion = request.CryptoVersion,
            KdfAlgorithm = UserKeyring.Argon2id,
            KdfMemoryKib = request.Kdf!.MemoryKib,
            KdfIterations = request.Kdf.Iterations,
            KdfParallelism = request.Kdf.Parallelism,
            KdfSalt = salt,
            PublicKey = publicKey,
            EncryptedPrivateKey = encryptedPrivateKey
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Keyring already enrolled");
        }

        return Results.Created("/api/v1/me/keyring", ToDto(await db.UserKeyrings.SingleAsync(k => k.UserId == user.UserId, cancellationToken)));
    }

    private static KeyringDto ToDto(UserKeyring k) => new(
        k.CryptoVersion,
        new KdfParamsDto(k.KdfAlgorithm, k.KdfMemoryKib, k.KdfIterations, k.KdfParallelism),
        Convert.ToBase64String(k.KdfSalt),
        Convert.ToBase64String(k.PublicKey),
        Convert.ToBase64String(k.EncryptedPrivateKey));
}
