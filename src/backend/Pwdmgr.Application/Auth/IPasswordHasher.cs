namespace Pwdmgr.Application.Auth;

public interface IPasswordHasher
{
    /// <summary>Returns a PHC-formatted Argon2id string (<c>$argon2id$v=19$m=..,t=..,p=..$salt$hash</c>).</summary>
    string Hash(string password);

    /// <summary>Constant-work verification: runs the full KDF even when the stored hash is a decoy.</summary>
    bool Verify(string phcHash, string password);

    /// <summary>A syntactically valid hash of an unknown password, used to equalise latency for unknown users.</summary>
    string DecoyHash { get; }
}
