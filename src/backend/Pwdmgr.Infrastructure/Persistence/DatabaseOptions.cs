namespace Pwdmgr.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply pending EF Core migrations when the host starts. Intended for dev/compose; production runs migrations as an explicit deploy step.</summary>
    public bool MigrateOnStartup { get; set; }
}
