using Microsoft.EntityFrameworkCore;
using Pwdmgr.Application.Auth;
using Pwdmgr.Domain.Crypto;
using Pwdmgr.Domain.Vaults;
using Pwdmgr.Infrastructure.Persistence;

namespace Pwdmgr.Api.Vaults;

public sealed record CreateVaultRequest(string Type, string NameCiphertext, string WrappedVaultKey);

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

    /// <summary>Vaults the current user holds a wrapped key for; the caller's own wrapped key travels along so the client can unwrap.</summary>
    private static async Task<IResult> ListAsync(ICurrentUser user, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.WrappedKeys
            .Where(w => w.ResourceType == WrappedKeyResourceType.Vault && w.RecipientType == WrappedKeyRecipientType.User && w.RecipientId == user.UserId)
            .Join(db.Vaults.Where(v => v.Status == VaultStatus.Active), w => new { w.ResourceId, w.KeyVersion }, v => new { ResourceId = v.Id, v.KeyVersion }, (w, v) => new { Vault = v, Wrapped = w })
            .OrderBy(x => x.Vault.CreatedAt)
            .ToListAsync(cancellationToken);
        return Results.Ok(rows.Select(x => ToDto(x.Vault, x.Wrapped)));
    }

    private static async Task<IResult> CreateAsync(CreateVaultRequest request, ICurrentUser user, ICurrentTenant tenant, PwdmgrDbContext db, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (!string.Equals(request.Type, "personal", StringComparison.Ordinal))
        {
            errors["type"] = ["only 'personal' can be created in this version"];
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

        var vault = new Vault
        {
            Id = Guid.NewGuid(),
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
        await db.SaveChangesAsync(cancellationToken);
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
