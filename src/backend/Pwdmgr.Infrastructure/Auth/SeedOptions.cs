namespace Pwdmgr.Infrastructure.Auth;

/// <summary>Development-only seed (tenant + one local user). Ignored outside the Development environment.</summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public bool Enabled { get; set; }

    public string TenantSlug { get; set; } = "dev";

    public string TenantDisplayName { get; set; } = "Development tenant";

    public string AdminEmail { get; set; } = "admin@dev.local";

    public string AdminDisplayName { get; set; } = "Dev Admin";

    /// <summary>Must be supplied via configuration/env; there is no built-in default password.</summary>
    public string? AdminPassword { get; set; }
}
