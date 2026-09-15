namespace Pwdmgr.Domain.Crypto;

/// <summary>
/// Argon2id parameter floor and ceiling, identical to <c>KDF_MINIMUM</c>/<c>KDF_MAXIMUM</c> in the
/// browser (ADR-0006). The server rejects stored parameters outside this range so tampered
/// keyring metadata cannot downgrade a user's KDF.
/// </summary>
public static class KdfLimits
{
    public const int MinMemoryKib = 19 * 1024;
    public const int MinIterations = 2;
    public const int MinParallelism = 1;

    public const int MaxMemoryKib = 1024 * 1024;
    public const int MaxIterations = 16;
    public const int MaxParallelism = 16;

    public static bool IsAllowed(int memoryKib, int iterations, int parallelism) =>
        memoryKib >= MinMemoryKib && memoryKib <= MaxMemoryKib &&
        iterations >= MinIterations && iterations <= MaxIterations &&
        parallelism >= MinParallelism && parallelism <= MaxParallelism;
}
