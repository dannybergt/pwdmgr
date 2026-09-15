using Microsoft.EntityFrameworkCore;
using Pwdmgr.Application.Auth;
using Pwdmgr.Domain.Crypto;
using Pwdmgr.Domain.Vaults;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Api.Vaults;

/// <summary><paramref name="Id"/> is chosen by the client so the vault-key AAD can bind it before the server sees the vault (ADR-0009).</summary>
public sealed record CreateVaultRequest(Guid Id, string Type, string NameCiphertext, string WrappedVaultKey);

public sealed record VaultDto(Guid Id, string Type, string NameCiphertext, int CryptoVersion, int KeyVersion, string WrappedVaultKey);

public static class VaultEndpoints
{
    public static RouteGroupBuilder MapVaults(this RouteGroupBuilder api)
    {
        var vaults = api.MapGroup("/vaults").RequireAuthorization();
        vaults.MapGet("/", ListAsync);
        vaults.MapPost("/", CreateAsync);
        return api;
    }

    /// <summary>
    /// Membership = the user holds a wrapped key for the vault's *current* key version. The one
    /// definition shared by every vault- and secret-scoped route; <paramref name="selector"/>
    /// picks what the caller needs so EF keeps the whole thing server-side.
    /// </summary>
    public static IQueryable<T> AccessibleVaults<T>(PwdmgrDbContext db, ICurrentUser user, System.Linq.Expressions.Expression<Func<WrappedKey, Vault, T>> selector) =>
        db.WrappedKeys
            .Where(w => w.ResourceType == WrappedKeyResourceType.Vault && w.RecipientType == WrappedKeyRecipientType.User && w.RecipientId == user.UserId)
            .Join(db.Vaults.Where(v => v.Status == VaultStatus.Active), w => new { w.ResourceId, w.KeyVersion }, v => new { ResourceId = v.Id, v.KeyVersion }, selector);

    /// <summary>Vaults the current user holds a wrapped key for; the caller's own wrapped key travels along so the client can unwrap.</summary>
    private static async Task<IResult> ListAsync(ICurrentUser user, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var rows = await AccessibleVaults(db, user, (w, v) => new { Vault = v, Wrapped = w }).OrderBy(x => x.Vault.CreatedAt).ToListAsync(cancellationToken);
        return Results.Ok(rows.Select(x => ToDto(x.Vault, x.Wrapped)));
    }

    private static async Task<IResult> CreateAsync(CreateVaultRequest request, ICurrentUser user, ICurrentTenant tenant, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!string.Equals(request.Type, "personal", StringComparison.Ordinal))
        {
            errors["type"] = ["only 'personal' can be created in this version"];
        }

        if (request.Id == Guid.Empty)
        {
            errors["id"] = ["client-generated random UUID required"];
        }

        if (!Base64Field.TryDecode(request.NameCiphertext, 28, Vault.NameCiphertextMaxLength, out var nameCiphertext))
        {
            errors["nameCiphertext"] = [$"base64 of 28..{Vault.NameCiphertextMaxLength} bytes"];
        }

        if (!Base64Field.TryDecode(request.WrappedVaultKey, 32 + 12 + 32 + 16, WrappedKey.CiphertextMaxLength, out var wrappedVaultKey))
        {
            errors["wrappedVaultKey"] = [$"base64 of 92..{WrappedKey.CiphertextMaxLength} bytes"];
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        if (!await db.UserKeyrings.AnyAsync(k => k.UserId == user.UserId, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Enrol a keyring before creating vaults");
        }

        if (await db.Vaults.IgnoreQueryFilters().AnyAsync(v => v.Id == request.Id, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Vault id already exists");
        }

        var vault = new Vault
        {
            Id = request.Id,
            TenantId = tenant.TenantId,
            Type = VaultType.Personal,
            NameCiphertext = nameCiphertext,
            CryptoVersion = KeyringEndpoints.SupportedCryptoVersion
        };
        var wrapped = new WrappedKey
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.TenantId,
            ResourceType = WrappedKeyResourceType.Vault,
            ResourceId = vault.Id,
            RecipientType = WrappedKeyRecipientType.User,
            RecipientId = user.UserId,
            KeyVersion = vault.KeyVersion,
            CryptoVersion = KeyringEndpoints.SupportedCryptoVersion,
            Ciphertext = wrappedVaultKey
        };
        db.Vaults.Add(vault);
        db.WrappedKeys.Add(wrapped);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Vault id already exists");
        }

        return Results.Created($"/api/v1/vaults/{vault.Id}", ToDto(vault, wrapped));
    }

    private static VaultDto ToDto(Vault v, WrappedKey w) => new(
        v.Id,
        v.Type.ToString().ToLowerInvariant(),
        Convert.ToBase64String(v.NameCiphertext),
        v.CryptoVersion,
        v.KeyVersion,
        Convert.ToBase64String(w.Ciphertext));
}
