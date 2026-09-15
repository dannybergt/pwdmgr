using Pwdmgr.Infrastructure.Auth;

namespace Pwdmgr.Infrastructure.Tests;

public sealed class Argon2PasswordHasherTests
{
    private static readonly byte[] Salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();

    /// <summary>Same frozen vectors as the browser KDF (TESTING.md): proves Konscious == hash-wasm == argon2-cffi.</summary>
    [Theory]
    [InlineData("correct horse battery staple", "853b272a44db1421c02962669a55eb0994f3cab385ed1c4c79253eee19bab49e")]
    [InlineData("ﬁancé ①", "641be819c7e4f005084a1604c28df8953e100c98b6e4d13299ff42beec1cd9fb")]
    public void Matches_the_frozen_cross_implementation_vectors(string passphrase, string expectedHex)
    {
        var normalised = passphrase.Normalize(System.Text.NormalizationForm.FormKC);
        var kek = Argon2PasswordHasher.Derive(normalised, Salt, 65536, 3, 4, 32);
        Assert.Equal(expectedHex, Convert.ToHexString(kek).ToLowerInvariant());
    }

    [Fact]
    public void Hash_produces_phc_string_and_verifies()
    {
        var hasher = new Argon2PasswordHasher();
        var phc = hasher.Hash("hunter2");
        Assert.StartsWith("$argon2id$v=19$m=65536,t=3,p=4$", phc, StringComparison.Ordinal);
        Assert.True(hasher.Verify(phc, "hunter2"));
        Assert.False(hasher.Verify(phc, "hunter3"));
        Assert.NotEqual(phc, hasher.Hash("hunter2"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("$argon2i$v=19$m=65536,t=3,p=4$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("$argon2id$v=19$m=0,t=3,p=4$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=4$notbase64!$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("$argon2id$v=19$m=65536,t=3,p=4$AAAAAAAAAAAAAAAAAAAAAA")]
    public void Malformed_hashes_never_verify(string phc)
    {
        Assert.False(new Argon2PasswordHasher().Verify(phc, "anything"));
    }

    [Fact]
    public void Decoy_hash_is_well_formed_and_rejects_everything_plausible()
    {
        var hasher = new Argon2PasswordHasher();
        Assert.True(Argon2PasswordHasher.TryParse(hasher.DecoyHash, out _, out _, out _, out _, out _));
        Assert.False(hasher.Verify(hasher.DecoyHash, ""));
        Assert.False(hasher.Verify(hasher.DecoyHash, "password"));
    }
}
