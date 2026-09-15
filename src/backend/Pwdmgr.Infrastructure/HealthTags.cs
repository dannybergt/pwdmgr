namespace Pwdmgr.Infrastructure;

public static class HealthTags
{
    /// <summary>Checks that gate <c>/health/ready</c>; liveness runs none of them.</summary>
    public const string Ready = "ready";
}
