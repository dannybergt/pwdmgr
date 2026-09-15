using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Pwdmgr.Api.Secrets;
using Pwdmgr.Api.Vaults;
using Pwdmgr.Infrastructure.Tests;

namespace Pwdmgr.Api.Tests;

public sealed class SecretTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly Uri Keyring = new("/api/v1/me/keyring", UriKind.Relative);
    private static readonly Uri Vaults = new("/api/v1/vaults", UriKind.Relative);

    private static string B64(int length, byte fill = 0x42) => Convert.ToBase64String(Enumerable.Repeat(fill, length).ToArray());

    private static CreateSecretRequest NewSecret(byte fill = 0x10) => new(Guid.NewGuid(), "password", B64(40, fill), B64(120, fill), B64(60, fill), B64(32, fill));

    private async Task<(HttpClient Client, Guid VaultId)> NewUserWithVaultAsync()
    {
        var client = await factory.CreateAuthenticatedClientAsync($"user-{Guid.NewGuid():N}@example.test");
        Assert.Equal(HttpStatusCode.Created, (await client.PutAsJsonAsync(Keyring, new KeyringDto(1, new KdfParamsDto("argon2id", 65536, 3, 4), B64(16), B64(32, 1), B64(76, 2)), TestContext.Current.CancellationToken)).StatusCode);
        var created = await client.PostAsJsonAsync(Vaults, new CreateVaultRequest(Guid.NewGuid(), "personal", B64(40), B64(92)), TestContext.Current.CancellationToken);
        var vault = await created.Content.ReadFromJsonAsync<VaultDto>(TestContext.Current.CancellationToken);
        return (client, vault!.Id);
    }

    [Fact]
    public async Task Create_list_latest_new_version_delete_chain()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var (client, vaultId) = await NewUserWithVaultAsync();
        using var _ = client;
        var secretsUri = new Uri($"/api/v1/vaults/{vaultId}/secrets", UriKind.Relative);

        var created = await client.PostAsJsonAsync(secretsUri, NewSecret(), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var v1 = await created.Content.ReadFromJsonAsync<SecretVersionDto>(TestContext.Current.CancellationToken);
        Assert.NotNull(v1);
        Assert.Equal(1, v1.VersionNo);
        Assert.Equal(vaultId, v1.VaultId);
        Assert.Equal(B64(32, 0x10), v1.AadHash);

        // Client-chosen id: conflict on reuse, 400 on empty.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(secretsUri, NewSecret(0x11) with { Id = v1.SecretId }, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(secretsUri, NewSecret(0x11) with { Id = Guid.Empty }, TestContext.Current.CancellationToken)).StatusCode);

        var list = await client.GetFromJsonAsync<List<SecretSummaryDto>>(secretsUri, TestContext.Current.CancellationToken);
        var summary = Assert.Single(list!, s => s.Id == v1.SecretId);
        Assert.Equal("password", summary.Type);
        Assert.Equal(1, summary.LatestVersionNo);

        var latestUri = new Uri($"/api/v1/secrets/{v1.SecretId}/versions/latest", UriKind.Relative);
        var latest = await client.GetFromJsonAsync<SecretVersionDto>(latestUri, TestContext.Current.CancellationToken);
        Assert.Equal(B64(120, 0x10), latest!.PayloadCiphertext);

        var v2Response = await client.PostAsJsonAsync(new Uri($"/api/v1/secrets/{v1.SecretId}/versions", UriKind.Relative), new NewVersionRequest(2, B64(130, 0x20), B64(60, 0x20), B64(32, 0x20)), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, v2Response.StatusCode);
        var v2 = await v2Response.Content.ReadFromJsonAsync<SecretVersionDto>(TestContext.Current.CancellationToken);
        Assert.Equal(2, v2!.VersionNo);
        Assert.Equal(vaultId, v2.VaultId);
        var latest2 = await client.GetFromJsonAsync<SecretVersionDto>(latestUri, TestContext.Current.CancellationToken);
        Assert.Equal(B64(130, 0x20), latest2!.PayloadCiphertext);
        Assert.Equal(vaultId, latest2.VaultId);

        // A stale client (still thinks latest is 1) must get 409, never a renumbered version.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(new Uri($"/api/v1/secrets/{v1.SecretId}/versions", UriKind.Relative), new NewVersionRequest(2, B64(130, 0x21), B64(60, 0x21), B64(32, 0x21)), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(new Uri($"/api/v1/secrets/{v1.SecretId}/versions", UriKind.Relative), new NewVersionRequest(4, B64(130, 0x21), B64(60, 0x21), B64(32, 0x21)), TestContext.Current.CancellationToken)).StatusCode);

        // Versions are immutable: no route rewrites one.
        Assert.Contains((await client.PutAsJsonAsync(new Uri($"/api/v1/secrets/{v1.SecretId}/versions/1", UriKind.Relative), new { }, TestContext.Current.CancellationToken)).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });

        await using (var db = factory.CreateDbContext(factory.TenantId))
        {
            Assert.Equal(2, await db.SecretVersions.CountAsync(v => v.SecretId == v1.SecretId, TestContext.Current.CancellationToken));
        }

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(new Uri($"/api/v1/secrets/{v1.SecretId}", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(latestUri, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(new Uri($"/api/v1/secrets/{v1.SecretId}/versions", UriKind.Relative), new NewVersionRequest(3, B64(130), B64(60), B64(32)), TestContext.Current.CancellationToken)).StatusCode);
        Assert.DoesNotContain(await client.GetFromJsonAsync<List<SecretSummaryDto>>(secretsUri, TestContext.Current.CancellationToken) ?? [], s => s.Id == v1.SecretId);

        await using (var db = factory.CreateDbContext(factory.TenantId))
        {
            // Soft delete keeps the rows.
            Assert.Equal(2, await db.SecretVersions.CountAsync(v => v.SecretId == v1.SecretId, TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task Foreign_vault_and_foreign_secret_are_404()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var (owner, vaultId) = await NewUserWithVaultAsync();
        var (stranger, _) = await NewUserWithVaultAsync();
        using var _ = owner;
        using var __ = stranger;
        var created = await owner.PostAsJsonAsync(new Uri($"/api/v1/vaults/{vaultId}/secrets", UriKind.Relative), NewSecret(), TestContext.Current.CancellationToken);
        var v1 = await created.Content.ReadFromJsonAsync<SecretVersionDto>(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(new Uri($"/api/v1/vaults/{vaultId}/secrets", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(new Uri($"/api/v1/vaults/{vaultId}/secrets", UriKind.Relative), NewSecret(), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(new Uri($"/api/v1/secrets/{v1!.SecretId}/versions/latest", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync(new Uri($"/api/v1/secrets/{v1.SecretId}/versions", UriKind.Relative), new NewVersionRequest(2, B64(130), B64(60), B64(32)), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.DeleteAsync(new Uri($"/api/v1/secrets/{v1.SecretId}", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(new Uri($"/api/v1/vaults/{Guid.NewGuid()}/secrets", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);

        // Another tenant: same five routes, same answer.
        var (slug, email) = await factory.CreateTenantWithUserAsync();
        using var foreign = await factory.CreateAuthenticatedClientAsync(email, slug);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(new Uri($"/api/v1/vaults/{vaultId}/secrets", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync(new Uri($"/api/v1/secrets/{v1.SecretId}/versions/latest", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.DeleteAsync(new Uri($"/api/v1/secrets/{v1.SecretId}", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task Oversized_payload_is_413_and_malformed_fields_400()
    {
        PostgresDatabase.SkipUnlessConfigured();
        var (client, vaultId) = await NewUserWithVaultAsync();
        using var _ = client;
        var uri = new Uri($"/api/v1/vaults/{vaultId}/secrets", UriKind.Relative);

        var big = await client.PostAsJsonAsync(uri, NewSecret() with { PayloadCiphertext = B64(64 * 1024 + 1) }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, big.StatusCode);
        var huge = await client.PostAsJsonAsync(uri, NewSecret() with { PayloadCiphertext = B64(200 * 1024) }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);
        var exact = await client.PostAsJsonAsync(uri, NewSecret(0x50) with { PayloadCiphertext = B64(64 * 1024) }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, exact.StatusCode);

        var bad = await client.PostAsJsonAsync(uri, NewSecret() with { AadHash = B64(31), WrappedDek = "nope", Type = "rocket" }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var body = await bad.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("aadHash", body);
        Assert.Contains("wrappedDek", body);
        Assert.Contains("type", body);
    }
}
