using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Pwdmgr.Api.Auth;
using Pwdmgr.Api.Vaults;
using Pwdmgr.Domain.Identity;
using Pwdmgr.Domain.Tenants;
using Pwdmgr.Infrastructure.Auth;
using Pwdmgr.Infrastructure.Tests;

namespace Pwdmgr.Api.Tests;

public sealed class KeyringAndVaultTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri Keyring = new("/api/v1/me/keyring", UriKind.Relative);
    private static readonly Uri Vaults = new("/api/v1/vaults", UriKind.Relative);

    private static string B64(int length, byte fill = 0x42) => Convert.ToBase64String(Enumerable.Repeat(fill, length).ToArray());

    private static KeyringDto ValidKeyring() => new(1, new KdfParamsDto("argon2id", 65536, 3, 4), B64(16), B64(32, 0x01), B64(76, 0x02));

    [Fact]
    public async Task Keyring_requires_authentication()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = factory.CreateApiClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Keyring, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(Keyring, ValidKeyring(), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Vaults, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Enrolment_is_write_once_and_readable_back_byte_for_byte()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = await factory.CreateAuthenticatedClientAsync();

        var before = await client.GetAsync(Keyring, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, before.StatusCode);

        var request = ValidKeyring();
        var created = await client.PutAsJsonAsync(Keyring, request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var stored = await client.GetFromJsonAsync<KeyringDto>(Keyring, TestContext.Current.CancellationToken);
        Assert.Equal(request, stored);

        var again = await client.PutAsJsonAsync(Keyring, request with { KdfSalt = B64(16, 0x99) }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(request, await client.GetFromJsonAsync<KeyringDto>(Keyring, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("kdf", 19 * 1024 - 1, 3, 4)]
    [InlineData("kdf", 65536, 1, 4)]
    [InlineData("kdf", 65536, 3, 0)]
    [InlineData("kdf", 2 * 1024 * 1024, 3, 4)]
    [InlineData("kdf", 65536, 17, 4)]
    [InlineData("kdf", 65536, 3, 17)]
    public async Task Kdf_parameters_outside_the_allowed_range_are_rejected(string field, int m, int t, int p)
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = await factory.CreateAuthenticatedClientAsync(NewUserEmail());
        var response = await client.PutAsJsonAsync(Keyring, ValidKeyring() with { Kdf = new KdfParamsDto("argon2id", m, t, p) }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(TestContext.Current.CancellationToken);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Fact]
    public async Task Malformed_fields_are_rejected_without_echoing_content()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var client = await factory.CreateAuthenticatedClientAsync(NewUserEmail());
        var bad = ValidKeyring() with { PublicKey = B64(31), KdfSalt = "not base64!", EncryptedPrivateKey = B64(5000) };
        var response = await client.PutAsJsonAsync(Keyring, bad, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("publicKey", body);
        Assert.Contains("kdfSalt", body);
        Assert.Contains("encryptedPrivateKey", body);
        Assert.DoesNotContain(B64(31), body);
    }

    [Fact]
    public async Task Vault_needs_a_keyring_then_is_listed_only_for_its_holder()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var owner = await factory.CreateAuthenticatedClientAsync(NewUserEmail());
        using var other = await factory.CreateAuthenticatedClientAsync(NewUserEmail());

        var noKeyring = await owner.PostAsJsonAsync(Vaults, new CreateVaultRequest(Guid.NewGuid(), "personal", B64(40), B64(92)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Conflict, noKeyring.StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await owner.PutAsJsonAsync(Keyring, ValidKeyring(), TestContext.Current.CancellationToken)).StatusCode);
        var created = await owner.PostAsJsonAsync(Vaults, new CreateVaultRequest(Guid.NewGuid(), "personal", B64(40, 0x0a), B64(92, 0x0b)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var vault = await created.Content.ReadFromJsonAsync<VaultDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(vault);
        Assert.Equal("personal", vault.Type);
        Assert.Equal(1, vault.KeyVersion);
        Assert.Equal(B64(92, 0x0b), vault.WrappedVaultKey);

        var mine = await owner.GetFromJsonAsync<List<VaultDto>>(Vaults, TestContext.Current.CancellationToken);
        Assert.Contains(mine!, v => v.Id == vault.Id && v.NameCiphertext == B64(40, 0x0a));

        var theirs = await other.GetFromJsonAsync<List<VaultDto>>(Vaults, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(theirs!, v => v.Id == vault.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(Vaults, new CreateVaultRequest(Guid.NewGuid(), "shared", B64(40), B64(92)), TestContext.Current.CancellationToken)).StatusCode);

        // Client-chosen id is honoured once, conflicts afterwards, and must not be empty.
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(Vaults, new CreateVaultRequest(vault.Id, "personal", B64(40), B64(92)), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(Vaults, new CreateVaultRequest(Guid.Empty, "personal", B64(40), B64(92)), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Other_tenants_keyring_and_vaults_are_invisible()
    {
        PostgresDatabase.SkipUnlessConfigured();
        using var alice = await factory.CreateAuthenticatedClientAsync(NewUserEmail());
        Assert.Equal(HttpStatusCode.Created, (await alice.PutAsJsonAsync(Keyring, ValidKeyring(), TestContext.Current.CancellationToken)).StatusCode);
        var created = await alice.PostAsJsonAsync(Vaults, new CreateVaultRequest(Guid.NewGuid(), "personal", B64(40), B64(92)), TestContext.Current.CancellationToken);
        var vault = await created.Content.ReadFromJsonAsync<VaultDto>(TestContext.Current.CancellationToken);

        var (slug, email) = await factory.CreateTenantWithUserAsync();
        using var stranger = await factory.CreateAuthenticatedClientAsync(email, slug);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(Keyring, TestContext.Current.CancellationToken)).StatusCode);
        var list = await stranger.GetFromJsonAsync<List<VaultDto>>(Vaults, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(list!, v => v.Id == vault!.Id);

        await using var db = factory.CreateDbContext();
        Assert.Equal(1, await db.WrappedKeys.IgnoreQueryFilters().CountAsync(w => w.ResourceId == vault!.Id, TestContext.Current.CancellationToken));
    }

    private static string NewUserEmail() => $"user-{Guid.NewGuid():N}@example.test";
}
